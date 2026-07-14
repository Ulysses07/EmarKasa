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
}
