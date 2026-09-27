using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class HataYuzeyiTests
{
    [Fact]
    public async Task Panel_ag_hatasinda_hata_yazar_ve_spinner_iner()
    {
        var api = new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.ServiceUnavailable, "kopuk") };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Zaman_asimi_baglanti_hatasindan_ayri_anlatilir_ve_kontrol_ister()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti) });

        await vm.YukleAsync();

        Assert.Contains("zamanında yanıt vermedi", vm.Hata);
        Assert.Contains("tamamlanmış olabilir", vm.Hata);
        Assert.DoesNotContain("ulaşılamadı", vm.Hata);
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", HataMetni(new TaskCanceledException()));
    }

    private sealed class HataOkuyucu : TemelViewModel { public static string Oku(Exception e) => HataMesaji(e); }
    private static string HataMetni(Exception e) => HataOkuyucu.Oku(e);

}
