using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kaydedilenleri bellekte tutan dosya kaydedici sahtesi.</summary>
public sealed class SahteKaydedici : IDosyaKaydedici
{
    public const string Klasor = @"C:\Users\ortak\Documents\Emar Kasa\";
    public List<(string Ad, byte[] Icerik)> Kaydedilenler = new();
    /// <summary>Ayarlanırsa kaydetme bu istisnayı fırlatır (disk dolu, izin yok, dosya Excel'de açık…).</summary>
    public Exception? Hata;
    public Task<string> KaydetAsync(string dosyaAdi, byte[] icerik)
    {
        if (Hata is not null) return Task.FromException<string>(Hata);
        Kaydedilenler.Add((dosyaAdi, icerik));
        return Task.FromResult(Klasor + dosyaAdi);
    }
}

/// <summary>"Excel'e aktar": dosya adı güvenliği, çakışma ve sayfa komutları.</summary>
public class ExcelAktarmaTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    [Theory]
    [InlineData("kasa-islemler-2026-09.csv", "kasa-islemler-2026-09.csv")]
    [InlineData("../../Windows/System32/x.csv", "x.csv")]
    [InlineData(@"..\..\x.csv", "x.csv")]
    [InlineData("a:b*?<>|\".csv", "a-b------.csv")]
    [InlineData("rapor.exe", "rapor.exe.csv")]
    [InlineData("rapor.CSV", "rapor.csv")]
    [InlineData("rapor. .", "rapor.csv")]
    [InlineData("CON.csv", "kasa-CON.csv")]
    [InlineData("nul", "kasa-nul.csv")]
    [InlineData("satır\nsonu.csv", "satır-sonu.csv")]
    [InlineData("", DosyaKayit.VarsayilanAd)]
    [InlineData(null, DosyaKayit.VarsayilanAd)]
    [InlineData(".csv", DosyaKayit.VarsayilanAd)]
    [InlineData("klasor/", DosyaKayit.VarsayilanAd)]
    public void Guvenli_ad(string? girdi, string beklenen)
        => Assert.Equal(beklenen, DosyaKayit.GuvenliAd(girdi));

    [Fact]
    public void Guvenli_ad_uzun_adi_kisaltir()
    {
        var ad = DosyaKayit.GuvenliAd(new string('a', 500) + ".csv");
        Assert.Equal(DosyaKayit.EnUzunAd + 4, ad.Length);
        Assert.EndsWith(".csv", ad);
    }

    [Fact]
    public void Benzersiz_yol_var_olan_dosyayi_ezmez()
    {
        var klasor = Path.Combine("belgeler", DosyaKayit.KlasorAdi);
        var mevcut = new HashSet<string>
        {
            Path.Combine(klasor, "kasa-aylik-2026-09.csv"),
            Path.Combine(klasor, "kasa-aylik-2026-09 (2).csv"),
        };
        Assert.Equal(Path.Combine(klasor, "kasa-aylik-2026-08.csv"), DosyaKayit.BenzersizYol(klasor, "kasa-aylik-2026-08.csv", mevcut.Contains));
        Assert.Equal(Path.Combine(klasor, "kasa-aylik-2026-09 (3).csv"), DosyaKayit.BenzersizYol(klasor, "kasa-aylik-2026-09.csv", mevcut.Contains));
    }

    [Fact]
    public async Task Haftalik_aktar_indirir_kaydeder_yolu_gosterir()
    {
        var api = new SahteApi { CsvDosyasi = new IndirilenDosya("kasa-haftalik-2026-09-24.csv", [1, 2, 3]) };
        var k = new SahteKaydedici();
        var vm = new HaftalikViewModel(api, k);

        await vm.ExceleAktarCommand.ExecuteAsync(null);

        Assert.Equal(1, api.HaftalikCsvCagri);
        var (ad, icerik) = Assert.Single(k.Kaydedilenler);
        Assert.Equal("kasa-haftalik-2026-09-24.csv", ad);
        Assert.Equal([1, 2, 3], icerik);
        Assert.Equal(SahteKaydedici.Klasor + "kasa-haftalik-2026-09-24.csv", vm.AktarilanDosya);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Sunucudan_gelen_tehlikeli_ad_guvenli_ada_cevrilir()
    {
        var api = new SahteApi { CsvDosyasi = new IndirilenDosya(@"..\..\baslangic\kotu.exe", [1]) };
        var k = new SahteKaydedici();
        await new HaftalikViewModel(api, k).ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal("kotu.exe.csv", Assert.Single(k.Kaydedilenler).Ad);
    }

    [Fact]
    public async Task Aylik_aktar_ekrandaki_ayi_ister()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 8, new List<KanalAylikDto>()) };
        var k = new SahteKaydedici();
        var vm = new AylikViewModel(api, new SabitSaat(Bugun), k);
        await vm.OncekiAyCommand.ExecuteAsync(null);

        await vm.ExceleAktarCommand.ExecuteAsync(null);

        Assert.Equal((2026, 8), api.SonAylikCsv);
        Assert.Single(k.Kaydedilenler);
        Assert.NotNull(vm.AktarilanDosya);
    }

    [Fact]
    public async Task Islemler_aktar_secili_filtreyi_kullanir()
    {
        var api = new SahteApi { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m) } };
        var k = new SahteKaydedici();
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun.AddHours(10)), k);
        await vm.YukleAsync();

        // Varsayılan filtre: bu ay, tüm kanallar.
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), (string?)null, (string?)null), api.SonIslemCsv);

        await vm.SecFiltreZamanCommand.ExecuteAsync(vm.FiltreZamanlar.Single(z => z.Ad == "Geçen ay"));
        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.Single(c => c.Ad == "MEZAT"));
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", (string?)null), api.SonIslemCsv);

        await vm.SecFiltreZamanCommand.ExecuteAsync(vm.FiltreZamanlar.Single(z => z.Ad == "Tümü"));
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(((DateOnly?)null, (DateOnly?)null, "MEZAT", (string?)null), api.SonIslemCsv);
        Assert.Equal(3, k.Kaydedilenler.Count);
    }

    [Fact]
    public async Task Kaydedici_yoksa_indirmeden_hata_gosterir()
    {
        var api = new SahteApi();
        var vm = new HaftalikViewModel(api);
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydediciYok, vm.Hata);
        Assert.Equal(0, api.HaftalikCsvCagri);
        Assert.Null(vm.AktarilanDosya);
    }

    [Fact]
    public async Task Sunucu_hatasi_mesajla_gosterilir_dosya_kaydedilmez()
    {
        var api = new SahteApi { CsvHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Ay 1 ile 12 arasında olmalı.") };
        var k = new SahteKaydedici();
        var vm = new AylikViewModel(api, new SabitSaat(Bugun), k);
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal("Ay 1 ile 12 arasında olmalı.", vm.Hata);
        Assert.Empty(k.Kaydedilenler);
        Assert.Null(vm.AktarilanDosya);
    }

    [Fact]
    public async Task Kaydetme_hatasi_anlasilir_mesaj_verir_eski_yol_temizlenir()
    {
        var api = new SahteApi();
        var k = new SahteKaydedici();
        var vm = new HaftalikViewModel(api, k);
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.NotNull(vm.AktarilanDosya);

        k.Hata = new IOException("The process cannot access the file because it is being used by another process.");
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydedilemediMesaji, vm.Hata);
        Assert.Null(vm.AktarilanDosya);

        k.Hata = new UnauthorizedAccessException();
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydedilemediMesaji, vm.Hata);
    }

    [Fact]
    public async Task Baglanti_hatasi_ortak_mesajla()
    {
        var api = new SahteApi { CsvHatasi = new HttpRequestException("yok") };
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun), new SahteKaydedici());
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
    }
}
