namespace Kasa.App.Core.Tests;

/// <summary>"Bu bilgisayarda Windows bildirimleri" ayarı (%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json): dosya yoksa ya da bozuksa
/// açıktır (editör için varsayılan), yazılan değeri başka nesne (başka süreç) okur, yazılamazsa değer bellekte kalır; başka süreç
/// dosyayı kısa süre tutuyorsa okuma ve taşıma yeniden denenir, okunamayan dosya "açık" sayılmaz.</summary>
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
    public async Task Baska_surec_dosyayi_kisa_sure_tutarsa_okuma_beklenir()
    {
        new DosyaBildirimAyari(_klasor).Acik = false;
        var kilit = new FileStream(Yol, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var birak = KilidiBirak(kilit);
            Assert.False(new DosyaBildirimAyari(_klasor, deneme: 100, bekleme: TimeSpan.FromMilliseconds(10)).Acik);
            await birak;
        }
        finally
        {
            await kilit.DisposeAsync();
        }
    }

    [Fact]
    public async Task Okunamayan_dosya_acik_sayilmaz_son_bilinen_deger_gecerlidir()
    {
        new DosyaBildirimAyari(_klasor).Acik = false;
        var ayar = new DosyaBildirimAyari(_klasor, deneme: 3, bekleme: TimeSpan.FromMilliseconds(10));
        Assert.False(ayar.Acik);
        var kilit = new FileStream(Yol, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Assert.False(ayar.Acik);
            // Bu süreçte hiç okunamamış mevcut dosya: kullanıcı ayarı yazmıştır, bilinmeyen değer "açık" varsayılmaz.
            Assert.False(new DosyaBildirimAyari(_klasor, deneme: 3, bekleme: TimeSpan.FromMilliseconds(10)).Acik);
        }
        finally
        {
            await kilit.DisposeAsync();
        }
    }

    [Fact]
    public async Task Baska_surec_dosyayi_kisa_sure_tutarsa_tasima_beklenir()
    {
        new DosyaBildirimAyari(_klasor).Acik = false;
        var kilit = new FileStream(Yol, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var birak = KilidiBirak(kilit);
            new DosyaBildirimAyari(_klasor, deneme: 100, bekleme: TimeSpan.FromMilliseconds(10)).Acik = true;
            await birak;
        }
        finally
        {
            await kilit.DisposeAsync();
        }
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
        Assert.Equal(@"{""Acik"":true}", File.ReadAllText(Yol));
    }

    private static Task KilidiBirak(FileStream kilit) => Task.Run(async () =>
    {
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await kilit.DisposeAsync();
    }, TestContext.Current.CancellationToken);

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
