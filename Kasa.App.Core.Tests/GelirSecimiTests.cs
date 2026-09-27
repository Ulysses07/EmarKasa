using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Web ui-core.js incomeSelection ve currentPeriod ile aynı seçim kuralları.</summary>
public class GelirSecimiTests
{
    private static readonly DateOnly D = new(2026, 9, 21);
    private static readonly KanalDto Mezat = new(1, "MEZAT", true, 0, 0m);

    [Fact]
    public void KanalId_dolu_satir_id_ile_eslenir_adi_farkli_olsa_bile()
    {
        var s = GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "Eski ad", 100m, KanalId: 1), new GelenDto(2, D, "MEZAT", 50m, KanalId: 2) }, Mezat);
        Assert.Equal(new GelirSecimSonucu(100m, 1, false), s);
    }

    [Theory]
    [InlineData("mezat", true)]      // SQLite NOCASE: ASCII A-Z küçültülür
    [InlineData("Mezat", true)]
    [InlineData("MEZAT", true)]
    [InlineData("MEZAT ", false)]
    public void KanalId_bos_satir_yalniz_ascii_kucultmeyle_adla_eslenir(string ad, bool eslesir)
        => Assert.Equal(eslesir ? 1 : 0, GelirSecimi.Hesapla(new[] { new GelenDto(1, D, ad, 10m) }, Mezat).Sayi);

    [Fact]
    public void Turkce_harfler_ayri_kalir()
    {
        var istanbul = new KanalDto(3, "İSTANBUL", true, 0, 0m);
        Assert.Equal(0, GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "istanbul", 10m) }, istanbul).Sayi);
        Assert.Equal(1, GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "İstanbul", 10m) }, istanbul).Sayi);
    }

    [Fact]
    public void Birden_fazla_satir_ya_da_eski_grup_salt_okunurdur()
    {
        Assert.Equal(new GelirSecimSonucu(30m, 2, true), GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "MEZAT", 10m, KanalId: 1), new GelenDto(2, D, "mezat", 20m) }, Mezat));
        Assert.True(GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "MEZAT", 10m, KanalId: 1, EskiYinelenenGrup: true) }, Mezat).SaltOkunur);
    }

    [Fact]
    public void Kanal_yoksa_hicbir_satir_secilmez()
        => Assert.Equal(new GelirSecimSonucu(0m, 0, false), GelirSecimi.Hesapla(new[] { new GelenDto(1, D, "MEZAT", 10m, KanalId: 1), new GelenDto(2, D, "", 5m) }, null));

    private static readonly DonemDto[] Donemler =
    {
        new(new(2026, 8, 31), new(2026, 8, 31), 2026, 8),
        new(new(2026, 9, 1), new(2026, 9, 6), 2026, 9),
        new(new(2026, 9, 7), new(2026, 9, 13), 2026, 9),
    };

    [Theory]
    [InlineData("2026-09-03", "2026-09-01")]   // bugünü içeren
    [InlineData("2026-09-13", "2026-09-07")]
    [InlineData("2026-09-20", "2026-09-07")]   // yoksa başlangıcı bugünden önceki en yeni
    [InlineData("2026-08-01", "2026-08-31")]   // o da yoksa ilk dönem
    public void Varsayilan_donem_web_currentPeriod_ile_ayni(string bugun, string beklenen)
        => Assert.Equal(DateOnly.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), GelirSecimi.VarsayilanDonem(Donemler.Reverse(), DateOnly.Parse(bugun, System.Globalization.CultureInfo.InvariantCulture))!.Start);

    [Fact]
    public void Donem_yoksa_varsayilan_yok() => Assert.Null(GelirSecimi.VarsayilanDonem(Array.Empty<DonemDto>(), D));
}
