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
        var vm = new AyarlarViewModel(api) { YeniIzleyiciSifre = "gizli-izleyici-123" };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Equal("gizli-izleyici-123", api.SonIzleyiciSifre);
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

    [Fact]
    public async Task Kayitli_kasa_acilis_devrini_sifira_indirmek_ikinci_basista_onaylanir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true), KanallarListe = new List<KanalDto>() };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();
        vm.KasaAcilisDevri = 0m;                                   // alan boşaltılmış olabilir

        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        Assert.Contains("15.000,00 ₺ yerine 0,00 ₺", vm.AyarUyarisi);
        Assert.Contains("Onaylamak için yeniden kaydedin", vm.AyarUyarisi);

        vm.KasaAcilisDevri = 1m; vm.KasaAcilisDevri = 0m;          // değer değişti: onay sıfırlanır
        Assert.Null(vm.AyarUyarisi);
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        vm.TakipBaslangic = new DateTime(2026, 2, 1);              // diğer alan değişti: onay sıfırlanır
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);

        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0m, api.SonAyar!.KasaAcilisDevri);
        Assert.Null(vm.AyarUyarisi);

        api.SonAyar = null;                                        // kayıtlı değer artık 0: yeniden onay istenmez
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0m, api.SonAyar!.KasaAcilisDevri);
    }

    [Fact]
    public async Task Kayitli_kanal_acilis_devrini_sifira_indirmek_ikinci_basista_onaylanir()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, true),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 5000m), new(2, "PERAKENDE", true, 1, 700m) },
        };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();
        vm.KanalDuzenle(vm.Kanallar[0]); vm.DuzenKanalAcilisDevri = 0m;

        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalGuncelle);
        Assert.Contains("MEZAT açılış devri 5.000,00 ₺ yerine 0,00 ₺", vm.KanalUyarisi);

        vm.KanalDuzenle(vm.Kanallar[1]); vm.DuzenKanalAcilisDevri = 0m;   // başka kanal: onay sıfırlanır
        Assert.Null(vm.KanalUyarisi);
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalGuncelle);
        Assert.Contains("PERAKENDE açılış devri 700,00 ₺", vm.KanalUyarisi);

        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.SonKanalGuncelle!.Value.Id);
        Assert.Equal(0m, api.SonKanalGuncelle!.Value.G.AcilisDevri);
        Assert.Null(vm.KanalUyarisi);

        vm.KanalDuzenle(vm.Kanallar[0]); vm.DuzenKanalAcilisDevri = 4000m;  // sıfır olmayan değişiklik onaysız gider
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal((1, 4000m), (api.SonKanalGuncelle!.Value.Id, api.SonKanalGuncelle.Value.G.AcilisDevri));

        vm.YeniKanalCommand.Execute(null); vm.DuzenKanalAd = "TOPTAN";     // yeni kanalın kayıtlı devri yok
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal("TOPTAN", api.SonKanalOlustur!.Ad);
    }
}
