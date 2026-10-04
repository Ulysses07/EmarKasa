using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Krediler "Yeni kredi" formu (tasarım 2026-10-02 §1-2): alan hataları, sunucu iletisi genel hatada, yazılmış formdan
/// krediye geçişte ve "Yeni"de onay.</summary>
public class KrediFormuTests
{
    private static async Task<(KrediTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KrediTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_form_alanlari_ayri_ayri_soyler_istek_gitmez()
    {
        var (vm, api) = await Kur();
        vm.TaksitSayisi = 0;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.KrediKayit);
        Assert.Equal("Banka / kredi adı boş olamaz.", vm.Hatalar[nameof(vm.Ad)]);
        Assert.Equal("Çekilen tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.CekilenTutar)]);
        Assert.Equal("Taksit sayısı 1 ile 600 arasında olmalı.", vm.Hatalar[nameof(vm.TaksitSayisi)]);
        Assert.Equal("Aylık taksit tutarı sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.AylikOdeme)]);
        Assert.Equal("En az bir kanal seçin.", vm.Hatalar[nameof(vm.Kanallar)]);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Yazilmis_yeni_kredi_formu_krediye_gecmeden_ve_yeniden_once_onay_ister()
    {
        var (vm, _) = await Kur();
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        vm.Ad = "Ziraat";
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);
        await vm.SecCommand.ExecuteAsync(vm.Krediler[0]);
        Assert.Null(vm.Secili);
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Ziraat", vm.Ad);

        cevap = true;
        await vm.SecCommand.ExecuteAsync(vm.Krediler[0]);
        Assert.NotNull(vm.Secili);
        Assert.False(vm.KaydedilmemisDegisiklikVar);   // kredi seçiliyken yeni kredi formu kapalıdır

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal(("", false), (vm.Ad, vm.KaydedilmemisDegisiklikVar));
    }

    /// <summary>Liste yenilemesi yazılmış yeni kredi formunu ezmez: kanal seçimi korunur, değişiklik izlenmeye devam eder.</summary>
    [Fact]
    public async Task Yenileme_yazilmis_yeni_kredi_formunu_ve_kanal_secimini_korur()
    {
        var (vm, _) = await Kur();
        vm.Ad = "Ziraat";
        vm.TumKanallariSecCommand.Execute(null);

        await vm.YukleAsync();

        Assert.Equal("Ziraat", vm.Ad);
        Assert.True(vm.Kanallar.Single().Secili);
        Assert.True(vm.KaydedilmemisDegisiklikVar);
    }
}
