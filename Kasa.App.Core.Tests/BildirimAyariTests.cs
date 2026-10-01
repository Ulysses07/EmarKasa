namespace Kasa.App.Core.Tests;

/// <summary>"Bu bilgisayarda Windows bildirimleri" ayarı (%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json): dosya yoksa ya da bozuksa
/// açıktır (editör için varsayılan), yazılan değeri başka nesne (başka süreç) okur, yazılamazsa değer bellekte kalır.</summary>
public sealed class BildirimAyariTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-ayar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Yol => Path.Combine(_klasor, DosyaBildirimAyari.DosyaAdi);

    [Fact]
    public void Dosya_yoksa_acik_sayilir()
    {
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
        Assert.False(File.Exists(Yol));
    }

    [Fact]
    public void Yazilan_deger_dosyadan_baska_nesneyle_okunur()
    {
        var ayar = new DosyaBildirimAyari(_klasor);
        ayar.Acik = false;
        Assert.False(new DosyaBildirimAyari(_klasor).Acik);
        Assert.Equal(@"{""Acik"":false}", File.ReadAllText(Yol));
        ayar.Acik = true;
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
        Assert.False(File.Exists(Yol + ".yeni"));
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("")]
    [InlineData("null")]
    public void Bozuk_dosyada_acik_sayilir(string icerik)
    {
        Directory.CreateDirectory(_klasor);
        File.WriteAllText(Yol, icerik);
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
    }

    [Fact]
    public void Yazilamayan_klasorde_deger_bellekte_kalir_hata_disari_cikmaz()
    {
        File.WriteAllText(_klasor + ".dosya", "");
        try
        {
            var ayar = new DosyaBildirimAyari(Path.Combine(_klasor + ".dosya", "alt"));
            ayar.Acik = false;
            Assert.False(ayar.Acik);
        }
        finally
        {
            File.Delete(_klasor + ".dosya");
        }
    }
}
