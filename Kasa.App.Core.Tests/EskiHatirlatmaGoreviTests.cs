using System.Diagnostics;

namespace Kasa.App.Core.Tests;

/// <summary>07-15 hatırlatıcısının eski Windows zamanlanmış görevinin bir kez silinmesi (gap-tarihsel-spec-ve-emekli-web-7): komut
/// pencere açmaz; görev silinince ya da zaten yoksa işaret yazılır ve komut bir daha çalışmaz; başlatılamayan, süresi dolan ya da
/// hâlâ duran görev sonraki açılışta yeniden denenir; hiçbir hata dışarı çıkmaz. Gerçek schtasks çalıştırması Windows'ta elle
/// doğrulanır.</summary>
public sealed class EskiHatirlatmaGoreviTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-gorev-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_klasor, true); } catch (DirectoryNotFoundException) { } }

    private sealed class Calistirici(params int?[] sonuclar)
    {
        private int _sira;
        public List<ProcessStartInfo> Komutlar { get; } = [];
        public Task<int?> Calistir(ProcessStartInfo komut) { Komutlar.Add(komut); return Task.FromResult(sonuclar[_sira++]); }
        public List<string> Ozet => Komutlar.Select(k => k.FileName + " " + string.Join(" ", k.ArgumentList)).ToList();
    }

    [Fact]
    public void Silme_komutu_pencere_acmadan_schtasks_ile_gorevi_zorla_siler()
    {
        var k = EskiHatirlatmaGorevi.SilmeKomutu();
        Assert.Equal("schtasks", k.FileName);
        Assert.Equal(["/Delete", "/TN", "EmarKasaHatirlatici", "/F"], k.ArgumentList);
        Assert.Equal((false, true, true, true), (k.UseShellExecute, k.CreateNoWindow, k.RedirectStandardOutput, k.RedirectStandardError));
        Assert.Equal(["/Query", "/TN", "EmarKasaHatirlatici"], EskiHatirlatmaGorevi.SorguKomutu().ArgumentList);
        Assert.True(EskiHatirlatmaGorevi.KontrolModu(["Kasa.App.exe", "--hatirlatma-kontrol"]));
        Assert.False(EskiHatirlatmaGorevi.KontrolModu(["Kasa.App.exe"]));
    }

    [Fact]
    public async Task Gorev_silinince_isaret_yazilir_ve_komut_bir_daha_calismaz()
    {
        var gorev = new EskiHatirlatmaGorevi(_klasor); var c = new Calistirici(0);
        Assert.True(await gorev.TemizleAsync(c.Calistir));
        Assert.True(gorev.Yapildi);
        Assert.False(await new EskiHatirlatmaGorevi(_klasor).TemizleAsync(c.Calistir));
        Assert.Equal(["schtasks /Delete /TN EmarKasaHatirlatici /F"], c.Ozet);
    }

    [Fact]
    public async Task Gorev_zaten_yoksa_sorguyla_anlasilir_ve_isaret_yazilir()
    {
        var gorev = new EskiHatirlatmaGorevi(_klasor); var c = new Calistirici(1, 1);
        Assert.True(await gorev.TemizleAsync(c.Calistir));
        Assert.True(gorev.Yapildi);
        Assert.Equal(["schtasks /Delete /TN EmarKasaHatirlatici /F", "schtasks /Query /TN EmarKasaHatirlatici"], c.Ozet);
    }

    [Theory]
    [InlineData(new int[] { -1 })]      // schtasks başlatılamadı ya da süresi doldu (null)
    [InlineData(new int[] { 1, 0 })]    // silinemedi, görev hâlâ duruyor
    [InlineData(new int[] { 1, -1 })]   // silinemedi, görev sorgulanamadı
    public async Task Silinemeyen_ya_da_calistirilamayan_gorev_isaretlenmez_sonraki_acilista_yeniden_denenir(int[] sonuclar)
    {
        var gorev = new EskiHatirlatmaGorevi(_klasor);
        var c = new Calistirici([.. sonuclar.Select(s => s < 0 ? (int?)null : s)]);
        Assert.False(await gorev.TemizleAsync(c.Calistir));
        Assert.False(gorev.Yapildi);
        Assert.Equal(sonuclar.Length, c.Komutlar.Count);
        Assert.True(await gorev.TemizleAsync(new Calistirici(0).Calistir));
    }

    [Fact]
    public async Task Calistirici_ya_da_isaret_hatasi_disari_cikmaz()
    {
        Assert.False(await new EskiHatirlatmaGorevi(_klasor).TemizleAsync(_ => throw new InvalidOperationException("çalıştırılamadı")));
        // İşaret klasörü bir dosya: işaret yazılamaz, hata yutulur.
        File.WriteAllText(_klasor + ".dosya", "");
        try { Assert.False(await new EskiHatirlatmaGorevi(Path.Combine(_klasor + ".dosya", "alt")).TemizleAsync(new Calistirici(0).Calistir)); }
        finally { File.Delete(_klasor + ".dosya"); }
    }

    [Fact]
    public async Task Gercek_calistirici_baslatilamayan_komutta_null_doner()
    {
        var komut = new ProcessStartInfo("kasa-olmayan-komut-" + Guid.NewGuid().ToString("N")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        Assert.Null(await EskiHatirlatmaGorevi.CalistirAsync(komut, TimeSpan.FromSeconds(5)));
        var dotnet = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "--version" } };
        Assert.Equal(0, await EskiHatirlatmaGorevi.CalistirAsync(dotnet, TimeSpan.FromSeconds(60)));
    }
}
