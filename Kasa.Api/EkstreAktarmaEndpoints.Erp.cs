using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>Cari listesinin öğesi: yalnız ERP12'den aktarılan hareketlerin toplamı. <paramref name="Bekleyen"/>: kasada karşılığı verilmemiş kasa hareketi.</summary>
public record ErpCariDto(string Anahtar, string? Kod, string Ad, decimal Borc, decimal Alacak, decimal Bakiye, int HareketSayisi, int Bekleyen, DateOnly SonTarih);
/// <summary>Carinin hareketi; <paramref name="Durum"/> <see cref="ErpHareketDurumlari"/>. Eşleşmişse bağlandığı kayıt <paramref name="EslesmeTuru"/>/<paramref name="EslesmeId"/>.</summary>
public record ErpCariHareketDto(int Id, int BelgeId, int? SatirNo, DateOnly Tarih, string? IslemTuru, string? BelgeNo, string? Aciklama, decimal Borc, decimal Alacak, string Durum,
    string? EslesmeTuru = null, int? EslesmeId = null);

// ERP12 cari hareketleri: yükleme (yinelenen hareket alınmaz), işlenmemiş belgeyi silme ve cari okumaları.
public static partial class EkstreAktarmaEndpoints
{
    private static void MapErp(WebApplication app, RouteGroupBuilder ekstre)
    {
        var api = app.MapGroup("/api/erp").RequireAuthorization("Finans");
        api.MapGet("/cariler", (string? ara, KasaDbContext db) => Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(ErpCariler(db, ara)))));
        api.MapGet("/cariler/hareketler", (string? anahtar, KasaDbContext db) => Safe(() => AlisEndpoints.Oku(db, () =>
        {
            Require(!string.IsNullOrWhiteSpace(anahtar) && anahtar.Length <= 500, "Cari seçin.");
            return Results.Ok(ErpCariHareketleri(db, anahtar));
        })));
        ekstre.MapDelete("/{id:int}/erp", (int id, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () => ErpBelgeSil(db, id))));
    }

    /// <summary>Yüklenen ERP12 belgesini kaydeder; daha önce aktarılmış hareketler alınmaz ve atlananlarda sayılır.</summary>
    private static IResult ErpKaydet(KasaDbContext db, EkstreBelgeEntity d, ErpOkumaSonucu read)
    {
        var mevcut = db.ErpHareketler.AsNoTracking().Select(h => new { h.Ozet, h.Tarih, h.CariAnahtari, h.BelgeNo, h.Borc, h.Alacak, h.BelgeId }).ToList();
        var ozetler = mevcut.Select(h => h.Ozet).ToHashSet(StringComparer.Ordinal);
        var sayac = new Dictionary<string, int>(StringComparer.Ordinal);
        var dosya = read.Satirlar.Select(s =>
        {
            var ilk = ErpEslestirme.Ozet(s, 1);
            var sira = sayac[ilk] = sayac.GetValueOrDefault(ilk) + 1;
            return (Satir: s, Ozet: sira == 1 ? ilk : ErpEslestirme.Ozet(s, sira), Cari: ErpEslestirme.CariAnahtari(s.CariKodu, s.CariAdi));
        }).ToList();
        var yeni = dosya.Where(x => !ozetler.Contains(x.Ozet)).ToList();
        Require(yeni.Count > 0, $"Bu dosyadaki {dosya.Count} hareketin tamamı daha önce aktarılmış; yeni hareket yok.", 409);
        var dosyaOzetleri = dosya.Select(x => x.Ozet).ToHashSet(StringComparer.Ordinal);
        // Aynı cari ve belge numarasının önceki aktarımdaki, bu dosyada artık olmayan hâli: ERP12'de değişmiş olabilir.
        var degisen = mevcut.Where(h => h.BelgeNo is not null && !dosyaOzetleri.Contains(h.Ozet)).ToLookup(h => (h.CariAnahtari, EkstreKuralServisi.Normalize(h.BelgeNo!)));
        var satirlar = new List<EkstreOkunanSatir>();
        var hareketler = new List<ErpHareketEntity>();
        foreach (var (s, ozet, cari) in yeni)
        {
            int? no = null;
            if (s.KasaSatiri)
            {
                no = satirlar.Count + 1;
                var uyarilar = s.Uyarilar.ToList();
                if (s.BelgeNo is { } belgeNo && degisen[(cari, EkstreKuralServisi.Normalize(belgeNo))].FirstOrDefault() is { } eski)
                    uyarilar.Add($"Aynı belge no ({belgeNo}) önce {eski.Tarih:dd.MM.yyyy} · {Math.Max(eski.Borc, eski.Alacak).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} TL olarak aktarılmıştı; ERP12'de değişmiş olabilir.");
                if (s.Yon == "Belirsiz")
                    uyarilar.Add("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.");
                var cek = EkstreExcelOkuyucu.ErpCekSozcugu().IsMatch(EkstreKuralServisi.Normalize($"{s.IslemTuru} {s.Aciklama}"));
                var sinif = s.Nakit ? ErpEslestirme.NakitSinifi : cek ? ErpEslestirme.CekSinifi : ErpEslestirme.BankaSinifi;
                var oneri = !s.Nakit ? EkstreIslemTurleri.Atla : s.Yon switch { "Giris" => EkstreIslemTurleri.Gelir, "Cikis" => EkstreIslemTurleri.Gider, _ => EkstreIslemTurleri.Atla };
                var ek = string.Join(" · ", new[] { s.IslemTuru, s.BelgeNo is null ? null : "Belge " + s.BelgeNo, s.Aciklama }.Where(m => !string.IsNullOrWhiteSpace(m)));
                satirlar.Add(new(no.Value, 1, s.Kaynak, s.Tarih, s.CariAdi, Math.Max(s.Borc, s.Alacak), s.Yon, oneri, sinif, "TRY", uyarilar, s.Yon == "Belirsiz" ? null : YonKaynaklari.Sutun, ek.Length == 0 ? null : ek));
            }
            hareketler.Add(new() { SatirNo = no, Tarih = s.Tarih, CariKodu = s.CariKodu, CariAdi = s.CariAdi, CariAnahtari = cari, IslemTuru = s.IslemTuru, BelgeNo = s.BelgeNo, Aciklama = s.Aciklama, Borc = s.Borc, Alacak = s.Alacak, Ozet = ozet });
        }
        Require(satirlar.Count <= 1500, "Dosya en fazla 1500 kasa hareketi içerebilir; daha kısa tarih aralığı seçin.", 422);
        var belgeUyarilari = read.Uyarilar.ToList();
        var atlananlar = read.Atlananlar.ToList();
        if (yeni.Count < dosya.Count)
        {
            belgeUyarilari.Add($"{dosya.Count - yeni.Count} hareket daha önce aktarıldığı için yeniden alınmadı; {yeni.Count} yeni hareket var.");
            atlananlar.AddRange(dosya.Where(x => ozetler.Contains(x.Ozet)).Take(200).Select(x => new EkstreAtlananSatir(1, x.Satir.Kaynak.Length <= 500 ? x.Satir.Kaynak : x.Satir.Kaynak[..500], "Daha önce aktarıldı")));
        }
        var (ilkGun, sonGun) = (dosya.Min(x => x.Satir.Tarih), dosya.Max(x => x.Satir.Tarih));
        var cariler = dosya.Select(x => x.Cari).ToHashSet(StringComparer.Ordinal);
        var eksik = mevcut.Count(h => h.Tarih >= ilkGun && h.Tarih <= sonGun && cariler.Contains(h.CariAnahtari) && !dosyaOzetleri.Contains(h.Ozet));
        if (eksik > 0)
            belgeUyarilari.Add($"Önceki aktarımlardan {eksik} hareket ({ilkGun:dd.MM.yyyy} – {sonGun:dd.MM.yyyy}, aynı cariler) bu dosyada yok; ERP12'de silinmiş ya da değişmiş olabilir.");
        d.SatirlarJson = Json(satirlar);
        d.UyarilarJson = Json(belgeUyarilari);
        d.AtlananJson = Json(atlananlar);
        db.EkstreBelgeler.Add(d);
        db.SaveChanges();
        foreach (var h in hareketler)
            h.BelgeId = d.Id;
        db.ErpHareketler.AddRange(hareketler);
        db.SaveChanges();
        return Results.Ok(Document(db, d.Id));
    }

    /// <summary>Yanlış yüklenen, hiçbir satırı kaydedilmemiş (iptal edilmişler dahil) ERP12 belgesini hareketleriyle siler.</summary>
    private static IResult ErpBelgeSil(KasaDbContext db, int id)
    {
        var d = GetDocument(db, id);
        Require(d.Kaynak == EkstreKaynaklari.Erp, "Yalnız ERP12 belgesi silinebilir.", 409);
        Require(!db.EkstreKayitlar.Any(k => k.BelgeId == id), "Eşleştirilmiş ya da kaydedilmiş satırı olan ERP12 belgesi silinemez.", 409);
        db.EkstreYokSayilanlar.RemoveRange(db.EkstreYokSayilanlar.Where(y => y.BelgeId == id));
        db.ErpHareketler.RemoveRange(db.ErpHareketler.Where(h => h.BelgeId == id));
        db.EkstreBelgeler.Remove(d);
        db.SaveChanges();
        return Results.NoContent();
    }

    /// <summary>ERP12 satırının eşleşme hedefi: adaylar arasında olmalı (aynı yön ve tutar; nakit ekstreyle, çek yalnız çekle).</summary>
    private static void ValidateErpMatch(KasaDbContext db, EkstreSatirYaz row, EkstreOkunanSatir original)
    {
        var havuz = new ErpEslestirme.Havuz(db, row.Tarih, row.Tarih, new HashSet<(string, int)>());
        Require(original.Yon is "Giris" or "Cikis", "Yönü belli olmayan ERP12 satırı eşleştirilmez; ERP12'den düzeltip yeniden aktarın.");
        Require(havuz.Bul(original.Sinif, original.Yon, row.Tarih, row.Tutar).Any(a => a.Tur == row.EslesenKayitTuru && a.Id == row.EslesenKayitId),
            "Seçilen kayıt bu ERP12 satırıyla eşleşmiyor: yönü ve tutarı aynı, tarihi en çok 3 gün farklı olmalı (nakit satır ekstre kaydıyla, çek satırı yalnız çekle eşleşir).");
        Require(!EslesmisHedefler(db, EkstreKaynaklari.Erp).Contains((row.EslesenKayitTuru!, row.EslesenKayitId!.Value)), "Bu kayıt başka bir ERP12 satırıyla eşleşti.", 409);
    }

    private static List<EkstreEslesmeAdayiDto> ErpAdaylari(KasaDbContext db, EkstreBelgeEntity doc, EkstreEslesmeAdayiSorgu dto)
    {
        var satir = Read<EkstreOkunanSatir>(doc.SatirlarJson).SingleOrDefault(r => r.No == dto.SatirNo);
        Require(satir is not null, "Kaynak satır bu belgede bulunamadı.");
        if (satir.Yon is not ("Giris" or "Cikis"))
            return [];
        return new ErpEslestirme.Havuz(db, dto.Tarih, dto.Tarih, EslesmisHedefler(db, EkstreKaynaklari.Erp)).Bul(satir.Sinif, satir.Yon, dto.Tarih, dto.Tutar).Select(ErpEslestirme.Dto).ToList();
    }

    private static List<ErpCariDto> ErpCariler(KasaDbContext db, string? ara)
    {
        var kapali = ErpKapali(db);
        var aranan = EkstreKuralServisi.Normalize(ara ?? "");
        return db.ErpHareketler.AsNoTracking().Select(h => new { h.Id, h.BelgeId, h.SatirNo, h.Tarih, h.CariKodu, h.CariAdi, h.CariAnahtari, h.Borc, h.Alacak }).ToList()
            .GroupBy(h => h.CariAnahtari).Select(g =>
            {
                var son = g.OrderByDescending(h => h.Tarih).ThenByDescending(h => h.Id).First();
                var (borc, alacak) = (g.Sum(h => h.Borc), g.Sum(h => h.Alacak));
                return new ErpCariDto(g.Key, son.CariKodu, son.CariAdi, borc, alacak, borc - alacak, g.Count(), g.Count(h => h.SatirNo is { } no && !kapali.ContainsKey((h.BelgeId, no))), son.Tarih);
            })
            .Where(c => aranan.Length == 0 || EkstreKuralServisi.Normalize(c.Ad).Contains(aranan, StringComparison.Ordinal) || EkstreKuralServisi.Normalize(c.Kod ?? "").Contains(aranan, StringComparison.Ordinal))
            .OrderBy(c => c.Ad, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), true)).ThenBy(c => c.Anahtar, StringComparer.Ordinal).ToList();
    }

    private static List<ErpCariHareketDto> ErpCariHareketleri(KasaDbContext db, string anahtar)
    {
        var kapali = ErpKapali(db);
        return db.ErpHareketler.AsNoTracking().Where(h => h.CariAnahtari == anahtar).ToList().OrderByDescending(h => h.Tarih).ThenByDescending(h => h.Id).Select(h =>
        {
            if (h.SatirNo is not { } no)
                return Hareket(h, ErpHareketDurumlari.KasaDisi);
            return kapali.TryGetValue((h.BelgeId, no), out var k) ? Hareket(h, k.Durum, k.EslesmeTuru, k.EslesmeId) : Hareket(h, ErpHareketDurumlari.Bekliyor);
        }).ToList();
        static ErpCariHareketDto Hareket(ErpHareketEntity h, string durum, string? tur = null, int? id = null) =>
            new(h.Id, h.BelgeId, h.SatirNo, h.Tarih, h.IslemTuru, h.BelgeNo, h.Aciklama, h.Borc, h.Alacak, durum, tur, id);
    }

    /// <summary>ERP12 belgelerinin kapanmış satırları: eşleşti, kaydedildi ya da yok sayıldı.</summary>
    private static Dictionary<(int Belge, int Satir), (string Durum, string? EslesmeTuru, int? EslesmeId)> ErpKapali(KasaDbContext db)
    {
        var belgeler = db.EkstreBelgeler.Where(b => b.Kaynak == EkstreKaynaklari.Erp).Select(b => b.Id);
        var sonuc = db.EkstreYokSayilanlar.AsNoTracking().Where(y => belgeler.Contains(y.BelgeId)).ToList().ToDictionary(y => (y.BelgeId, y.SatirNo), _ => (ErpHareketDurumlari.YokSayildi, (string?)null, (int?)null));
        foreach (var k in db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && belgeler.Contains(k.BelgeId)).Select(k => new { k.BelgeId, k.SatirNo, k.IslemTuru, k.EslesmeTuru, k.EslesmeId }).ToList())
            sonuc[(k.BelgeId, k.SatirNo)] = k.IslemTuru == EkstreIslemTurleri.Eslestir ? (ErpHareketDurumlari.Eslesti, k.EslesmeTuru, k.EslesmeId) : (ErpHareketDurumlari.Kaydedildi, null, null);
        return sonuc;
    }
}
