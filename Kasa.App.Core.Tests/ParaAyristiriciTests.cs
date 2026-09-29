using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

/// <summary>MAUI para girişi kuralı web ui-core.js cents() ile aynı olmalı: gruplama yok, en çok iki ondalık.</summary>
public class ParaAyristiriciTests
{
    [Theory]
    [InlineData("25.000")]      // Türkçe binlik nokta: 25 TL değil, hata
    [InlineData("1.500")]
    [InlineData("12.000")]
    [InlineData("1.500.000")]
    [InlineData("1.250.000")]
    [InlineData("1,234.56")]    // en-US gruplama
    [InlineData("1.234,56")]    // tr-TR gruplama
    [InlineData("12.345,67")]
    [InlineData("12,345")]      // 3 ondalık
    [InlineData("1,500")]
    [InlineData("1,500.00")]
    [InlineData("10,001")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("1,")]
    [InlineData("1.")]
    [InlineData(",5")]
    [InlineData(".5")]
    [InlineData("-")]
    [InlineData("--5")]
    [InlineData("5-")]
    [InlineData("+5")]
    [InlineData("1..5")]
    [InlineData("1,,5")]
    [InlineData("1.5.0")]
    [InlineData("1e3")]
    [InlineData("٣")]      // Arapça-Hint rakamı: web \d yalnız ASCII kabul eder
    public void Gruplanmis_ya_da_bicimsiz_tutar_reddedilir(string metin)
    {
        Assert.False(ParaAyristirici.Coz(metin, out var tutar, out var hata));
        Assert.Equal(0m, tutar);
        Assert.Equal(ParaAyristirici.BicimHatasi, hata);
        Assert.Contains("binlik", hata);
    }

    [Theory]
    [InlineData("1500", "1500")]
    [InlineData("25000", "25000")]
    [InlineData("1500,5", "1500.5")]
    [InlineData("1500.50", "1500.50")]
    [InlineData("1500,50", "1500.50")]
    [InlineData("0", "0")]
    [InlineData("0,01", "0.01")]
    [InlineData("007", "7")]
    [InlineData("-12,30", "-12.30")]
    [InlineData("-250,75", "-250.75")]
    [InlineData(" 1 500 ₺", "1500")]
    [InlineData("1500 TL", "1500")]
    [InlineData("1500 ₺", "1500")]
    [InlineData(" 1500,5", "1500.5")]
    [InlineData("999999999999,99", "999999999999.99")]
    [InlineData("-999999999999,99", "-999999999999.99")]
    public void Gruplanmamis_tutar_iki_ondalikla_okunur(string metin, string beklenen)
    {
        Assert.True(ParaAyristirici.Coz(metin, out var tutar, out var hata));
        Assert.Null(hata);
        Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), tutar);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("₺")]
    public void Bos_giris_sifirdir(string? metin)
    {
        Assert.True(ParaAyristirici.Coz(metin, out var tutar, out var hata));
        Assert.Equal(0m, tutar);
        Assert.Null(hata);
    }

    [Theory]
    [InlineData("1000000000000")]
    [InlineData("-1000000000000")]
    [InlineData("99999999999999999999999999999999999")]
    public void Sunucu_sinirini_asan_tutar_reddedilir(string metin)
    {
        Assert.False(ParaAyristirici.Coz(metin, out _, out var hata));
        Assert.Equal(ParaAyristirici.SinirHatasi, hata);
    }

    /// <summary>Web cents(): trim, ilk virgül → nokta, /^\d+(?:\.\d{1,2})?$/ ve MAX_CENTS sınırı.</summary>
    private static bool WebKabulEder(string metin)
    {
        var text = metin.Trim();
        var virgul = text.IndexOf(',');
        if (virgul >= 0) text = text[..virgul] + "." + text[(virgul + 1)..];
        if (!Regex.IsMatch(text, "^[0-9]+(?:\\.[0-9]{1,2})?$")) return false;
        var parcalar = text.Split('.');
        var kurus = System.Numerics.BigInteger.Parse(parcalar[0]) * 100 + System.Numerics.BigInteger.Parse((parcalar.Length > 1 ? parcalar[1] : "").PadRight(2, '0'));
        return kurus <= 99999999999999;
    }

    [Theory]
    [InlineData("25.000")] [InlineData("1.500")] [InlineData("1.500.000")] [InlineData("1,234.56")] [InlineData("1.234,56")]
    [InlineData("12,345")] [InlineData("abc")] [InlineData("1,")] [InlineData("1.")] [InlineData(",5")] [InlineData("1..5")]
    [InlineData("1,5,0")] [InlineData("1500")] [InlineData("1500,5")] [InlineData("1500.50")] [InlineData("0")] [InlineData("007")]
    [InlineData("0,01")] [InlineData("999999999999,99")] [InlineData("1000000000000")] [InlineData("٣")]
    public void Kabul_ret_kumesi_web_cents_ile_ayni(string metin)
        => Assert.Equal(WebKabulEder(metin), ParaAyristirici.Coz(metin, out _, out _));

    [Theory]
    [InlineData("0", "")]
    [InlineData("1500", "1500")]
    [InlineData("1500.5", "1500,5")]
    [InlineData("1500.50", "1500,5")]
    [InlineData("1234567.89", "1234567,89")]   // gruplama yok: metin yeniden ayrıştırılabilir
    [InlineData("-12.3", "-12,3")]
    public void Bicimle_gruplamasiz_tr_metin_uretir(string tutar, string beklenen)
        => Assert.Equal(beklenen, ParaAyristirici.Bicimle(decimal.Parse(tutar, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1500.5")] [InlineData("0.01")] [InlineData("25000")] [InlineData("-250.75")] [InlineData("999999999999.99")]
    public void Bicimle_ve_Coz_gidis_donus_ayni_tutari_verir(string tutar)
    {
        var d = decimal.Parse(tutar, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ParaAyristirici.Coz(ParaAyristirici.Bicimle(d), out var geri, out _));
        Assert.Equal(d, geri);
    }

    [Fact]
    public void Bicimle_ikiden_fazla_ondalikli_tutari_yuvarlamaz()
    {
        // Yuvarlanmış metin sessizce başka tutara dönmesin; geri ayrıştırma hataya düşer.
        Assert.Equal("1,234", ParaAyristirici.Bicimle(1.234m));
        Assert.False(ParaAyristirici.Coz("1,234", out _, out _));
    }

    [Fact]
    public void Gecersiz_sentinel_sunucu_araligi_disinda_ve_gecersiz_sayilir()
    {
        Assert.True(ParaAyristirici.Gecersiz < -ParaAyristirici.EnBuyuk);
        Assert.False(ParaAyristirici.GecerliMi(ParaAyristirici.Gecersiz));
        Assert.True(ParaAyristirici.GecerliMi(0m));
        Assert.True(ParaAyristirici.GecerliMi(-ParaAyristirici.EnBuyuk));
        Assert.True(ParaAyristirici.HepsiGecerli(1m, 0m, -5m));
        Assert.False(ParaAyristirici.HepsiGecerli(1m, ParaAyristirici.Gecersiz));
        Assert.Equal("Tutar geçersiz", ParaAyristirici.Goster(ParaAyristirici.Gecersiz));
        Assert.Equal("1.500,00", ParaAyristirici.Goster(1500m));
    }
}
