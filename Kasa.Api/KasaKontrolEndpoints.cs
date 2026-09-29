using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class KasaKontrolEndpoints
{
    /// <summary>Kontrol listesinde gösterilen en yeni kayıt sayısı.</summary>
    public const int ListeAdedi = 50;
    /// <summary>"Kontrolden beri değişenler"deki her listenin en çok öğe sayısı (en eskiler; aşılırsa yanıt kırpıldı der).</summary>
    public const int SonrasiSiniri = 500;
    /// <summary>Kasa hareket dökümünün en uzun aralığı (gün, iki uç dahil).</summary>
    public const int DokumEnFazlaGun = 366;
    public const string NotZorunluIletisi = "Fark varsa açıklama girin.";

    /// <summary>"Kontrolden beri değişenler"e girmeyen denetim olayları: kasayı değiştirmeyen oturum/güvenlik olayları, kasa
    /// kontrolünün kendisi ve açıklaması, kanal alt sınırı ve alıcı hesabı.</summary>
    private static readonly string[] SonrasiDisi = [GuvenlikOlaylari.Varlik, "KasaKontrol", "KasaEsik", "Alici"];

    public static WebApplication MapKasaKontrolEndpoints(this WebApplication app)
    {
        // Okuma: bakiye ve eşikler aynı salt okunur anlık görüntüden; yazma kilidi alınmaz.
        app.MapGet("/api/kasa-esikleri", (KasaDbContext db, HesapServisi hesap, CancellationToken ct) =>
            AlisEndpoints.Oku(db, () => Results.Ok(Esikler(db, hesap.Panel(ct))))).RequireAuthorization("Finans");

        app.MapPut("/api/kasa-esikleri/{kanalId:int}", (int kanalId, KasaEsikYaz dto, KasaDbContext db, HesapServisi hesap) => AlisEndpoints.Mutate(db, () =>
        {
            var channel = db.Kanallar.Find(kanalId);
            if (channel is null) return Results.NotFound();
            var v = new GirdiDogrulama(); v.Para(dto.Tutar, "tutar");
            if (v.Sonuc() is { } error) return error;
            var limit = db.KasaEsikleri.SingleOrDefault(x => x.KanalId == kanalId);
            if (dto.Surum != (limit?.Surum ?? 0)) return AlisEndpoints.Conflict("Alt sınır değişmiş. Yenileyip tekrar deneyin.");
            if (limit is null) { limit = new() { KanalId = kanalId, Surum = 0 }; db.KasaEsikleri.Add(limit); }
            // Bir eşik değişikliği mevcut düşük bakiye olayını tekrar tekrar üretmez.
            if (!dto.Etkin) { limit.AlarmAcik = false; limit.UyariTarihi = null; }
            limit.Tutar = dto.Tutar; limit.Etkin = dto.Etkin; limit.Surum++;
            db.SaveChanges();
            var balance = hesap.Panel().Kanallar.Single(k => k.KanalId == kanalId).Bakiye;
            return Results.Ok(new KasaEsikDto(kanalId, channel.Ad, limit.Surum, limit.Tutar, limit.Etkin, balance, limit.Etkin && balance < limit.Tutar));
        })).RequireAuthorization("Editor");

        // Liste: her kaydın günü için genel kasa bugünkü veriyle yeniden hesaplanır (gap-coklu-giris-cift-sayim-mutabakat-17);
        // bütün kayıtlar tek dökümden (tek rapor yüklemesi) hesaplanır.
        app.MapGet("/api/kasa-kontrol", (KasaDbContext db, HesapServisi hesap, CancellationToken ct) => AlisEndpoints.Oku(db, () =>
        {
            var rows = db.KasaKontrolleri.AsNoTracking().OrderByDescending(x => x.Id).Take(ListeAdedi).ToList();
            var dokum = rows.Count == 0 ? null : hesap.Dokum(ct);
            return Results.Ok(rows.Select(row => ToDto(row, dokum)).ToList());
        })).RequireAuthorization("Finans");
        app.MapPost("/api/kasa-kontrol/onizleme", (KasaKontrolOnizle dto, KasaDbContext db, HesapServisi hesap, CancellationToken ct) => AlisEndpoints.Oku(db, () =>
        {
            if (Validate(dto.GercekBakiye, dto.Not) is { } error) return error;
            var takip = new TakipHesapBaglami(db, ct);
            return Results.Ok(Preview(hesap.Panel(takip), takip.Bugun, dto.GercekBakiye));
        })).RequireAuthorization("Editor");
        app.MapPost("/api/kasa-kontrol", (KasaKontrolYaz dto, KasaDbContext db, HesapServisi hesap, TimeProvider saat) => AlisEndpoints.Mutate(db, () =>
        {
            if (Validate(dto.GercekBakiye, dto.Not) is { } error) return error;
            var digest = FinansHesaplari.Ozet(dto with { IstekId = Guid.Empty });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "KasaKontrol", digest, id => Results.Ok(ToDto(db.KasaKontrolleri.Single(x => x.Id == id)))) is { } replay) return replay;
            var takip = new TakipHesapBaglami(db);
            var preview = Preview(hesap.Panel(takip), takip.Bugun, dto.GercekBakiye);
            if (dto.KontrolOzeti != preview.KontrolOzeti) return AlisEndpoints.Conflict("Kasa bakiyesi veya karşılaştırma bilgileri değişti. Yeniden karşılaştırın.");
            // Özet tuttu: fark kullanıcının önizlemede gördüğü farktır. Açıklama önizlemeden sonra da yazılabilir (özete girmez).
            if (preview.Fark != 0 && string.IsNullOrWhiteSpace(dto.Not))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["not"] = [NotZorunluIletisi] });
            // Filigran: panel ile aynı yazma transaction'ında (BEGIN IMMEDIATE) okunur; arada başka yazma olamaz. Bu kaydın kendi
            // denetim olayı ve mali isteği bu kimliklerden sonra yazılır.
            var row = new KasaKontrolEntity { Kaydedildi = saat.GetUtcNow().ToUnixTimeMilliseconds(),
                SistemBakiye = preview.SistemBakiye, GercekBakiye = dto.GercekBakiye, Fark = preview.Fark, Not = dto.Not?.Trim(),
                HesapTarihi = takip.Bugun, KanalBakiyeleriJson = KanallariYaz(preview.KanalBakiyeleri!),
                SonIslemId = db.Islemler.Max(i => (int?)i.Id) ?? 0, SonFinansIstekId = db.FinansIstekler.Max(f => (int?)f.Id) ?? 0,
                SonDenetimOlayId = db.DenetimOlaylari.Max(o => (int?)o.Id) ?? 0 };
            db.KasaKontrolleri.Add(row); db.SaveChanges();
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "KasaKontrol", digest, row.Id); db.SaveChanges();
            return Results.Ok(ToDto(row));
        })).RequireAuthorization("Editor");
        // Farkın sonradan açıklanması: yalnız açıklama ve sürüm değişir; tutarlar ve filigran aynen kalır.
        app.MapPut("/api/kasa-kontrol/{id:int}/aciklama", (int id, KasaKontrolAciklamaYaz dto, KasaDbContext db, TimeProvider saat) => AlisEndpoints.Mutate(db, () =>
        {
            var v = new GirdiDogrulama(); v.Metin(dto.Aciklama, "aciklama", 2000);
            if (v.Sonuc() is { } error) return error;
            var aciklama = dto.Aciklama.Trim();
            var digest = FinansHesaplari.Ozet(new { id, dto.Surum, aciklama });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "KasaKontrolAciklama", digest, sonuc => Results.Ok(ToDto(db.KasaKontrolleri.AsNoTracking().Single(x => x.Id == sonuc)))) is { } replay) return replay;
            var row = db.KasaKontrolleri.SingleOrDefault(x => x.Id == id);
            if (row is null) return Results.NotFound();
            if (dto.Surum != row.Surum) return AlisEndpoints.Conflict("Kontrol kaydı değişmiş. Yenileyin.");
            row.FarkAciklamasi = aciklama; row.FarkAciklamaZamani = saat.GetUtcNow().ToUnixTimeMilliseconds(); row.Surum++;
            // İstek kaydı aynı kayıtta: değişikliğin denetim olayı istek kimliğini taşır.
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "KasaKontrolAciklama", digest, row.Id);
            db.SaveChanges();
            return Results.Ok(ToDto(row));
        })).RequireAuthorization("Editor");
        // "Bu kontrolden beri değişenler" (gap-denetim-izi-gozlemlenebilirlik-3): filigrandan sonraki denetim olayları ve mali istekler,
        // kontrol gününden bugüne kasaya işleyen hareketler (kendiliğinden işleyenler işaretli) ve kontrolden sonra girilip geçmişe
        // düşen giderler. Denetim izi IP ve gerekçe içerir: yalnız editör.
        app.MapGet("/api/kasa-kontrol/{id:int}/sonrasi", (int id, KasaDbContext db, HesapServisi hesap, CancellationToken ct) => AlisEndpoints.Oku(db, () =>
        {
            var row = db.KasaKontrolleri.AsNoTracking().SingleOrDefault(x => x.Id == id);
            if (row is null) return Results.NotFound();
            var dokum = hesap.Dokum(ct);
            var gun = EsasTarih(row);
            var olaylar = db.DenetimOlaylari.AsNoTracking().Where(o => !SonrasiDisi.Contains(o.Varlik) && o.Tur != "GecmisKayit");
            // Filigransız (eski) kayıtta sıra kimliği yok: kayıt anından sonraki olaylar.
            var kaydedildi = row.Kaydedildi;
            olaylar = row.SonDenetimOlayId is { } sonOlay ? olaylar.Where(o => o.Id > sonOlay) : olaylar.Where(o => o.ZamanUtc > kaydedildi);
            var degisiklikler = olaylar.OrderBy(o => o.Id).Take(SonrasiSiniri + 1).ToList();
            var istekler = row.SonFinansIstekId is { } sonIstek
                ? db.FinansIstekler.AsNoTracking().Where(f => f.Id > sonIstek && f.Tur != "KasaKontrol" && f.Tur != "KasaKontrolAciklama")
                    .OrderBy(f => f.Id).Take(SonrasiSiniri + 1).ToList()
                : [];
            var hareketler = dokum.Hareketler.Where(h => h.EtkiTarihi > gun || row.SonIslemId is { } sonIslem && KasaDokumu.IslemId(h) > sonIslem)
                .Take(SonrasiSiniri + 1).ToList();
            var kirpildi = degisiklikler.Count > SonrasiSiniri || istekler.Count > SonrasiSiniri || hareketler.Count > SonrasiSiniri;
            return Results.Ok(new KasaKontrolSonrasiDto(row.Id, Zaman(row.Kaydedildi), gun, row.HesapTarihi is not null, row.SistemBakiye,
                dokum.GenelKasa(gun), dokum.GenelKasa(dokum.Bugun),
                degisiklikler.Take(SonrasiSiniri).Select(DenetimEndpoints.Dto).ToList(),
                istekler.Take(SonrasiSiniri).Select(f => new KasaKontrolIstekDto(f.IstekId, f.Tur, f.SonucId)).ToList(),
                hareketler.Take(SonrasiSiniri).Select(dokum.Dto).ToList(), kirpildi));
        })).RequireAuthorization("Editor");
        // Kasa hareket dökümü: genel kasayı (kanalId verilirse o kanalın kasasını) oluşturan bütün hareketler kaynağıyla; bitiş en geç
        // bugündür (gelecek tarihli hareket kasaya girmemiştir). Varsayılan aralık bitiş ayının başından bitişe.
        app.MapGet("/api/kasa-hareketleri", (DateOnly? baslangic, DateOnly? bitis, int? kanalId, KasaDbContext db, HesapServisi hesap, CancellationToken ct) => AlisEndpoints.Oku(db, () =>
        {
            var takip = new TakipHesapBaglami(db, ct);
            var son = bitis is { } b && b < takip.Bugun ? b : takip.Bugun;
            var ilk = baslangic ?? new DateOnly(son.Year, son.Month, 1);
            var v = new GirdiDogrulama();
            v.Kontrol(ilk <= son, "baslangic", "Başlangıç, bitişten (en geç bugün) sonra olamaz.");
            v.Kontrol(son.DayNumber - ilk.DayNumber < DokumEnFazlaGun, "bitis", $"Döküm en fazla {DokumEnFazlaGun} günü kapsar.");
            v.Kontrol(kanalId is not { } k || db.Kanallar.Any(x => x.Id == k), "kanalId", "Kanal bulunamadı.");
            if (v.Sonuc() is { } error) return error;
            var dokum = hesap.Dokum(takip);
            var kanal = kanalId is { } kid ? dokum.KanalAdi(kid) : null;
            var satirlar = dokum.Hareketler.Where(h => h.EtkiTarihi >= ilk && h.EtkiTarihi <= son && (kanal is null || h.Kanal == kanal)).Select(dokum.Dto).ToList();
            var (acilis, kapanis) = kanalId is { } id
                ? (dokum.KanalBakiyesiOnce(id, ilk) ?? 0m, dokum.KanalBakiyesi(id, son) ?? 0m)
                : (dokum.GenelKasaOnce(ilk), dokum.GenelKasa(son));
            return Results.Ok(new KasaHareketleriDto(ilk, son, kanalId, kanal, acilis, kapanis, satirlar));
        })).RequireAuthorization("Finans");
        return app;
    }

    /// <summary>Kanal eşikleri ve verilen paneldeki bakiyeler (aynı anlık görüntüde okunmuş olmalı).</summary>
    internal static List<KasaEsikDto> Esikler(KasaDbContext db, PanelDto panel)
    {
        var balances = panel.Kanallar.ToDictionary(k => k.KanalId ?? 0, k => k.Bakiye);
        var limits = db.KasaEsikleri.AsNoTracking().ToDictionary(x => x.KanalId);
        return db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList().Select(k =>
        {
            limits.TryGetValue(k.Id, out var limit);
            var balance = balances.GetValueOrDefault(k.Id);
            return new KasaEsikDto(k.Id, k.Ad, limit?.Surum ?? 0, limit?.Tutar ?? 0, limit?.Etkin ?? false, balance,
                limit is { Etkin: true } && balance < limit.Tutar);
        }).ToList();
    }

    private static IResult? Validate(decimal actual, string? note)
    { var v = new GirdiDogrulama(); v.Para(actual, "gercekBakiye", negatifOlabilir: true); v.Metin(note, "not", 2000, zorunlu: false); return v.Sonuc(); }

    /// <summary>Karşılaştırma: panelin genel kasası ve kanal kasaları. Özet açıklamayı içermez: açıklama önizlemeden sonra
    /// (fark görülünce) yazılabilir; bakiye ya da kanal bakiyesi değişirse kayıt 409 alır.</summary>
    private static KasaKontrolOnizlemeDto Preview(PanelDto panel, DateOnly hesapTarihi, decimal actual)
    {
        var system = panel.GuncelKasa;
        var kanallar = panel.Kanallar.Select(k => new KasaKontrolKanalDto(k.KanalId, k.Kanal, k.Bakiye)).ToList();
        return new(system, actual, actual - system, FinansHesaplari.Ozet(new { system, actual, kanallar = kanallar.Select(k => new { k.KanalId, k.Bakiye }) }),
            kanallar, hesapTarihi);
    }

    /// <summary>Listede <paramref name="dokum"/> verilir: kaydın günü için genel kasa ve kanal kasaları bugünkü veriyle yeniden
    /// hesaplanır. Kayıt ve açıklama yanıtlarında güncel alanlar boştur.</summary>
    private static KasaKontrolDto ToDto(KasaKontrolEntity row, KasaDokumu? dokum = null)
    {
        var kanallar = KanallariOku(row.KanalBakiyeleriJson);
        decimal? guncel = null;
        if (dokum is not null)
        {
            var gun = EsasTarih(row);
            guncel = dokum.GenelKasa(gun);
            kanallar = kanallar?.Select(k => k with { GuncelBakiye = k.KanalId is { } id ? dokum.KanalBakiyesi(id, gun) : null }).ToList();
        }
        return new(row.Id, Zaman(row.Kaydedildi), row.SistemBakiye, row.GercekBakiye, row.Fark, row.Not, row.Surum, row.HesapTarihi, kanallar,
            row.FarkAciklamasi, row.FarkAciklamaZamani is { } z ? Zaman(z) : null, guncel, row.GercekBakiye - guncel, guncel is { } g && g != row.SistemBakiye);
    }

    /// <summary>Kontrolün esas günü: hesap günü; filigransız (eski) kayıtta kayıt anının İstanbul günü (panel "bugün"ü o gündü).</summary>
    private static DateOnly EsasTarih(KasaKontrolEntity row) => row.HesapTarihi
        ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(row.Kaydedildi), KasaSaati.Istanbul).DateTime);

    private static DateTimeOffset Zaman(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    /// <summary>Kanal bakiyelerinin saklanan biçimi (güncel bakiye saklanmaz).</summary>
    internal sealed record KanalBakiyeKaydi(int? KanalId, string Kanal, decimal Bakiye);

    private static string KanallariYaz(IEnumerable<KasaKontrolKanalDto> kanallar) =>
        JsonSerializer.Serialize(kanallar.Select(k => new KanalBakiyeKaydi(k.KanalId, k.Kanal, k.Bakiye)).ToList());

    /// <summary>Saklanan kanal bakiyeleri; filigransız kayıtta ya da okunamayan değerde null (liste düşmez).</summary>
    private static List<KasaKontrolKanalDto>? KanallariOku(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<List<KanalBakiyeKaydi>>(json)?.Where(k => k is not null).Select(k => new KasaKontrolKanalDto(k.KanalId, k.Kanal, k.Bakiye)).ToList(); }
        catch (JsonException) { return null; }
    }
}
