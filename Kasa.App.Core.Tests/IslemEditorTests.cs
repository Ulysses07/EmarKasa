using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class IslemEditorTests
{
    [Fact]
    public async Task Yeni_islem_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            DuzenTarih = new DateTime(2026, 3, 5),
            DuzenCari = "MEZAT alış",
            DuzenTutar = 2500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.Cari,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal("MEZAT", api.SonIslemOlustur!.Kanal);
        Assert.Equal(2500m, api.SonIslemOlustur!.TutarTl);
        Assert.Equal(new DateOnly(2026, 3, 5), api.SonIslemOlustur!.Tarih);
    }

    [Fact]
    public async Task Kart_harcamasi_krediKartiId_ve_tip_ile_kaydeder()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            DuzenTarih = new DateTime(2026, 7, 10),
            DuzenCari = "Market",
            DuzenTutar = 500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.KrediKarti,
            DuzenKrediKartiId = 7,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal(7, api.SonIslemOlustur!.KrediKartiId);
        Assert.Equal(GiderTipi.KrediKarti, api.SonIslemOlustur!.Tip);
    }

    [Fact]
    public void SecTip_kredi_karti_secince_kart_secicisi_gorunur()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);

        Assert.False(vm.KartSeciciGorunur);
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));

        Assert.Equal(GiderTipi.KrediKarti, vm.DuzenTip);
        Assert.True(vm.KartSeciciGorunur);
    }

    [Fact]
    public void SecKart_secilen_kart_idsini_atar_tip_disina_donunce_temizler()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api) { DuzenTip = GiderTipi.KrediKarti };

        vm.SecKartCommand.Execute(new KartCipi(9, "Bonus"));
        Assert.Equal(9, vm.DuzenKrediKartiId);

        vm.SecTipCommand.Execute(new SecimCipi("Diğer gider"));
        Assert.Null(vm.DuzenKrediKartiId);
    }

    [Fact]
    public async Task Mevcut_islem_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        vm.Duzenle(new IslemDto(11, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));
        vm.DuzenTutar = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemGuncelle);
        Assert.Equal(11, api.SonIslemGuncelle!.Value.Id);
        Assert.Equal(12000m, api.SonIslemGuncelle!.Value.G.TutarTl);
    }

    [Fact]
    public async Task Sil_islem_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);

        await vm.SilCommand.ExecuteAsync(new IslemDto(5, new DateOnly(2026,3,5), "x", 1m, "MEZAT", GiderTipi.Cari, null));

        Assert.Equal(5, api.SonIslemSil);
    }

    [Fact]
    public async Task Eski_yinelenen_gelir_salt_okunur_mesaji_gosterilir_form_korunur()
    {
        const string mesaj = "Bu dönemde birden fazla eski gelir kaydı var; kayıtlar salt okunur olarak korunuyor.";
        var api = new SahteApi { GelenKaydetHatasi = new KasaApiException(System.Net.HttpStatusCode.Conflict, mesaj) };
        var vm = new IslemlerViewModel(api) { GelenTarih = new(2026, 3, 2), GelenKanal = "MEZAT", GelenTutar = 123.45m };

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(mesaj, vm.Hata);
        Assert.Equal("MEZAT", vm.GelenKanal);
        Assert.Equal(123.45m, vm.GelenTutar);
        Assert.Equal(new DateTime(2026, 3, 2), vm.GelenTarih);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Gelen_kaydet_gelen_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            GelenTarih = new DateTime(2026, 3, 2),
            GelenKanal = "PERAKENDE",
            GelenTutar = 5000m,
        };

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonGelen);
        Assert.Equal("PERAKENDE", api.SonGelen!.Kanal);
        Assert.Equal(5000m, api.SonGelen!.TutarTl);
        Assert.Equal(new DateOnly(2026, 3, 2), api.SonGelen!.DonemStart);
    }

    [Fact]
    public async Task Kanal_filtresi_secilince_o_kanalla_listeler()
    {
        var api = new SahteApi
        {
            KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m), new KanalDto(2, "TOPTAN", true, 1, 0m) },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        var mezatCipi = vm.FiltreKanallari.First(c => c.Ad == "MEZAT");
        await vm.SecFiltreKanalCommand.ExecuteAsync(mezatCipi);

        Assert.Equal("MEZAT", api.SonFiltreKanal);
        Assert.True(mezatCipi.Secili);
        Assert.True(vm.FiltreKanallari.First(c => c.Ad == "Tümü").Secili == false);
    }

    [Fact]
    public async Task Tum_kanal_cipi_filtreyi_temizler()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.First(c => c.Ad == "Ortak"));
        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.First(c => c.Ad == "Tümü"));

        Assert.Null(api.SonFiltreKanal);
        Assert.Null(vm.FiltreKanal);
    }

    [Fact]
    public async Task Donem_secilince_o_haftanin_tarih_araligiyla_listeler()
    {
        var donem = new DonemDto(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21), 2026, 6);
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        vm.SeciliDonem = donem;

        Assert.Equal(new DateOnly(2026, 6, 15), vm.FiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 6, 21), vm.FiltreBitis);
    }

    [Fact]
    public async Task Ozet_islem_sayisi_ve_toplami_gosterir()
    {
        var api = new SahteApi
        {
            IslemlerListe = new[]
            {
                new IslemDto(1, new DateOnly(2026, 6, 15), "a", 100m, "MEZAT", GiderTipi.Cari, null),
                new IslemDto(2, new DateOnly(2026, 6, 16), "b", 250m, "MEZAT", GiderTipi.Cari, null),
            },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        Assert.Equal(2, vm.FiltreSayi);
        Assert.Equal(350m, vm.FiltreToplam);
        Assert.Contains("2 işlem", vm.FiltreOzet);
    }
}
