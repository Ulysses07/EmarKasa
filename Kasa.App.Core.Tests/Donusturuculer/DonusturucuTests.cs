using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Converters;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kasa.App dönüştürücüleri (maui-8, tests-9): XAML bağlamalarındaki biçim ve renk mantığı, uygulamanın kendi kaynak
/// dosyalarıyla (csproj'da bağlı derleme) sınanır. Görünüm tr-TR'dir: bağlamanın verdiği kültür ve iş parçacığı kültürü
/// ne olursa olsun ay adı, baş harf ve para metni Türkçe kurallarla üretilir.
/// </summary>
public class DonusturucuTests
{
    private static readonly CultureInfo[] BaglamaKulturleri = [CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("tr-TR")];

    /// <summary>İş parçacığı kültürü İngilizceyken çalıştırır: dönüştürücü tr-TR'yi kendisi seçmeli.</summary>
    private static T IngilizceKulturde<T>(Func<T> islem)
    {
        var onceki = CultureInfo.CurrentCulture; var oncekiUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            return islem();
        }
        finally { CultureInfo.CurrentCulture = onceki; CultureInfo.CurrentUICulture = oncekiUi; }
    }

    private static object? Cevir(IValueConverter d, object? deger, object? parametre = null, CultureInfo? kultur = null)
        => d.Convert(deger, typeof(object), parametre, kultur ?? CultureInfo.InvariantCulture);

    [Fact]
    public void Kapsam_her_donusturucu_bu_sinifta_sinanir()
    {
        // Yeni dönüştürücü eklendiğinde (csproj onu kendiliğinden derler) buraya ve aşağıya testi eklenmeli.
        var bilinen = new[] { typeof(AyAdiConverter), typeof(BasHarfConverter), typeof(DoluIseConverter), typeof(DonemBicimConverter),
            typeof(ParaBicimConverter), typeof(ParaRenkConverter), typeof(TersIseConverter) };
        var derlenen = typeof(AyAdiConverter).Assembly.GetTypes()
            .Where(t => t.Namespace == "Kasa.App.Converters" && typeof(IValueConverter).IsAssignableFrom(t)).ToArray();
        Assert.Equal(bilinen.Select(t => t.Name).Order(), derlenen.Select(t => t.Name).Order());
    }

    [Theory]
    [InlineData(1, "Ocak")] [InlineData(2, "Şubat")] [InlineData(3, "Mart")] [InlineData(4, "Nisan")]
    [InlineData(5, "Mayıs")] [InlineData(6, "Haziran")] [InlineData(7, "Temmuz")] [InlineData(8, "Ağustos")]
    [InlineData(9, "Eylül")] [InlineData(10, "Ekim")] [InlineData(11, "Kasım")] [InlineData(12, "Aralık")]
    public void AyAdi_her_ay_turkce_ve_bas_harfi_buyuk(int ay, string beklenen)
    {
        foreach (var kultur in BaglamaKulturleri)
            Assert.Equal(beklenen, IngilizceKulturde(() => Cevir(new AyAdiConverter(), ay, kultur: kultur)));
    }

    [Theory]
    [InlineData(0)] [InlineData(13)] [InlineData(-1)] [InlineData(int.MaxValue)] [InlineData(null)]
    public void AyAdi_aralik_disi_ve_bos_deger_bos_metin(int? ay)
        => Assert.Equal("", Cevir(new AyAdiConverter(), ay));

    [Fact]
    public void AyAdi_sayisal_metin_ve_uzun_sayi_kabul_edilir_sayi_olmayan_metin_hata_verir()
    {
        Assert.Equal("Temmuz", Cevir(new AyAdiConverter(), "7"));
        Assert.Equal("Aralık", Cevir(new AyAdiConverter(), 12L));
        // Bağlama yalnız int ay alanlarına yapılır; sayı olmayan metin Convert.ToInt32'den hata olarak çıkar (bugünkü davranış).
        Assert.Throws<FormatException>(() => Cevir(new AyAdiConverter(), "Temmuz"));
    }

    [Theory]
    [InlineData("ahmet", "A")]
    [InlineData("ismail", "İ")]   // tr-TR: noktalı büyük İ (en-US'te "I" olurdu)
    [InlineData("ılgaz", "I")]    // tr-TR: noktasız ı → I
    [InlineData("çağrı", "Ç")] [InlineData("şule", "Ş")] [InlineData("öykü", "Ö")] [InlineData("ümit", "Ü")] [InlineData("ğ", "Ğ")]
    [InlineData("Zeynep", "Z")] [InlineData("7 Numara", "7")]
    public void BasHarf_turkce_buyuk_harf_kuralini_kullanir(string ad, string beklenen)
    {
        foreach (var kultur in BaglamaKulturleri)
            Assert.Equal(beklenen, IngilizceKulturde(() => Cevir(new BasHarfConverter(), ad, kultur: kultur)));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(42)]
    public void BasHarf_bos_ya_da_metin_olmayan_deger_soru_isareti(object? deger)
        => Assert.Equal("?", Cevir(new BasHarfConverter(), deger));

    [Fact]
    public void BasHarf_bastaki_bosluk_kirpilmaz()
        => Assert.Equal(" ", Cevir(new BasHarfConverter(), " ahmet")); // Bugünkü davranış: rozet boş görünür.

    [Theory]
    [InlineData(null, false)] [InlineData("", false)] [InlineData("   ", false)] [InlineData("\t\n", false)]
    [InlineData("Hata", true)] [InlineData(" x ", true)] [InlineData(5, false)]
    public void DoluIse_yalniz_bosluk_disi_metinde_true(object? deger, bool beklenen)
        => Assert.Equal(beklenen, Cevir(new DoluIseConverter(), deger));

    [Fact]
    public void DonemBicim_yil_parametresiyle_yilli_diger_parametrelerde_yilsiz()
    {
        var donem = new DonemDto(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4), 2025, 12);
        foreach (var kultur in BaglamaKulturleri)
        {
            Assert.Equal("29 Ara – 04 Oca", IngilizceKulturde(() => Cevir(new DonemBicimConverter(), donem, kultur: kultur)));
            Assert.Equal("29 Ara 2025 – 04 Oca 2026", IngilizceKulturde(() => Cevir(new DonemBicimConverter(), donem, "yil", kultur)));
            Assert.Equal("29 Ara – 04 Oca", Cevir(new DonemBicimConverter(), donem, "YIL", kultur)); // parametre büyük/küçük harfe duyarlı
        }
        var ayIci = new DonemDto(new DateOnly(2026, 2, 23), new DateOnly(2026, 2, 28), 2026, 2);
        Assert.Equal("23 Şub – 28 Şub", Cevir(new DonemBicimConverter(), ayIci));
    }

    [Theory]
    [InlineData(null, "")] [InlineData("hazır metin", "hazır metin")] [InlineData(42, "42")]
    public void DonemBicim_donem_olmayan_deger_metne_cevrilir(object? deger, string beklenen)
        => Assert.Equal(beklenen, Cevir(new DonemBicimConverter(), deger));

    [Theory]
    [InlineData("1308800", "1.308.800,00")] [InlineData("-48200", "-48.200,00")] [InlineData("0", "0,00")]
    [InlineData("0.005", "0,01")] [InlineData("0.004", "0,00")] [InlineData("-0.01", "-0,01")] [InlineData("1234.5", "1.234,50")]
    [InlineData("999999999999.99", "999.999.999.999,99")]
    public void ParaBicim_decimal_tr_TR_binlik_nokta_kurus_virgul(string tutar, string beklenen)
    {
        var deger = decimal.Parse(tutar, CultureInfo.InvariantCulture);
        foreach (var kultur in BaglamaKulturleri)
            Assert.Equal(beklenen, IngilizceKulturde(() => Cevir(new ParaBicimConverter(), deger, kultur: kultur)));
    }

    [Theory]
    [InlineData(null, "")] [InlineData(1500, "1500")] [InlineData("12,50", "12,50")]
    public void ParaBicim_decimal_olmayan_deger_bicimlenmeden_metne_cevrilir(object? deger, string beklenen)
        => Assert.Equal(beklenen, Cevir(new ParaBicimConverter(), deger)); // Para alanları decimal'dır; int gelirse biçimlenmez.

    [Theory]
    [InlineData("-0.01", true)] [InlineData("-48200", true)] [InlineData("0", false)] [InlineData("0.01", false)] [InlineData("1308800", false)]
    public void ParaRenk_negatif_kirmizi_sifir_ve_pozitif_yesil(string tutar, bool kirmizi)
    {
        Assert.Equal(kirmizi ? "#C13A2E" : "#1B7A4E", Renk(decimal.Parse(tutar, CultureInfo.InvariantCulture)));
    }

    private static string Renk(object? deger) => Assert.IsType<Color>(Cevir(new ParaRenkConverter(), deger)).ToHex();

    [Fact]
    public void ParaRenk_bos_ve_decimal_olmayan_sayilar()
    {
        Assert.Equal("#1B7A4E", Renk(null));
        Assert.Equal("#C13A2E", Renk(-5));
        Assert.Equal("#C13A2E", Renk(-0.5d));
        Assert.Equal("#1B7A4E", Renk(3L));
        Assert.Throws<FormatException>(() => Cevir(new ParaRenkConverter(), "tutar"));
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(null, true)] [InlineData("true", true)] [InlineData(1, true)]
    public void TersIse_bool_tersler_bool_olmayan_false_sayilir(object? deger, bool beklenen)
    {
        Assert.Equal(beklenen, Cevir(new TersIseConverter(), deger));
        Assert.Equal(beklenen, new TersIseConverter().ConvertBack(deger, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Tek_yonlu_donusturuculer_geri_donusumu_reddeder()
    {
        IValueConverter[] tekYonlu = [new AyAdiConverter(), new BasHarfConverter(), new DoluIseConverter(), new DonemBicimConverter(),
            new ParaBicimConverter(), new ParaRenkConverter()];
        foreach (var d in tekYonlu)
            Assert.Throws<NotSupportedException>(() => d.ConvertBack("x", typeof(object), null, CultureInfo.InvariantCulture));
    }
}
