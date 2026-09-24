using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Grafik birimi. Nominal dışındakiler ayın kur/endeks satırını ister; satır yoksa "kur yok".</summary>
public enum GrafikBirimi { NominalTl, ReelTl, Usd, Eur, AltinGram }

/// <summary>Çizilen ölçü: kanal geliri (gelen + çek tahsilatı) ya da ay sonucu.</summary>
public enum GrafikOlcusu { Gelir, AySonucu }

/// <summary>Bir ayın çubuğu: bu yıl ve geçen yılın aynı ayı (dönüştürülmüş; bilinmiyorsa null).</summary>
/// <param name="Deger">Seçili birimde değer; kur yoksa ya da takip öncesiyse null.</param>
/// <param name="GecenYil">Bir yıl önceki aynı ay (aynı kurallarla).</param>
public sealed record GrafikCubugu(int Yil, int Ay, string Etiket, decimal? Deger, bool KurYok, bool TakipOncesi,
    decimal? GecenYil, bool GecenYilKurYok, bool GecenYilTakipOncesi)
{
    /// <summary>Tablo metni: tutar, "kur yok" ya da "takip öncesi".</summary>
    public string DegerMetni(GrafikBirimi b) => Metin(Deger, KurYok, TakipOncesi, b);
    public string GecenYilMetni(GrafikBirimi b) => Metin(GecenYil, GecenYilKurYok, GecenYilTakipOncesi, b);

    /// <summary>Geçen yıla göre % değişim (ikisi de biliniyor ve geçen yıl ≠ 0 ise; 1 ondalık).</summary>
    public decimal? DegisimYuzdesi => Deger is { } d && GecenYil is { } g && g != 0m
        ? decimal.Round((d - g) / Math.Abs(g) * 100m, 1, MidpointRounding.AwayFromZero) : null;

    internal static string Metin(decimal? d, bool kurYok, bool takipOncesi, GrafikBirimi b)
        => takipOncesi ? GrafikVerisi.TakipOncesiMetni
           : kurYok ? GrafikVerisi.KurYokMetni
           : d is { } v ? GrafikVerisi.Bicimle(v, b) : "—";
}

/// <summary>Grafik tablosunun satırı (sayfada gösterilir; çizimle aynı veri).</summary>
public sealed record GrafikSatiri(string Etiket, string Deger, string GecenYil, string Degisim, decimal RenkDegeri, bool KurYok);

/// <summary>
/// Grafik verisinin biçimlenmesi (saf; birim testli). Hiçbir kur ya da endeks uydurulmaz: dönüşüm için
/// gereken değer yoksa sonuç null'dır ve "kur yok" yazılır.
/// <list type="bullet">
/// <item>Reel TL = nominal × TÜFE(referans) / TÜFE(ay). Referans, seçilen aya kadar TÜFE'si girilmiş en son aydır.</item>
/// <item>USD / EUR / gram altın = nominal ÷ ayın kuru.</item>
/// </list>
/// </summary>
public static class GrafikVerisi
{
    public const string KurYokMetni = "kur yok";
    public const string TakipOncesiMetni = "takip öncesi";
    /// <summary>Kanal seçicisinde tüm kanalların toplamı.</summary>
    public const string Toplam = "Toplam";
    /// <summary>Çizilen ay sayısı (son 12 ay; her biri geçen yılın aynı ayıyla).</summary>
    public const int AySayisi = 12;

    public static string BirimAdi(GrafikBirimi b) => b switch
    {
        GrafikBirimi.ReelTl => "Reel TL",
        GrafikBirimi.Usd => "USD",
        GrafikBirimi.Eur => "EUR",
        GrafikBirimi.AltinGram => "Gram altın",
        _ => "TL",
    };

    public static string OlcuAdi(GrafikOlcusu o) => o == GrafikOlcusu.AySonucu ? "Ay sonucu" : "Gelir";

    /// <summary>Türkçe biçim: "12.345,67 ₺", "1.234,56 USD", "12,34 gr".</summary>
    public static string Bicimle(decimal d, GrafikBirimi b) => b switch
    {
        GrafikBirimi.Usd => $"{Bicim.Tl(d)} USD",
        GrafikBirimi.Eur => $"{Bicim.Tl(d)} EUR",
        GrafikBirimi.AltinGram => $"{Bicim.Tl(d)} gr",
        _ => $"{Bicim.Tl(d)} ₺",
    };

    /// <summary>Seçilen aya kadar TÜFE'si olan en son ay (reel TL'nin fiyat tabanı); yoksa null.</summary>
    public static GrafikAyDto? TufeReferansi(GrafikDto g)
        => g.Aylar.LastOrDefault(a => a.TufeEndeksi is > 0m);

    /// <summary>
    /// Nominal TL'yi birime çevirir. Gereken kur/endeks yoksa (ya da sıfır/negatifse) null — asla tahmin edilmez.
    /// Sonuç 2 ondalığa yuvarlanır.
    /// </summary>
    public static decimal? Donustur(decimal nominal, GrafikAyDto ay, GrafikBirimi birim, decimal? referansTufe)
    {
        decimal? r = birim switch
        {
            GrafikBirimi.NominalTl => nominal,
            GrafikBirimi.ReelTl => ay.TufeEndeksi is > 0m && referansTufe is > 0m ? nominal * referansTufe.Value / ay.TufeEndeksi.Value : null,
            GrafikBirimi.Usd => ay.UsdTry is > 0m ? nominal / ay.UsdTry.Value : null,
            GrafikBirimi.Eur => ay.EurTry is > 0m ? nominal / ay.EurTry.Value : null,
            GrafikBirimi.AltinGram => ay.AltinGramTry is > 0m ? nominal / ay.AltinGramTry.Value : null,
            _ => null,
        };
        return r is { } v ? decimal.Round(v, 2, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>Ayın seçili kanal(lar)daki nominal değeri (Toplam = tüm kanallar).</summary>
    public static decimal Nominal(GrafikAyDto ay, string kanal, GrafikOlcusu olcu)
    {
        var kanallar = kanal == Toplam ? ay.Kanallar : ay.Kanallar.Where(k => k.Kanal == kanal);
        return kanallar.Sum(k => olcu == GrafikOlcusu.AySonucu ? k.AySonucu : k.Gelir);
    }

    /// <summary>
    /// Son <see cref="AySayisi"/> ayın çubukları (eskiden yeniye). Her çubuk geçen yılın aynı ayını da taşır.
    /// Takip başlangıcından önceki aylar değer taşımaz.
    /// </summary>
    public static IReadOnlyList<GrafikCubugu> Cubuklar(GrafikDto g, string kanal, GrafikOlcusu olcu, GrafikBirimi birim)
    {
        var referans = TufeReferansi(g)?.TufeEndeksi;
        var aylar = g.Aylar.ToDictionary(a => (a.Yil, a.Ay));
        (decimal? Deger, bool KurYok, bool TakipOncesi) Hesap(GrafikAyDto? a)
        {
            if (a is null || a.TakipOncesi) return (null, false, true);
            var d = Donustur(Nominal(a, kanal, olcu), a, birim, referans);
            return (d, d is null, false);
        }

        var son = g.Aylar.Count == 0 ? new DateOnly(g.Yil, g.Ay, 1) : new DateOnly(g.Aylar[^1].Yil, g.Aylar[^1].Ay, 1);
        var liste = new List<GrafikCubugu>(AySayisi);
        for (int i = AySayisi - 1; i >= 0; i--)
        {
            var t = son.AddMonths(-i);
            var gy = t.AddMonths(-12);
            aylar.TryGetValue((t.Year, t.Month), out var bu);
            aylar.TryGetValue((gy.Year, gy.Month), out var once);
            var (d, ky, to) = Hesap(bu);
            var (gd, gky, gto) = Hesap(once);
            liste.Add(new GrafikCubugu(t.Year, t.Month, KisaEtiket(t), d, ky, to, gd, gky, gto));
        }
        return liste;
    }

    /// <summary>"Ağu 26".</summary>
    public static string KisaEtiket(DateOnly ay) => ay.ToString("MMM yy", Kultur.Turkce);

    /// <summary>Tablo satırları (en yeni önce).</summary>
    public static IReadOnlyList<GrafikSatiri> Satirlar(IReadOnlyList<GrafikCubugu> cubuklar, GrafikBirimi birim)
        => Enumerable.Reverse(cubuklar).Select(c => new GrafikSatiri(
                KasaDokumuGorunum.AyEtiketi(c.Yil, c.Ay),
                c.DegerMetni(birim),
                c.GecenYilMetni(birim),
                c.DegisimYuzdesi is { } y ? (y > 0 ? "+" : "") + y.ToString("0.0", Kultur.Turkce) + " %" : "—",
                c.Deger ?? 0m,
                c.KurYok))
            .ToList();

    /// <summary>
    /// Dikey eksen: sıfırı içeren [en küçük, en büyük] aralığı (değer yoksa [0, 1]). Çizim her değeri bu
    /// aralığa göre oranlar; eksi ay sonucu sıfır çizgisinin altına iner.
    /// </summary>
    public static (decimal EnKucuk, decimal EnBuyuk) Olcek(IEnumerable<GrafikCubugu> cubuklar)
    {
        var degerler = cubuklar.SelectMany(c => new[] { c.Deger, c.GecenYil }).OfType<decimal>().ToList();
        var enBuyuk = Math.Max(0m, degerler.DefaultIfEmpty(0m).Max());
        var enKucuk = Math.Min(0m, degerler.DefaultIfEmpty(0m).Min());
        if (enBuyuk == enKucuk) enBuyuk = enKucuk + 1m;
        return (enKucuk, enBuyuk);
    }

    /// <summary>Değerin eksen üzerindeki konumu: 0 = en alt (en küçük), 1 = en üst (en büyük).</summary>
    public static double Oran(decimal deger, decimal enKucuk, decimal enBuyuk)
    {
        if (enBuyuk <= enKucuk) return 0d;
        var o = (double)((deger - enKucuk) / (enBuyuk - enKucuk));
        return Math.Clamp(o, 0d, 1d);
    }
}
