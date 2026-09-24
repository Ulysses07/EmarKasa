using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// POS tanımları, günlük POS satışları ve POS özeti. POS kayıtları YALNIZ BİLGİ AMAÇLIDIR:
/// kasa, kanal devri ve kârlılık hesabına (HesapServisi / HesapMotoru) hiç girmez.
/// Okuma: oturum açmış herkes; yazma: editör.
/// </summary>
public static class PosEndpoints
{
    /// <summary>POS'u bir kanala bağlı olmayan satışların özet etiketi.</summary>
    public const string Kanalsiz = "Kanalsız";

    public static RouteGroupBuilder MapPosEndpoints(this RouteGroupBuilder api)
    {
        // ------------------------------------------------------------------ tanımlar
        api.MapGet("/pos/tanimlar", (KasaDbContext db) =>
        {
            var kanallar = KanalAdlari(db);
            return db.PosTanimlari.AsNoTracking().ToList()
                .OrderByDescending(p => p.Aktif).ThenBy(p => p.Ad, Metin.Sirala)
                .Select(p => TanimDto(p, kanallar)).ToList();
        });

        api.MapPost("/pos/tanimlar", (PosTanimYazDto dto, KasaDbContext db) =>
            UcNokta.Yaz(db, "Bu adla bir POS zaten var.", () =>
        {
            var e = new PosTanimEntity();
            if (TanimUygula(db, dto, e, null) is string hata) return UcNokta.Hata(hata);
            db.PosTanimlari.Add(e); db.SaveChanges();
            return Results.Created($"/api/pos/tanimlar/{e.Id}", TanimDto(e, KanalAdlari(db)));
        })).RequireAuthorization("Editor");

        api.MapPut("/pos/tanimlar/{id:int}", (int id, PosTanimYazDto dto, KasaDbContext db) =>
            UcNokta.Yaz(db, "Bu adla bir POS zaten var.", () =>
        {
            var e = db.PosTanimlari.Find(id);
            if (e is null) return Results.NotFound();
            var eskiKanal = e.KanalId;
            if (TanimUygula(db, dto, e, id) is string hata) return UcNokta.Hata(hata);
            if (dto.EskiSatislaraUygula && eskiKanal != e.KanalId) SatislariTasi(db, e, eskiKanal);
            db.SaveChanges();
            return Results.Ok(TanimDto(e, KanalAdlari(db)));
        })).RequireAuthorization("Editor");

        api.MapDelete("/pos/tanimlar/{id:int}", (int id, KasaDbContext db) =>
            UcNokta.Yaz(db, "POS silinemedi; satış kayıtları olabilir.", () =>
        {
            var e = db.PosTanimlari.Find(id);
            if (e is null) return Results.NotFound();
            if (db.PosSatislari.Any(s => s.PosId == id))
                return Results.Conflict(new { hata = "Bu POS'un satış kayıtları var. Silmek yerine pasif yapın." });
            db.PosTanimlari.Remove(e); db.SaveChanges();
            return Results.NoContent();
        })).RequireAuthorization("Editor");

        // ------------------------------------------------------------------ satışlar
        api.MapGet("/pos/satislar", (DateOnly? baslangic, DateOnly? bitis, int? posId, KasaDbContext db, TimeProvider saat) =>
        {
            var q = db.PosSatislari.AsNoTracking();
            if (baslangic is { } b) q = q.Where(s => s.Tarih >= b);
            if (bitis is { } son) q = q.Where(s => s.Tarih <= son);
            if (posId is { } p) q = q.Where(s => s.PosId == p);
            var bugun = Saat.Bugun(saat);
            var tanimlar = db.PosTanimlari.AsNoTracking().ToDictionary(t => t.Id);
            var kanallar = KanalAdlari(db);
            return q.OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).ToList()
                .Select(s => SatisDto(s, tanimlar, kanallar, bugun)).ToList();
        });

        api.MapPost("/pos/satislar", (PosSatisYazDto dto, KasaDbContext db, TimeProvider saat) =>
            UcNokta.Yaz(db, "POS satışı kaydedilemedi; POS silinmiş olabilir.", () =>
        {
            var e = new PosSatisEntity();
            if (SatisUygula(db, dto, e) is string hata) return UcNokta.Hata(hata);
            db.PosSatislari.Add(e); db.SaveChanges();
            return Results.Created($"/api/pos/satislar/{e.Id}", SatisDto(e, db, saat));
        })).RequireAuthorization("Editor");

        api.MapPut("/pos/satislar/{id:int}", (int id, PosSatisYazDto dto, KasaDbContext db, TimeProvider saat) =>
            UcNokta.Yaz(db, "POS satışı kaydedilemedi; POS silinmiş olabilir.", () =>
        {
            var e = db.PosSatislari.Find(id);
            if (e is null) return Results.NotFound();
            if (SatisUygula(db, dto, e) is string hata) return UcNokta.Hata(hata);
            db.SaveChanges();
            return Results.Ok(SatisDto(e, db, saat));
        })).RequireAuthorization("Editor");

        api.MapDelete("/pos/satislar/{id:int}", (int id, KasaDbContext db) =>
        {
            var e = db.PosSatislari.Find(id);
            if (e is null) return Results.NotFound();
            db.PosSatislari.Remove(e); db.SaveChanges();
            return Results.NoContent();
        }).RequireAuthorization("Editor");

        // ------------------------------------------------------------------ özet
        api.MapGet("/pos/ozet", (int yil, int ay, KasaDbContext db, TimeProvider saat) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string hata) return UcNokta.Hata(hata);
            return Results.Ok(Ozet(db, yil, ay, Saat.Bugun(saat)));
        });

        return api;
    }

    /// <summary>Özet: bugün bloke duran net (tüm satışlar) + seçilen ayın kanal başına komisyonu.</summary>
    public static PosOzetDto Ozet(KasaDbContext db, int yil, int ay, DateOnly bugun)
    {
        var kalemler = Kalemler(db);
        var (bloke, blokeAdet) = PosHesap.Bloke(kalemler, bugun);
        var valorler = PosHesap.BekleyenValorler(kalemler, bugun).Select(v => new PosValorGunuDto(v.Valor, v.Net, v.Adet)).ToList();
        var kanallar = PosHesap.AylikOzet(kalemler, yil, ay)
            .OrderBy(k => k.Kanal == Kanalsiz).ThenBy(k => k.Kanal, Metin.Sirala)
            .Select(k => new PosKanalOzetiDto(k.Kanal, k.Brut, k.Komisyon, k.Net, k.Adet)).ToList();
        var blokeKanallar = PosHesap.BlokeKanallar(kalemler, bugun)
            .OrderBy(k => k.Kanal == Kanalsiz).ThenBy(k => k.Kanal, Metin.Sirala)
            .Select(k => new PosKanalBlokeDto(k.Kanal, k.Net, k.Adet)).ToList();
        return new PosOzetDto(yil, ay, bugun, bloke, blokeAdet, valorler, kanallar,
            kanallar.Sum(k => k.Brut), kanallar.Sum(k => k.Komisyon), kanallar.Sum(k => k.Net), blokeKanallar);
    }

    /// <summary>Hesap kalemleri: kanal, satışa kayıt anında yazılan kanaldır (POS'un bugünkü kanalı değil).</summary>
    private static List<PosKalem> Kalemler(KasaDbContext db)
    {
        var kanallar = KanalAdlari(db);
        return db.PosSatislari.AsNoTracking().OrderBy(s => s.Tarih).ThenBy(s => s.Id).ToList()
            .Select(s => new PosKalem(s.Tarih, KanalAdi(s.KanalId, kanallar), s.BrutTutar, s.KomisyonOrani, s.BlokajGunu))
            .ToList();
    }

    private static Dictionary<int, string> KanalAdlari(KasaDbContext db)
        => db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);

    private static string KanalAdi(int? kanalId, IReadOnlyDictionary<int, string> kanallar)
        => kanalId is int k && kanallar.TryGetValue(k, out var ad) ? ad : Kanalsiz;

    /// <summary>
    /// "Eski satışlara da uygula": POS'un tüm kayıtlı satışlarını yeni kanala taşır (yanlış girilmiş kanalın
    /// düzeltilmesi). Tek geçmiş satırı yazılır; çağıran transaction içindedir ve ardından SaveChanges yapar.
    /// </summary>
    private static void SatislariTasi(KasaDbContext db, PosTanimEntity pos, int? eskiKanal)
    {
        var yeniKanal = pos.KanalId;
        var adet = db.PosSatislari.Where(s => s.PosId == pos.Id && s.KanalId != yeniKanal)
            .ExecuteUpdate(u => u.SetProperty(s => s.KanalId, yeniKanal));
        if (adet == 0) return;
        var kanallar = KanalAdlari(db);
        db.TopluDegisiklikEkle(GecmisTurleri.PosSatisi, pos.Id,
            $"{pos.Ad}: {adet} POS satışının kanalı değişti: {KanalAdi(eskiKanal, kanallar)} → {KanalAdi(yeniKanal, kanallar)}",
            eski: new { posId = pos.Id, kanalId = eskiKanal }, yeni: new { posId = pos.Id, kanalId = yeniKanal, adet });
    }

    private static PosTanimDto TanimDto(PosTanimEntity e, IReadOnlyDictionary<int, string> kanallar)
        => new(e.Id, e.Ad, e.Saglayici, e.KanalId, e.KanalId is int k ? kanallar.GetValueOrDefault(k) : null,
            e.KomisyonOrani, e.BlokajGunu, e.Aktif);

    private static PosSatisDto SatisDto(PosSatisEntity s, KasaDbContext db, TimeProvider saat)
        => SatisDto(s, db.PosTanimlari.AsNoTracking().ToDictionary(t => t.Id), KanalAdlari(db), Saat.Bugun(saat));

    private static PosSatisDto SatisDto(PosSatisEntity s, IReadOnlyDictionary<int, PosTanimEntity> tanimlar,
        IReadOnlyDictionary<int, string> kanallar, DateOnly bugun)
    {
        var t = tanimlar.GetValueOrDefault(s.PosId);
        return new PosSatisDto(s.Id, s.Tarih, s.PosId, t?.Ad ?? $"#{s.PosId}", KanalAdi(s.KanalId, kanallar),
            s.BrutTutar, s.KomisyonOrani, PosHesap.Komisyon(s.BrutTutar, s.KomisyonOrani), PosHesap.Net(s.BrutTutar, s.KomisyonOrani),
            s.BlokajGunu, PosHesap.Valor(s.Tarih, s.BlokajGunu), PosHesap.BlokeMi(s.Tarih, s.BlokajGunu, bugun), s.Not);
    }

    // ------------------------------------------------------------------ doğrulama

    public static string? OranHatasi(decimal oran)
    {
        if (oran < 0 || oran > 100) return "Komisyon oranı 0 ile 100 arasında olmalı.";
        if (decimal.Round(oran, 4) != oran) return "Komisyon oranı en fazla 4 ondalık basamak içerebilir.";
        return null;
    }

    public static string? BlokajHatasi(int gun) => gun is < 0 or > 365 ? "Blokaj günü 0 ile 365 arasında olmalı." : null;

    private static string? TanimUygula(KasaDbContext db, PosTanimYazDto dto, PosTanimEntity e, int? haricId)
    {
        var ad = dto.Ad?.Trim() ?? "";
        if (ad.Length == 0) return "POS adı boş olamaz.";
        if (ad.Length > 100) return "POS adı en fazla 100 karakter olabilir.";
        if (!Enum.IsDefined(dto.Saglayici)) return "Geçersiz sağlayıcı.";
        if (OranHatasi(dto.KomisyonOrani) is string o) return o;
        if (BlokajHatasi(dto.BlokajGunu) is string b) return b;
        if (dto.KanalId is int k && !db.Kanallar.Any(x => x.Id == k)) return "Kanal bulunamadı.";
        var digerleri = db.PosTanimlari.AsNoTracking().Where(p => p.Id != haricId).Select(p => p.Ad).ToList();
        if (digerleri.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))) return $"'{Metin.Kisalt(ad)}' adında bir POS zaten var.";
        e.Ad = ad; e.Saglayici = dto.Saglayici; e.KanalId = dto.KanalId;
        e.KomisyonOrani = dto.KomisyonOrani; e.BlokajGunu = dto.BlokajGunu; e.Aktif = dto.Aktif;
        return null;
    }

    /// <summary>
    /// Satışı doğrular ve uygular. Oran/blokaj boşsa: yeni satışta ya da POS değiştiyse POS tanımındaki
    /// değer, aynı POS'un düzenlemesinde satışın kayıtlı değeri kullanılır. Kanal da aynı kuralla: yeni
    /// satışta ya da POS değiştiyse POS'un bugünkü kanalı, aynı POS'un düzenlemesinde satışın kayıtlı kanalı.
    /// </summary>
    private static string? SatisUygula(KasaDbContext db, PosSatisYazDto dto, PosSatisEntity e)
    {
        if (UcNokta.TarihHatasi(dto.Tarih) is string tar) return tar;
        if (dto.BrutTutar <= 0) return "Brüt tutar sıfırdan büyük olmalı.";
        if (UcNokta.TutarHatasi(dto.BrutTutar, "Brüt tutar") is string th) return th;
        var not = dto.Not?.Trim();
        if (not is { Length: > 1000 }) return "Not en fazla 1000 karakter olabilir.";
        var pos = db.PosTanimlari.AsNoTracking().FirstOrDefault(p => p.Id == dto.PosId);
        if (pos is null) return "POS bulunamadı.";
        var yeniPos = e.Id == 0 || e.PosId != dto.PosId;
        var oran = dto.KomisyonOrani ?? (yeniPos ? pos.KomisyonOrani : e.KomisyonOrani);
        var blokaj = dto.BlokajGunu ?? (yeniPos ? pos.BlokajGunu : e.BlokajGunu);
        if (OranHatasi(oran) is string o) return o;
        if (BlokajHatasi(blokaj) is string b) return b;
        if (yeniPos) e.KanalId = pos.KanalId;
        e.Tarih = dto.Tarih; e.PosId = dto.PosId; e.BrutTutar = dto.BrutTutar;
        e.KomisyonOrani = oran; e.BlokajGunu = blokaj; e.Not = string.IsNullOrEmpty(not) ? null : not;
        return null;
    }
}
