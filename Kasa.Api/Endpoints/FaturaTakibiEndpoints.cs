using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Fatura takibi (bekleyen faturalar, ayın belge dökümü) ve ay sonu muhasebeci listesi (CSV).
/// Yalnız okur; işlemlerin tutarları olduğu gibi gösterilir, kasa/kârlılık hesabı yapılmaz.
/// </summary>
public static class FaturaTakibiEndpoints
{
    public static RouteGroupBuilder MapFaturaTakibiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/faturatakibi", (int yil, int ay, KasaDbContext db) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string hata) return UcNokta.Hata(hata);
            return Results.Ok(FaturaTakibi.Hesapla(db, yil, ay));
        });

        api.MapGet("/disaaktar/muhasebeci.csv", (int yil, int ay, KasaDbContext db) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string hata) return UcNokta.Hata(hata);
            var (bas, son) = FaturaTakibi.AyAraligi(yil, ay);
            var islemler = db.Islemler.AsNoTracking().Where(i => i.Tarih >= bas && i.Tarih <= son)
                .OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
            var ekSayilari = FaturaTakibi.EkSayilari(db, islemler.Select(i => i.Id));
            var kartlar = db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
            return Results.File(FaturaTakibi.MuhasebeciCsv(islemler, ekSayilari, kartlar), CsvYazici.IcerikTipi,
                $"kasa-muhasebeci-{yil:0000}-{ay:00}.csv");
        });

        return api;
    }
}

/// <summary>Fatura takibi hesapları (uç noktalar ve testler ortak kullanır).</summary>
public static class FaturaTakibi
{
    /// <summary>Özet ve CSV'deki belge türü sırası; null = belirtilmemiş (en sonda).</summary>
    public static readonly BelgeTuru?[] TurSirasi =
        [BelgeTuru.EFatura, BelgeTuru.EArsiv, BelgeTuru.Fis, BelgeTuru.Makbuz, BelgeTuru.Belgesiz, null];

    public static string TurAdi(BelgeTuru? t) => t switch
    {
        BelgeTuru.EFatura => "e-Fatura",
        BelgeTuru.EArsiv => "e-Arşiv",
        BelgeTuru.Fis => "Fiş",
        BelgeTuru.Makbuz => "Makbuz",
        BelgeTuru.Belgesiz => "Belgesiz",
        _ => "Belirtilmemiş",
    };

    public static (DateOnly Bas, DateOnly Son) AyAraligi(int yil, int ay)
    {
        var bas = new DateOnly(yil, ay, 1);
        return (bas, bas.AddMonths(1).AddDays(-1));
    }

    public static Dictionary<int, int> EkSayilari(KasaDbContext db, IEnumerable<int> islemIdleri)
    {
        var idler = islemIdleri.Distinct().ToList();
        var sonuc = new Dictionary<int, int>();
        // SQLite parametre sınırına takılmamak için parça parça.
        foreach (var parca in idler.Chunk(500))
            foreach (var g in db.IslemEkleri.AsNoTracking().Where(e => parca.Contains(e.IslemId))
                         .GroupBy(e => e.IslemId).Select(g => new { g.Key, Adet = g.Count() }))
                sonuc[g.Key] = g.Adet;
        return sonuc;
    }

    public static FaturaTakibiDto Hesapla(KasaDbContext db, int yil, int ay)
    {
        var (bas, son) = AyAraligi(yil, ay);
        var bekleyen = db.Islemler.AsNoTracking().Where(i => i.FaturaBekleniyor).ToList();
        var ayinkiler = db.Islemler.AsNoTracking().Where(i => i.Tarih >= bas && i.Tarih <= son).ToList();
        var ekler = EkSayilari(db, bekleyen.Select(i => i.Id));

        FaturaIslemDto Dto(IslemEntity i) => new(i.Id, i.Tarih, i.Cari, i.TutarTl, i.Kanal, i.Tip, i.KrediKartiId,
            i.BelgeTuru, i.BelgeNo, i.FaturaBekleniyor, ekler.GetValueOrDefault(i.Id), i.Not);

        var gruplar = bekleyen
            .GroupBy(i => i.Cari, Metin.EsitBuyukKucukDuyarsiz)
            .Select(g =>
            {
                var sirali = g.OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
                return new FaturaBekleyenCariDto(sirali[0].Cari, sirali.Sum(i => i.TutarTl), sirali.Count,
                    sirali[0].Tarih, sirali.Select(Dto).ToList());
            })
            .OrderBy(g => g.EnEskiTarih).ThenBy(g => g.Cari, Metin.Sirala)
            .ToList();

        var ozet = TurSirasi.Select(t =>
        {
            var l = ayinkiler.Where(i => i.BelgeTuru == t).ToList();
            return new BelgeTuruToplamDto(t, TurAdi(t), l.Sum(i => i.TutarTl), l.Count);
        }).ToList();
        var belgesiz = ozet.Single(o => o.Tur == BelgeTuru.Belgesiz);

        return new FaturaTakibiDto(yil, ay, gruplar, bekleyen.Sum(i => i.TutarTl), bekleyen.Count,
            ozet, ayinkiler.Sum(i => i.TutarTl), ayinkiler.Count, belgesiz.Toplam, belgesiz.Adet);
    }

    /// <summary>
    /// Ay sonu muhasebeci listesi: ayın tüm işlemleri (tarih, id artan) belge bilgisiyle; ardından
    /// belge türü başına toplam satırları ve genel toplam. Biçim <see cref="CsvYazici"/>'dır
    /// (UTF-8 BOM, ';', formül enjeksiyonu koruması).
    /// </summary>
    public static byte[] MuhasebeciCsv(IReadOnlyList<IslemEntity> islemler, IReadOnlyDictionary<int, int> ekSayilari,
        IReadOnlyDictionary<int, string> kartAdlari)
    {
        var csv = new CsvYazici().Baslik("Tarih", "Cari/Kalem", "Kanal", "Tip", "Kart", "Tutar",
            "Belge türü", "Belge no", "Fatura bekleniyor", "Ek sayısı", "Not");
        foreach (var i in islemler)
        {
            var kart = i.KrediKartiId is int k ? kartAdlari.GetValueOrDefault(k) : null;
            csv.Satir(
                CsvYazici.Tarih(i.Tarih),
                CsvYazici.Metin(i.Cari),
                CsvYazici.Metin(i.Kanal),
                CsvYazici.Metin(CsvRaporlari.TipAdi(i.Tip, i.KrediKartiId)),
                CsvYazici.Metin(kart),
                CsvYazici.Sayi(i.TutarTl),
                CsvYazici.Metin(i.BelgeTuru is null ? "" : TurAdi(i.BelgeTuru)),
                CsvYazici.Metin(i.BelgeNo),
                CsvYazici.Metin(i.FaturaBekleniyor ? "Evet" : ""),
                ekSayilari.GetValueOrDefault(i.Id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvYazici.Metin(i.Not));
        }
        csv.Satir();
        foreach (var t in TurSirasi)
        {
            var l = islemler.Where(i => i.BelgeTuru == t).ToList();
            if (l.Count == 0) continue;
            csv.Satir(CsvYazici.Metin($"Toplam: {TurAdi(t)} ({l.Count} işlem)"), "", "", "", "",
                CsvYazici.Sayi(l.Sum(i => i.TutarTl)), "", "", "", "", "");
        }
        var bekleyen = islemler.Where(i => i.FaturaBekleniyor).ToList();
        if (bekleyen.Count > 0)
            csv.Satir(CsvYazici.Metin($"Fatura bekleniyor ({bekleyen.Count} işlem)"), "", "", "", "",
                CsvYazici.Sayi(bekleyen.Sum(i => i.TutarTl)), "", "", "", "", "");
        csv.Satir(CsvYazici.Metin($"Genel toplam ({islemler.Count} işlem)"), "", "", "", "",
            CsvYazici.Sayi(islemler.Sum(i => i.TutarTl)), "", "", "", "", "");
        return csv.Baytlar();
    }
}
