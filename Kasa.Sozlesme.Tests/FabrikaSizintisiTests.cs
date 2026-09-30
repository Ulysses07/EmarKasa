using System.Runtime.CompilerServices;

namespace Kasa.Sozlesme.Tests;

/// <summary>Kapatılan sözleşme fabrikası bellekte kalmaz: hız sınırı ara katmanının zamanlayıcısı (aspnetcore#66434) uygulamayı canlı
/// tutsaydı her sözleşme testinin sunucusu (bellek içi veritabanı ve uç tablosuyla) koşu sonuna dek birikirdi. Fabrika ortak
/// <see cref="Kasa.TestOrtak.SizdirmayanFabrika{TGiris}"/>'dan türer (Kasa.Api.Tests'teki KasaWebFactory ile aynı kaynak).</summary>
public class FabrikaSizintisiTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> KullanVeKapat(bool senkron)
    {
        var f = new SozlesmeFabrikasi();
        // Hız sınırı politikalı bir uç (giriş, 'giris' politikası): ara katmanın uç sınırlayıcısı bölüm açar.
        var istemci = f.Istemci(new YanitKaydedici());
        Assert.Equal("editor", (await istemci.LoginAsync("editor", SozlesmeFabrikasi.EditorSifresi)).Rol);
        WeakReference[] izler = [new(f), new(f.Services)];
        if (senkron)
            f.Dispose();
        else
            await f.DisposeAsync();
        return izler;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kapatilan_sozlesme_fabrikasi_ve_uygulamasi_cop_toplayiciya_birakilir(bool senkron)
    {
        var izler = await KullanVeKapat(senkron);
        // Kapanışın ardından biten iş parçacığı havuzu devamları kısa süre başvuru tutabilir; tam toplama birkaç kez denenir.
        for (var i = 0; i < 10 && izler.Any(z => z.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50);
        }
        Assert.False(izler[0].IsAlive, "Kapatılan sözleşme fabrikası bellekte kaldı.");
        Assert.False(izler[1].IsAlive, "Kapatılan uygulamanın servis sağlayıcısı bellekte kaldı (hız sınırı ara katmanı sızıntısı).");
    }
}
