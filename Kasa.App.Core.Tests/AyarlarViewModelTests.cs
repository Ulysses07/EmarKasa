using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AyarlarViewModelTests
{
    [Fact]
    public async Task Yukle_ayar_ve_kanallari_doldurur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) },
        };
        var vm = new AyarlarViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(15000m, vm.KasaAcilisDevri);
        Assert.Single(vm.Kanallar);
    }

    [Fact]
    public async Task Yeni_kanal_olustur_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026,1,1), 0m, false) };
        var vm = new AyarlarViewModel(api) { DuzenKanalAd = "TOPTAN", DuzenKanalSira = 3 };

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKanalOlustur);
        Assert.Equal("TOPTAN", api.SonKanalOlustur!.Ad);
        Assert.Equal(3, api.SonKanalOlustur!.Sira);
    }

    [Fact]
    public async Task Kanal_sil_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026,1,1), 0m, false) };
        var vm = new AyarlarViewModel(api);

        await vm.KanalSilCommand.ExecuteAsync(new KanalDto(4, "PERAKENDE", true, 1, 0m));

        Assert.Equal(4, api.SonKanalSil);
    }

    [Fact]
    public async Task Izleyici_sifre_kaydet_cagirir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api) { YeniIzleyiciSifre = "gizli123" };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Equal("gizli123", api.SonIzleyiciSifre);
        Assert.Equal("", vm.YeniIzleyiciSifre);
    }

    [Fact]
    public async Task Ayar_kaydet_cagirir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api)
        {
            TakipBaslangic = new DateTime(2026, 2, 1),
            KasaAcilisDevri = 20000m,
        };

        await vm.AyarKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonAyar);
        Assert.Equal(new DateOnly(2026, 2, 1), api.SonAyar!.TakipBaslangic);
        Assert.Equal(20000m, api.SonAyar!.KasaAcilisDevri);
    }
}

public class OturumKapatTests
{
    [Fact]
    public async Task Oturumlari_kapat_ikinci_basista_calisir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api);

        await vm.OturumlariKapatCommand.ExecuteAsync(null);
        Assert.Equal(0, api.OturumKapatSayisi);
        Assert.True(vm.OturumKapatOnayBekliyor);

        await vm.OturumlariKapatCommand.ExecuteAsync(null);
        Assert.Equal(1, api.OturumKapatSayisi);
        Assert.False(vm.OturumKapatOnayBekliyor);
    }
}
