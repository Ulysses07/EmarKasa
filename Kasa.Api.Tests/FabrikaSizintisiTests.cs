using System.Runtime.CompilerServices;

namespace Kasa.Api.Tests;

/// <summary>Kapatılan test fabrikası bellekte kalmaz (<see cref="HizSiniriSizintisi"/>): hız sınırı ara katmanının zamanlayıcısı
/// uygulamayı canlı tutsaydı her testin sunucusu koşu sonuna dek birikir, çöp toplayıcı bütün testleri yavaşlatırdı.</summary>
public class FabrikaSizintisiTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> KullanVeKapat(bool senkron)
    {
        var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using (var c = await f.EditorClientAsync())
            // Hız sınırı politikalı bir uç: ara katmanın uç sınırlayıcısı bölüm açar.
            (await c.GetAsync("/api/rapor/panel")).EnsureSuccessStatusCode();
        // WithWebHostBuilder'la aynı veritabanıyla yeniden açılan sunucu (ör. yeniden başlatma testleri).
        var yeniden = f.WithWebHostBuilder(_ => { });
        using (var c = yeniden.CreateClient())
            (await c.GetAsync("/health")).EnsureSuccessStatusCode();
        WeakReference[] izler = [new(f), new(f.Services), new(yeniden), new(yeniden.Services)];
        // Senkron Dispose() (using var) da WebApplicationFactory.Dispose(bool) üzerinden sanal DisposeAsync()'i çağırır;
        // sınırlayıcılar iki yolda da kapanır.
        if (senkron)
            f.Dispose();
        else
            await f.DisposeAsync();
        return izler;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kapatilan_fabrika_ve_uygulamasi_cop_toplayiciya_birakilir(bool senkron)
    {
        var izler = await KullanVeKapat(senkron);
        // Kapanışın ardından biten iş parçacığı havuzu devamları kısa süre başvuru tutabilir; tam toplama birkaç kez denenir.
        for (var i = 0; i < 10 && izler.Any(z => z.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.False(izler[0].IsAlive, "Kapatılan fabrika bellekte kaldı.");
        Assert.False(izler[1].IsAlive, "Kapatılan uygulamanın servis sağlayıcısı bellekte kaldı (hız sınırı ara katmanı sızıntısı).");
        Assert.False(izler[2].IsAlive, "WithWebHostBuilder'la türetilen fabrika bellekte kaldı.");
        Assert.False(izler[3].IsAlive, "Türetilen fabrikanın uygulaması bellekte kaldı.");
    }
}
