namespace Kasa.App.Core.Tests;

/// <summary>Bu bilgisayarda gösterilen bildirim kimlikleri (%LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json): her kimlik bir kez
/// "yeni" sayılır, dosya en çok 500 kimlik tutar (eskiler atılır), bozuk dosya boş sayılır, başka süreç dosyayı kilitliyse beklenir;
/// kilit bırakılmazsa IOException.</summary>
public sealed class GosterilenBildirimDeposuTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-bildirim-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Yol => Path.Combine(_klasor, DosyaGosterilenBildirimDeposu.DosyaAdi);

    [Fact]
    public void Yeni_kimlikler_bir_kez_ayrilir_ve_dosyaya_yazilir()
    {
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal([3, 1], depo.YenileriAyir([3, 1, 3]));
        Assert.Equal([2], depo.YenileriAyir([1, 2, 3]));
        Assert.Empty(new DosyaGosterilenBildirimDeposu(_klasor).YenileriAyir([1, 2, 3]));
        Assert.Equal("[3,1,2]", File.ReadAllText(Yol));
    }

    [Fact]
    public void Sinirda_en_eski_kimlikler_atilir()
    {
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal(500, depo.YenileriAyir([.. Enumerable.Range(1, 500)]).Count);
        Assert.Equal([501, 502], depo.YenileriAyir([501, 502]));
        var kayitli = depo.Kayitlilar();
        Assert.Equal(500, kayitli.Count);
        Assert.Equal(3, kayitli[0]);
        Assert.Equal(502, kayitli[^1]);
        // Atılan en eski kimlik yeniden yeni sayılır; liste bugünle sınırlı olduğundan pratikte görülmez.
        Assert.Equal([1], depo.YenileriAyir([1]));
    }

    [Theory]
    [InlineData("bozuk{")]
    [InlineData("")]
    [InlineData(@"{""a"":1}")]
    public void Bozuk_dosya_bos_sayilir_ve_uzerine_yazilir(string icerik)
    {
        Directory.CreateDirectory(_klasor);
        File.WriteAllText(Yol, icerik);
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal([7], depo.YenileriAyir([7]));
        Assert.Equal("[7]", File.ReadAllText(Yol));
    }

    [Fact]
    public async Task Baska_surecin_kilidi_birakilinca_beklenir_birakilmazsa_IOException()
    {
        Directory.CreateDirectory(_klasor);
        var kilit = new FileStream(Yol, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Assert.Throws<IOException>(() => new DosyaGosterilenBildirimDeposu(_klasor, deneme: 3, bekleme: TimeSpan.FromMilliseconds(10)).YenileriAyir([1]));
            var birak = Task.Run(async () =>
            {
                await Task.Delay(200, TestContext.Current.CancellationToken);
                await kilit.DisposeAsync();
            }, TestContext.Current.CancellationToken);
            Assert.Equal([1], new DosyaGosterilenBildirimDeposu(_klasor, deneme: 100, bekleme: TimeSpan.FromMilliseconds(50)).YenileriAyir([1]));
            await birak;
        }
        finally
        {
            // Bir doğrulama erken düşerse kilit açık kalmaz (klasör silinebilir); ikinci kapatma zararsızdır.
            await kilit.DisposeAsync();
        }
    }
}
