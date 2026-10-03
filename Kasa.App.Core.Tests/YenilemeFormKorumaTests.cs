using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Son incelemenin Ö-3 ve Küçük-4 bulguları: sayfanın Yenile'si ve kabuğun otomatik yenilemesi izlenmeyen formları
/// (Kartlar'da masraf ve ekstre, Aylık giderlerde ödeme) silmez; Kartlar'da kart bilgisi formunun Vazgeç'i bırakılan değerleri
/// formda bırakmaz.</summary>
public class YenilemeFormKorumaTests
{
    private static async Task<KartTakipViewModel> Kartlar()
    {
        var vm = new KartTakipViewModel(new FinansTakipTests.Sahte(), new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] }, TestOturumu.Ac());
        await vm.YukleAsync();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        return vm;
    }

    [Fact]
    public async Task Kartlar_masraf_formu_yenilemede_korunur()
    {
        var vm = await Kartlar();
        vm.FormAcCommand.Execute(KartFormu.Masraf);
        vm.MasrafEkstresi = vm.MasrafEkstreleri[0];
        vm.MasrafTutari = 50m;
        vm.MasrafAciklama = "Faiz";

        await vm.YukleAsync();

        Assert.Equal(KartFormu.Masraf, vm.AcikForm);
        Assert.Equal(7, vm.MasrafEkstresi?.Veri.Id);
        Assert.Contains(vm.MasrafEkstresi, vm.MasrafEkstreleri);
        Assert.Equal(50m, vm.MasrafTutari);
        Assert.Equal("Faiz", vm.MasrafAciklama);
    }

    [Fact]
    public async Task Kartlar_ekstre_formu_yenilemede_korunur_ekstre_kimlikle_yeniden_eslenir()
    {
        var vm = await Kartlar();
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        vm.Gerekce = "Bankanın düzeltmesi";
        vm.AsgariVar = true;
        vm.AsgariTutar = 30m;

        await vm.YukleAsync();

        Assert.Equal(KartFormu.Ekstre, vm.AcikForm);
        Assert.Equal(7, vm.DuzenlenenEkstre?.Veri.Id);
        Assert.Contains(vm.DuzenlenenEkstre, vm.Ekstreler);
        Assert.Equal(("Bankanın düzeltmesi", true, 30m), (vm.Gerekce, vm.AsgariVar, vm.AsgariTutar));
    }

    /// <summary>Küçük-4: kart bilgisi formunda Vazgeç, yazılıp bırakılan değerleri kartın kayıtlı değerleriyle yeniden yazar.</summary>
    [Fact]
    public async Task Kartlar_kart_bilgisi_vazgecte_alanlar_kayitli_degerlere_doner()
    {
        var vm = await Kartlar();
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Değişti";
        vm.Limit = 5m;
        vm.KesimGunu = 20;
        vm.SonOdemeGunu = 28;

        vm.VazgecCommand.Execute(null);

        Assert.Equal(("Kart", 1000m, 1, 10), (vm.Ad, vm.Limit, vm.KesimGunu, vm.SonOdemeGunu));
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal("Kart", vm.Ad);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Aylik_gider_odeme_formu_yenilemede_acik_kalir()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(api, new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] }, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.OdemeSec(vm.Kayitlar[0]);
        vm.OdemeNotu = "Havale";
        vm.OdemeOnay = true;

        await vm.YukleAsync();

        Assert.True(vm.OdemeSecili);
        Assert.Equal(vm.Kayitlar[0], vm.SeciliOdeme);
        Assert.Equal("Havale", vm.OdemeNotu);
        Assert.True(vm.OdemeOnay);   // satır değişmedi: onay da kalır

        api.Odendi = true;            // satır artık ödenmiş: form kapanır
        await vm.YukleAsync();
        Assert.False(vm.OdemeSecili);
    }
}
