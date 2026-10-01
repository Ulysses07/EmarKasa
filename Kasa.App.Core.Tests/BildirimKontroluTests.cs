using System.Diagnostics;
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Pencere açmadan çalışma (--bildirim-kontrol, tasarım 2026-09-30 masaüstü bildirimleri §1): kayıtlı belirteçle rol sorulur,
/// editörse yeni bildirimler gösterilir; belirteç yok, süresi dolmuş, rol editör değil ya da ayar kapalıysa sessizce çıkılır; en çok
/// 60 saniye çalışılır.</summary>
public class BildirimKontroluTests
{
    private static (BildirimOrtami O, SahteApi Oturum, BildirimKontrolu Kontrol) Kur(string? rol = "editor")
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun)];
        var oturum = new SahteApi { MeRol = rol };
        return (o, oturum, new BildirimKontrolu(oturum, o.Yoklayici, o.Ayar));
    }

    [Fact]
    public void Kontrol_argumani_taninir()
    {
        Assert.True(BildirimKontrolu.KontrolModu(["Kasa.App.exe", "--bildirim-kontrol"]));
        Assert.False(BildirimKontrolu.KontrolModu(["Kasa.App.exe", "--hatirlatma-kontrol"]));
        Assert.False(BildirimKontrolu.KontrolModu(["Kasa.App.exe"]));
        Assert.Equal(TimeSpan.FromSeconds(60), BildirimKontrolu.EnUzunSure);
    }

    [Fact]
    public async Task Editor_oturumunda_yeni_bildirimler_gosterilir()
    {
        var (o, _, kontrol) = Kur();
        var sonuc = await kontrol.CalistirAsync();
        Assert.Equal(1, sonuc!.YeniSayisi);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("alici")]
    [InlineData(null)]
    public async Task Editor_olmayan_ya_da_rolsuz_oturumda_hicbir_sey_gosterilmez(string? rol)
    {
        var (o, _, kontrol) = Kur(rol);
        Assert.Null(await kontrol.CalistirAsync());
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Oturum_suresi_dolmussa_ya_da_ayar_kapaliysa_sessizce_cikilir()
    {
        var (o, oturum, kontrol) = Kur();
        oturum.MeHatasi = new KasaApiException(HttpStatusCode.Unauthorized);
        Assert.Null(await kontrol.CalistirAsync());
        oturum.MeHatasi = null;
        o.Ayar.Acik = false;
        Assert.Null(await kontrol.CalistirAsync());
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Rol_sorulurken_sunucuya_ulasilamazsa_sessizce_cikilir()
    {
        var (o, oturum, kontrol) = Kur();
        oturum.MeHatasi = new HttpRequestException("bağlantı yok");
        Assert.Null(await kontrol.CalistirAsync());
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Sure_dolunca_beklemeden_cikilir()
    {
        var (o, _, kontrol) = Kur();
        o.Api.Bekleyen = new TaskCompletionSource<IReadOnlyList<BildirimDto>>().Task;
        var sure = Stopwatch.StartNew();
        Assert.Null(await kontrol.CalistirAsync(TimeSpan.FromMilliseconds(200)));
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(10), $"süre {sure.Elapsed}");
        Assert.Empty(o.Gosterici.Gosterilenler);
    }
}
