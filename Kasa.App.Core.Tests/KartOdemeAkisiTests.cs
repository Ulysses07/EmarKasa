namespace Kasa.App.Core.Tests;

/// <summary>Kart ödemesi akışı (tasarım 2026-10-02 §4; KR-01): tek "Ödemeyi kontrol et" önce eksik alanı söyler, sonra önizler;
/// "Onayla ve kaydet" yalnız güncel önizlemede çalışır, girdi değişince kalkar.</summary>
public class KartOdemeAkisiTests
{
    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = new KartTakipViewModel(api, new SahteApi { KanallarListe = [new Kasa.ApiClient.KanalDto(1, "MEZAT", true, 0, 0)] },
            new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        return (vm, api);
    }

    [Fact]
    public async Task Bos_tutarla_kontrol_once_alani_soyler_onizleme_istemez()
    {
        var (vm, api) = await Kur();

        await vm.OdemeOnizleCommand.ExecuteAsync(null);

        Assert.Null(api.OnizlenenOdeme);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.OdemeHatalari[nameof(vm.OdemeTutari)]);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.False(vm.OdemeKaydetCommand.CanExecute(null));
        Assert.Null(vm.OdemeOnizleme);
    }

    [Fact]
    public async Task Onizleme_sonrasi_girdi_degisince_onay_kalkar_yeniden_kontrol_ister()
    {
        var (vm, api) = await Kur();
        vm.OdemeTutari = 40m;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OdemeOnizlemeGuncel);
        Assert.True(vm.OdemeKaydetCommand.CanExecute(null));
        Assert.Contains("MEZAT", vm.OdemeOnizleme);

        vm.OdemeTarihi = vm.OdemeTarihi.AddDays(-1);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.Null(vm.OdemeOnizleme);

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.OdemeNotu = "Dekont 12";
        Assert.False(vm.OdemeKaydetCommand.CanExecute(null));

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.OdemeEkstresi = vm.Ekstreler[0];
        Assert.False(vm.OdemeOnizlemeGuncel);

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(40m, Assert.Single(api.OdemeIstekleri).Tutar);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }
}
