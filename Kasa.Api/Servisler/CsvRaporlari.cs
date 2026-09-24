using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Servisler;

/// <summary>
/// "Excel'e aktar" çıktıları: işlem listesi ve raporlar, ekrandaki / JSON uç noktalarındaki
/// rakamların aynısıyla CSV'ye yazılır (bkz. <see cref="CsvYazici"/>). Hesap yapılmaz; yalnız biçimlenir.
/// </summary>
public static class CsvRaporlari
{
    /// <summary>İşlem tipinin Türkçe adı. Karta bağlı işlem, kayıtlı tipi ne olursa olsun K.K'dir (hesaptaki gibi).</summary>
    public static string TipAdi(GiderTipi tip, int? krediKartiId) => (krediKartiId is not null ? GiderTipi.KrediKarti : tip) switch
    {
        GiderTipi.SabitGider => "Sabit gider",
        GiderTipi.KrediKarti => "Kredi kartı",
        _ => "Cari",
    };

    /// <summary>
    /// İşlem listesi (GET /api/islemler ile aynı sıra: tarih, id artan) + sonda toplam satırı.
    /// Sütunlar: Tarih; Cari/Kalem; Kanal; Tip; Kart; Tutar; Not.
    /// </summary>
    public static byte[] Islemler(IReadOnlyList<IslemEntity> islemler, IReadOnlyDictionary<int, string> kartAdlari)
    {
        var csv = new CsvYazici().Baslik("Tarih", "Cari/Kalem", "Kanal", "Tip", "Kart", "Tutar", "Not");
        foreach (var i in islemler)
        {
            var kart = i.KrediKartiId is int k ? kartAdlari.GetValueOrDefault(k) : null;
            csv.Satir(
                CsvYazici.Tarih(i.Tarih),
                CsvYazici.Metin(i.Cari),
                CsvYazici.Metin(i.Kanal),
                CsvYazici.Metin(TipAdi(i.Tip, i.KrediKartiId)),
                CsvYazici.Metin(kart),
                CsvYazici.Sayi(i.TutarTl),
                CsvYazici.Metin(i.Not));
        }
        csv.Satir(CsvYazici.Metin($"Toplam ({islemler.Count} işlem)"), "", "", "", "",
            CsvYazici.Sayi(islemler.Sum(i => i.TutarTl)), "");
        return csv.Baytlar();
    }

    /// <summary>Haftalık raporda kasa satırının "Kanal" sütunundaki etiketi.</summary>
    public const string KasaSatiri = "Kasa (toplam)";

    /// <summary>
    /// Haftalık rapor (GET /api/rapor/haftalik ile aynı rakamlar): her dönem için kanal başına bir
    /// satır (gelen, cari giden, sonuç, devir), ardından o dönemin kasa satırı (toplam gelen,
    /// toplam giden, kasa sonucu, kasa devri). Çek tahsilatı/ödemesi son iki sütundadır (sonuç ve
    /// devre dahildir; gelen/giden sütunları çek içermez).
    /// </summary>
    public static byte[] Haftalik(IReadOnlyList<HaftalikOzet> ozetler)
    {
        var csv = new CsvYazici().Baslik("Dönem başı", "Dönem sonu", "Kanal", "Gelen", "Giden", "Sonuç", "Devir", "Çek tahsilat", "Çek ödeme");
        foreach (var o in ozetler)
        {
            var bas = CsvYazici.Tarih(o.Donem.Start);
            var son = CsvYazici.Tarih(o.Donem.End);
            foreach (var k in o.Kanallar)
                csv.Satir(bas, son, CsvYazici.Metin(k.Kanal),
                    CsvYazici.Sayi(k.Gelen), CsvYazici.Sayi(k.Giden), CsvYazici.Sayi(k.Sonuc), CsvYazici.Sayi(k.Devir),
                    CsvYazici.Sayi(k.CekGelen), CsvYazici.Sayi(k.CekGiden));
            csv.Satir(bas, son, CsvYazici.Metin(KasaSatiri),
                CsvYazici.Sayi(o.ToplamGelen), CsvYazici.Sayi(o.ToplamGiden), CsvYazici.Sayi(o.KasaSonucu), CsvYazici.Sayi(o.KasaDevir),
                CsvYazici.Sayi(o.ToplamCekGelen), CsvYazici.Sayi(o.ToplamCekGiden));
        }
        return csv.Baytlar();
    }

    /// <summary>
    /// Aylık rapor (GET /api/rapor/aylik ile aynı rakamlar): kanal başına tüm sütunlar + toplam satırı.
    /// Çek tahsilatı/ödemesi son iki sütundadır (ay sonucuna dahildir).
    /// </summary>
    public static byte[] Aylik(AylikRapor rapor)
    {
        var csv = new CsvYazici().Baslik("Kanal", "Gelen", "Cari giden", "Sabit gider", "Kredi kartı", "Ortak pay", "Ay sonucu", "Çek tahsilat", "Çek ödeme");
        foreach (var k in rapor.Kanallar)
            csv.Satir(CsvYazici.Metin(k.Kanal),
                CsvYazici.Sayi(k.Gelen), CsvYazici.Sayi(k.CariGiden), CsvYazici.Sayi(k.SabitGider),
                CsvYazici.Sayi(k.KrediKarti), CsvYazici.Sayi(k.OrtakPay), CsvYazici.Sayi(k.AySonucu),
                CsvYazici.Sayi(k.CekGelen), CsvYazici.Sayi(k.CekGiden));
        var l = rapor.Kanallar;
        csv.Satir(CsvYazici.Metin("Toplam"),
            CsvYazici.Sayi(l.Sum(k => k.Gelen)), CsvYazici.Sayi(l.Sum(k => k.CariGiden)), CsvYazici.Sayi(l.Sum(k => k.SabitGider)),
            CsvYazici.Sayi(l.Sum(k => k.KrediKarti)), CsvYazici.Sayi(l.Sum(k => k.OrtakPay)), CsvYazici.Sayi(l.Sum(k => k.AySonucu)),
            CsvYazici.Sayi(l.Sum(k => k.CekGelen)), CsvYazici.Sayi(l.Sum(k => k.CekGiden)));
        return csv.Baytlar();
    }

    /// <summary>
    /// İşlem dosyasının adı filtreden türer: tam bir takvim ayı → kasa-islemler-2026-09.csv; başka
    /// aralık → kasa-islemler-2026-09-01_2026-09-15.csv; filtre yok → kasa-islemler-tumu.csv. Kanal
    /// ve cari filtresi ASCII'ye sadeleştirilip eklenir.
    /// </summary>
    public static string IslemDosyaAdi(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari)
    {
        var aralik = (baslangic, bitis) switch
        {
            ({ } b, { } s) when b.Day == 1 && s == b.AddMonths(1).AddDays(-1)
                => b.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            ({ } b, { } s) when b == s => CsvYazici.DosyaTarihi(b),
            ({ } b, { } s) => $"{CsvYazici.DosyaTarihi(b)}_{CsvYazici.DosyaTarihi(s)}",
            ({ } b, null) => $"{CsvYazici.DosyaTarihi(b)}-sonrasi",
            (null, { } s) => $"{CsvYazici.DosyaTarihi(s)}-oncesi",
            _ => "tumu",
        };
        var ad = "kasa-islemler-" + aralik;
        foreach (var parca in new[] { kanal, cari })
            if (CsvYazici.DosyaAdiParcasi(parca) is { Length: > 0 } p) ad += "-" + p;
        return ad + ".csv";
    }
}
