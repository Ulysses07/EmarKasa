using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// contract-6: masaüstü düzenlemede okunan kaydın sürümünü gönderir (gider, gelir, kanal, ayarlar). Kayıt arada başka oturumda
/// değiştiyse sunucu 409 verir: ileti gösterilir, liste/ayarlar/dönem gelirleri yeniden yüklenir.
/// </summary>
public class CekirdekSurumVmTests
{
    private const string GiderIletisi = "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";

    [Fact]
    public async Task Gider_duzenlemesi_okunan_surumu_gonderir_yeni_gider_sifir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        vm.Duzenle(new IslemDto(5, new DateOnly(2026, 3, 5), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null, Surum: 4));
        vm.DuzenTutar = 80m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal((5, 80m, 4), (api.SonIslemGuncelle!.Value.Id, api.SonIslemGuncelle.Value.G.TutarTl, api.SonIslemGuncelle.Value.G.Surum));

        // Kayıttan sonra form yeni gidere döner: sürüm 0 (sunucu oluşturmada yok sayar).
        vm.DuzenTarih = new DateTime(2026, 3, 5);
        vm.DuzenCari = "Yeni";
        vm.DuzenTutar = 10m;
        vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.SonIslemOlustur!.Surum);
    }

    [Fact]
    public async Task Gider_surum_cakismasinda_ileti_gosterilir_liste_yenilenir_form_korunur()
    {
        var api = new SahteApi { IslemGuncelleHatasi = new KasaApiException(HttpStatusCode.Conflict, GiderIletisi) };
        var vm = new IslemlerViewModel(api);
        vm.Duzenle(new IslemDto(5, new DateOnly(2026, 3, 5), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null, Surum: 0));
        vm.DuzenTutar = 80m;
        var okuma = api.IslemlerCagri;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(GiderIletisi, vm.Hata);
        Assert.True(api.IslemlerCagri > okuma, "Liste güncel kayıtlarla yenilendi.");
        Assert.Equal((5, 80m), (vm.DuzenId, vm.DuzenTutar));
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Gelir_kaydi_secili_satirin_surumunu_satir_yoksa_sifir_gonderir()
    {
        var hafta = new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);
        var api = new SahteApi
        {
            KanallarListe = [new(1, "MEZAT", true, 0, 0m), new(2, "PERAKENDE", true, 1, 0m)],
            DonemlerListe = [hafta],
            GelenlerListe = [new GelenDto(5, hafta.Start, "MEZAT", 5000m, KanalId: 1, Surum: 6)],
        };
        var vm = new IslemlerViewModel(api, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 23)));
        await vm.YukleAsync();

        vm.SecGelenKanalCommand.Execute(vm.GelenKanallari.First(c => c.Ad == "MEZAT"));
        vm.GelenTutar = 5500m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal((5500m, 6), (api.SonGelen!.TutarTl, api.SonGelen.Surum));

        vm.SecGelenKanalCommand.Execute(vm.GelenKanallari.First(c => c.Ad == "PERAKENDE"));
        vm.GelenTutar = 700m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal((700m, 0), (api.SonGelen!.TutarTl, api.SonGelen.Surum));
    }

    [Fact]
    public async Task Ayar_ve_kanal_kaydi_okunan_surumu_gonderir_kayittan_sonra_guncel_surum_okunur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 1000m, true, Surum: 3),
            KanallarListe = [new(1, "MEZAT", true, 0, 0m, Surum: 2)],
        };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();

        vm.KasaAcilisDevri = 1500m;
        await vm.AyarKaydetCommand.ExecuteAsync(null); // sahte sunucu kaydı yansıtır ve sürümü 4 yapar
        Assert.Equal((1500m, 3), (api.SonAyar!.KasaAcilisDevri, api.SonAyar.Surum));
        Assert.Equal(1500m, vm.KasaAcilisDevri);
        vm.KasaAcilisDevri = 1600m;
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal((1600m, 4), (api.SonAyar!.KasaAcilisDevri, api.SonAyar.Surum));

        vm.KanalDuzenle(vm.Kanallar.Single());
        vm.DuzenKanalSira = 5;
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal((1, 5, 2), (api.SonKanalGuncelle!.Value.Id, api.SonKanalGuncelle.Value.G.Sira, api.SonKanalGuncelle.Value.G.Surum));
        vm.DuzenKanalAd = "YENİ";
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.SonKanalOlustur!.Surum);
    }

    [Fact]
    public async Task Ayar_ve_kanal_surum_cakismasinda_ileti_gosterilir_guncel_degerler_yuklenir()
    {
        const string ayarIletisi = "Ayarlar başka bir oturumda değişti. Güncel değerleri yükleyip tekrar deneyin.";
        const string kanalIletisi = "Kanal başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 1000m, true, Surum: 3),
            KanallarListe = [new(1, "MEZAT", true, 0, 0m, Surum: 2)],
            AyarGuncelleHatasi = new KasaApiException(HttpStatusCode.Conflict, ayarIletisi),
            KanalGuncelleHatasi = new KasaApiException(HttpStatusCode.Conflict, kanalIletisi),
        };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();

        vm.KasaAcilisDevri = 1500m;
        api.AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 2000m, true, Surum: 4);
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(ayarIletisi, vm.Hata);
        Assert.Equal(2000m, vm.KasaAcilisDevri); // başka oturumun kaydettiği güncel değer

        vm.KanalDuzenle(vm.Kanallar.Single());
        vm.DuzenKanalSira = 5;
        api.KanallarListe = [new(1, "MEZAT", false, 0, 0m, Surum: 3)];
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(kanalIletisi, vm.Hata);
        Assert.False(vm.Kanallar.Single().Aktif); // liste güncel kayıtlarla yenilendi
        Assert.Equal((1, 5), (vm.DuzenKanalId, vm.DuzenKanalSira)); // form korunur
    }
}
