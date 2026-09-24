namespace Kasa.App.Core.Tests;

/// <summary>Türkçe tutar ayrıştırma kuralları (ParaGirisConverter bunu çağırır).</summary>
public class ParaGirisTests
{
    [Theory]
    // Boş → 0 (alan boş bırakılabilir)
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("   ", "0")]
    // Düz sayılar
    [InlineData("0", "0")]
    [InlineData("5", "5")]
    [InlineData("1500", "1500")]
    [InlineData("007", "7")]
    [InlineData("+50", "50")]
    // Yalnız nokta, her grup 3 hane → binlik
    [InlineData("1.500", "1500")]
    [InlineData("12.500", "12500")]
    [InlineData("123.456", "123456")]
    [InlineData("1.500.000", "1500000")]
    [InlineData("1.234.567", "1234567")]
    [InlineData("12.345", "12345")]
    // Tek nokta + 1–2 hane → ondalık
    [InlineData("12.5", "12.5")]
    [InlineData("12.50", "12.50")]
    [InlineData("0.05", "0.05")]
    [InlineData(".5", "0.5")]
    [InlineData("1234.5", "1234.5")]
    // Virgül ondalık
    [InlineData("12,5", "12.5")]
    [InlineData("12,50", "12.50")]
    [InlineData("1,5", "1.5")]
    [InlineData(",5", "0.5")]
    [InlineData("0,01", "0.01")]
    [InlineData("1500,5", "1500.5")]
    // Nokta binlik + virgül ondalık
    [InlineData("1.500,50", "1500.5")]
    [InlineData("1.500,5", "1500.5")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("1.500,", "1500")]
    // Yazarken ara durumlar
    [InlineData("12,", "12")]
    [InlineData("12.", "12")]
    // Boşluk, ₺, TL
    [InlineData("1 500", "1500")]
    [InlineData(" 1.500 ", "1500")]
    [InlineData("₺ 2.000,00", "2000")]
    [InlineData("2.000,00 ₺", "2000")]
    [InlineData("2.000,50 TL", "2000.5")]
    [InlineData("TL 750", "750")]
    [InlineData("1 500,25", "1500.25")]
    [InlineData("1 500", "1500")]
    // Sınır
    [InlineData("999.999.999.999,99", "999999999999.99")]
    public void Gecerli_girisler(string? metin, string beklenen)
    {
        var s = ParaGiris.Ayristir(metin);
        Assert.True(s.Gecerli, $"'{metin}' geçerli olmalıydı: {s.Hata}");
        Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), s.Tutar);
        Assert.Null(s.Hata);
    }

    [Theory]
    // Negatif
    [InlineData("-50", ParaGiris.HataNegatif)]
    [InlineData("-1.500", ParaGiris.HataNegatif)]
    [InlineData("−50", ParaGiris.HataNegatif)]
    [InlineData("(50)", ParaGiris.HataNegatif)]
    // Harf / sembol
    [InlineData("abc", ParaGiris.HataGecersiz)]
    [InlineData("12a", ParaGiris.HataGecersiz)]
    [InlineData("1e5", ParaGiris.HataGecersiz)]
    [InlineData("$50", ParaGiris.HataGecersiz)]
    [InlineData("+", ParaGiris.HataGecersiz)]
    [InlineData(",", ParaGiris.HataGecersiz)]
    [InlineData(".", ParaGiris.HataGecersiz)]
    // İngilizce biçim / bozuk gruplama
    [InlineData("1,234,567", ParaGiris.HataGecersiz)]
    [InlineData("1,234.56", ParaGiris.HataGecersiz)]
    [InlineData("1.2.3", ParaGiris.HataGecersiz)]
    [InlineData("1.23.456", ParaGiris.HataGecersiz)]
    [InlineData("1234.567", ParaGiris.HataGecersiz)]
    [InlineData("12.34,5", ParaGiris.HataGecersiz)]
    [InlineData("1.2345,5", ParaGiris.HataGecersiz)]
    [InlineData("1,2,3", ParaGiris.HataGecersiz)]
    [InlineData("1..500", ParaGiris.HataGecersiz)]
    // 2'den fazla ondalık (kuruş altı)
    [InlineData("0,005", ParaGiris.HataKurus)]
    [InlineData("12,345", ParaGiris.HataKurus)]
    [InlineData("1.500,505", ParaGiris.HataKurus)]
    [InlineData("1.5000", ParaGiris.HataKurus)]
    // Çok büyük
    [InlineData("1.000.000.000.000", ParaGiris.HataCokBuyuk)]
    [InlineData("99999999999999999999999999999999", ParaGiris.HataCokBuyuk)]
    public void Gecersiz_girisler_asla_sifira_donmez(string metin, string hata)
    {
        var s = ParaGiris.Ayristir(metin);
        Assert.False(s.Gecerli, $"'{metin}' geçersiz olmalıydı, {s.Tutar} döndü");
        Assert.Equal(hata, s.Hata);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1500, "1500")]
    [InlineData(1500.5, "1500,5")]
    [InlineData(12.25, "12,25")]
    public void Bicimle_giris_kutusu_metni(decimal tutar, string beklenen)
        => Assert.Equal(beklenen, ParaGiris.Bicimle(tutar));

    [Theory]
    [InlineData(1500)]
    [InlineData(1500.5)]
    [InlineData(0.01)]
    [InlineData(1234567.89)]
    public void Bicimlenen_metin_ayni_tutara_geri_ayrisir(decimal tutar)
    {
        var s = ParaGiris.Ayristir(ParaGiris.Bicimle(tutar));
        Assert.True(s.Gecerli);
        Assert.Equal(tutar, s.Tutar);
    }
}
