using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Aylık giderler (tasarım 2026-10-02 §1, §2, §5; AG-02): satır düğmesi "Öde" / "Ödemeyi iptal et", şablon ve ödeme formu
/// hataları alanda, yazılmış şablon formunda onay, kayıttan sonraki yenileme hatası formun hatası değildir.</summary>
public class AylikGiderFormuTests
{
    private static async Task<(AylikGiderViewModel Vm, KasaKontrolVeAylikGiderTests.Sahte Api)> Kur()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new AylikGiderViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Satir_dugmesi_odenmemiste_Ode_odenmiste_Odemeyi_iptal_et_der()
    {
        var (vm, api) = await Kur();
        Assert.Equal("Öde", AylikGiderViewModel.SatirDugmesi(vm.Kayitlar[0]));
        api.Odendi = true;
        await vm.YukleAsync();
        Assert.Equal("Ödemeyi iptal et", AylikGiderViewModel.SatirDugmesi(vm.Kayitlar[0]));
    }

    [Fact]
    public async Task Bos_sablon_formu_alanlari_ayri_ayri_soyler()
    {
        var (vm, api) = await Kur();
        vm.OdemeGunu = 0;

        await vm.SablonKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.Sablon);
        Assert.Equal("Ad / açıklama boş olamaz.", vm.SablonHatalari[nameof(vm.Ad)]);
        Assert.Equal("Gider türünü seçin.", vm.SablonHatalari[nameof(vm.Tur)]);
        Assert.Equal("Aylık tutar sıfırdan büyük olmalı.", vm.SablonHatalari[nameof(vm.Tutar)]);
        Assert.Equal("Ödeme günü 1 ile 31 arasında olmalı.", vm.SablonHatalari[nameof(vm.OdemeGunu)]);
        Assert.Equal("Dağılım biçimini seçin.", vm.SablonHatalari[nameof(vm.DagilimTuru)]);

        vm.DagilimTuru = vm.DagilimTurleri[1];
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Dağıtılacak kanalları seçin.", vm.SablonHatalari[nameof(vm.KanalSecimleri)]);
    }

    [Fact]
    public async Task Yazilmis_sablon_formu_yeniden_once_onay_ister()
    {
        var (vm, _) = await Kur();
        vm.Ad = "Kira";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Kira", vm.Ad);

        cevap = true;
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal(("", false), (vm.Ad, vm.KaydedilmemisDegisiklikVar));
    }

    [Fact]
    public async Task Odeme_onaysiz_kaydedilmez_hata_onay_kutusunun_altinda()
    {
        var (vm, api) = await Kur();
        vm.OdemeSec(vm.Kayitlar[0]);

        await vm.OdeCommand.ExecuteAsync(null);

        Assert.Empty(api.Odemeler);
        Assert.Equal("Gösterilen ödeme tutarını ve kanal etkisini onaylayın.", vm.OdemeHatalari[nameof(vm.OdemeOnay)]);
        vm.OdemeOnay = true;
        Assert.False(vm.OdemeHatalari.Var);
    }

    [Fact]
    public async Task Kayittan_sonraki_yenileme_hatasi_formun_hatasi_degil_sayfanin_okuma_hatasidir()
    {
        var (vm, api) = await Kur();
        vm.OdemeSec(vm.Kayitlar[0]);
        vm.OdemeOnay = true;
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());

        await vm.OdeCommand.ExecuteAsync(null);

        Assert.Single(api.Odemeler);
        Assert.False(vm.OdemeHatalari.Var);
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", vm.Hata);
        Assert.True(vm.VeriEski);
    }

    /// <summary>Liste yenilemesi yazılmış şablon formunu ezmez: alanlar ve kanal seçimi kalır, değişiklik izlenmeye devam eder.</summary>
    [Fact]
    public async Task Yenileme_yazilmis_sablon_formunu_korur()
    {
        var (vm, _) = await Kur();
        vm.Ad = "Kira";
        vm.KanalSecimleri[0].Secili = true;

        await vm.YukleAsync();

        Assert.Equal("Kira", vm.Ad);
        Assert.True(vm.KanalSecimleri[0].Secili);
        Assert.True(vm.KaydedilmemisDegisiklikVar);
    }
}
