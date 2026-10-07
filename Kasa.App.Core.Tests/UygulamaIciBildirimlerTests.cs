using Kasa.ApiClient;
using Kasa.App.Services;

namespace Kasa.App.Core.Tests;

public sealed class UygulamaIciBildirimlerTests
{
    [Fact]
    public async Task IOS_okunmamis_sayisi_yenilenir_sistem_bildirimi_gosterilmis_sayilmaz()
    {
        var api = new SahteBildirimApi
        {
            Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun), SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun, okundu: true)],
        };
        var depo = new SahteDepo();
        var ayar = new UygulamaIciBildirimAyari();
        var gosterici = new UygulamaIciBildirimGosterici();
        var saat = new SabitBildirimSaati(new DateTimeOffset(2026, 9, 30, 14, 5, 0, TimeSpan.Zero));
        var yoklayici = new BildirimYoklayici(api, gosterici, depo, ayar, saat);

        await yoklayici.YoklaAsync(true);

        Assert.Equal(1, yoklayici.Okunmamis);
        Assert.Equal(1, api.ListeCagri);
        Assert.Empty(depo.Kayitli);
        Assert.Empty(api.Okunanlar);
        Assert.False(gosterici.DenemeGoster());
    }

    [Fact]
    public async Task Arka_planda_bildirim_istegi_baslamaz_on_plana_donuste_yenilenir()
    {
        var api = new SahteBildirimApi { Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun)] };
        var ayar = new UygulamaIciBildirimAyari();
        var yoklayici = new BildirimYoklayici(api, new UygulamaIciBildirimGosterici(), new SahteDepo(), ayar);

        Assert.True(ayar.OnPlanAyarla(false));
        Assert.Null(await yoklayici.YoklaAsync(true));
        Assert.Equal(0, api.ListeCagri);
        Assert.True(ayar.OnPlanAyarla(true));
        Assert.False(ayar.OnPlanAyarla(true));
        await yoklayici.YoklaAsync(true);
        Assert.Equal(1, api.ListeCagri);
        Assert.Equal(1, yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Arka_planda_saglik_istegi_baslamaz_ve_basari_sayilmaz()
    {
        var sunucu = new SaglikYoklamasi();
        var ayar = new UygulamaIciBildirimAyari();
        var yoklama = new OnPlanBaglantiYoklamasi(sunucu, ayar);
        ayar.OnPlanAyarla(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => yoklama.YoklaAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, sunucu.Cagri);
        ayar.OnPlanAyarla(true);
        using var iptal = new CancellationTokenSource();
        await yoklama.YoklaAsync(iptal.Token);
        Assert.Equal(1, sunucu.Cagri);
        Assert.Equal(iptal.Token, sunucu.SonToken);
    }

    [Fact]
    public async Task IOS_saat_kaydi_Windows_gorevi_kurmaz()
    {
        var api = new SahteBildirimApi();
        var ayar = new UygulamaIciBildirimAyari();
        var gosterici = new UygulamaIciBildirimGosterici();
        var gorev = new BildirimGoreviYok();
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true };
        var yoklayici = new BildirimYoklayici(api, gosterici, new SahteDepo(), ayar);
        var nobetci = new BildirimNobetcisi(yoklayici, api, gorev, ayar, gosterici, auth);

        await nobetci.SaatDegistiAsync(9, 0);
        await nobetci.OturumAcildiAsync();

        Assert.False(gorev.Kurulu);
        Assert.Equal(1, api.ListeCagri);
    }

    private sealed class SaglikYoklamasi : IBaglantiYoklamasi
    {
        public int Cagri { get; private set; }
        public CancellationToken SonToken { get; private set; }

        public Task YoklaAsync(CancellationToken ct = default)
        {
            Cagri++;
            SonToken = ct;
            return Task.CompletedTask;
        }
    }
}
