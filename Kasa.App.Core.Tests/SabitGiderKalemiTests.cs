using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Sabit gider tipinde ad, cariler yerine sabit gider kalemleri listesinden seçilir; form içinden kalem eklenebilir.</summary>
public class SabitGiderKalemiTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static (SahteApi api, IslemlerViewModel vm) Kur()
    {
        var api = new SahteApi
        {
            KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m) },
            DonemlerListe = new[] { new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9) },
            CarilerListe = new[] { new CariDto(1, "Market", true) },
            GiderKalemleriListe = new[] { new GiderKalemiDto(1, "Kira", true), new GiderKalemiDto(2, "İşyeri aidatı", true), new GiderKalemiDto(3, "Eski kalem", false) },
        };
        return (api, new IslemlerViewModel(api, new SabitSaat(Bugun.AddHours(10))));
    }

    private static void FormuDoldur(IslemlerViewModel vm, string ad)
    {
        vm.DuzenTip = GiderTipi.SabitGider;
        vm.DuzenCari = ad; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT"; vm.DuzenTarih = Bugun;
    }

    [Fact]
    public async Task Sabit_gider_tipinde_etiket_ve_oneriler_kalem_listesinden_gelir()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        Assert.Equal("Cari", vm.AdEtiketi);

        vm.DuzenTip = GiderTipi.SabitGider;
        Assert.Equal("Gider kalemi", vm.AdEtiketi);
        vm.DuzenCari = "işyeri";
        Assert.Equal(new[] { "İşyeri aidatı" }, vm.CariOnerileri);
        vm.DuzenCari = "mar";
        Assert.Empty(vm.CariOnerileri);             // cari önerilmez
        vm.DuzenCari = "eski";
        Assert.Empty(vm.CariOnerileri);             // pasif kalem önerilmez

        vm.DuzenTip = GiderTipi.Cari;
        vm.DuzenCari = "mar";
        Assert.Equal(new[] { "Market" }, vm.CariOnerileri);
    }

    [Fact]
    public async Task Kayitli_kalemle_kaydedilir_yazim_kayitli_hale_cevrilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "KİRA");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal("Kira", api.SonIslemOlustur!.Cari);
        Assert.Equal(GiderTipi.SabitGider, api.SonIslemOlustur.Tip);
    }

    [Fact]
    public async Task Kayitsiz_kalemle_kaydetme_durur_ve_ekle_dugmesi_gorunur()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "Elektrik");
        Assert.True(vm.KalemEklenebilir);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(IslemlerViewModel.KalemYokMesaji("Elektrik"), vm.Hata);
        Assert.Null(api.SonIslemOlustur);
    }

    [Fact]
    public async Task Kalem_olarak_ekle_kalemi_olusturur_ve_kayit_yapilabilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "Elektrik");

        await vm.KalemEkleCommand.ExecuteAsync(null);

        Assert.Equal("Elektrik", api.SonKalemOlustur!.Ad);
        Assert.False(vm.KalemEklenebilir);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal("Elektrik", api.SonIslemOlustur!.Cari);
    }

    [Fact]
    public async Task Cari_tipinde_ekle_dugmesi_gorunmez()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        vm.DuzenCari = "Yeni firma";
        Assert.False(vm.KalemEklenebilir);
    }

    [Fact]
    public async Task Ayarlar_kalemleri_listeler_ekler_gunceller_siler()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            GiderKalemleriListe = new[] { new GiderKalemiDto(5, "Kira", true) },
        };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.GiderKalemleri);

        vm.DuzenKalemAd = "SGK";
        await vm.KalemKaydetCommand.ExecuteAsync(null);
        Assert.Equal("SGK", api.SonKalemOlustur!.Ad);
        Assert.Equal("", vm.DuzenKalemAd);

        vm.KalemDuzenle(new GiderKalemiDto(5, "Kira", true));
        vm.DuzenKalemAd = "Dükkan kirası";
        await vm.KalemKaydetCommand.ExecuteAsync(null);
        Assert.Equal((5, new GiderKalemiYaz("Dükkan kirası", true)), api.SonKalemGuncelle);

        await vm.KalemSilCommand.ExecuteAsync(new GiderKalemiDto(5, "Kira", true));
        Assert.Equal(5, api.SonKalemSil);
    }
}
