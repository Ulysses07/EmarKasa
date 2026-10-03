using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kartlar: yeni kart ve "Kartı düzenle" formu (tasarım 2026-10-02 §1-2; KR-04): başlık, alan hataları ve kaydedilmemiş
/// değişiklikte başka karta geçiş / yeni kart onayı.</summary>
public class KartFormuTests
{
    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var ikinci = FinansTakipTests.Sahte.OrnekKart() with { Id = 2, Ad = "World" };
        api.KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([api.Kart, ikinci]);
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KartTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Yeni_kart_formu_bos_kayitta_alanlari_ayri_ayri_soyler()
    {
        var (vm, api) = await Kur();
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(("Yeni kart", "Kartı kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
        vm.KesimGunu = 0;
        vm.SonOdemeGunu = 40;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        Assert.Equal("Kesim günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.KesimGunu)]);
        Assert.Equal("Son ödeme günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.SonOdemeGunu)]);
        Assert.Equal(nameof(vm.Ad), vm.KartHatalari.IlkAlan);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);

        vm.Ad = "Bonus";
        Assert.Null(vm.KartHatalari[nameof(vm.Ad)]);
        vm.VazgecCommand.Execute(null);
        Assert.False(vm.KartHatalari.Var);
    }

    [Fact]
    public async Task Duzenleme_basligi_kartin_adini_soyler()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(("Düzenleniyor: Kart", "Değişikliği kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
    }

    [Fact]
    public async Task Yazilmis_form_baska_karta_ve_yeni_karta_gecmeden_once_onay_ister_ayni_kartin_formlari_arasinda_sormaz()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 250m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((1, KartFormu.Odeme, 250m), (vm.AcikKartId, vm.AcikForm, vm.OdemeTutari));
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.AcikKartId);

        vm.FormAcCommand.Execute(KartFormu.Harcama);   // aynı kart: sorulmaz
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        Assert.Equal(2, sorulan);

        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Yeni ad";
        cevap = true;
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((2, KartFormu.Yok), (vm.AcikKartId, vm.AcikForm));
        Assert.Equal("World", vm.Ad);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Degisiklikleri_birakmak_formu_kapatir_ve_kartin_kayitli_degerlerine_doner()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Değişti";
        vm.Limit = 5m;

        vm.DegisiklikleriBirak();

        Assert.Equal((KartFormu.Yok, "Kart", 1000m), (vm.AcikForm, vm.Ad, vm.Limit));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }
}
