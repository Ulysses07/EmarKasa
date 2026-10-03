using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class RaporDurumuTests
{
    private static AylikRaporDto Ay(int ay) => new(2026, ay, new List<KanalAylikDto>());

    [Fact]
    public async Task Ay_degistirirken_hata_olursa_onceki_ayin_verisi_gosterilmez_ve_tekrar_denenir()
    {
        var api = new SahteApi { AylikRapor = Ay(8) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        await vm.SonrakiAyCommand.ExecuteAsync(null);

        Assert.Equal(9, vm.Ay);
        Assert.Null(vm.Rapor);
        Assert.False(vm.VeriVar);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
        // K-5: ay değişince önceki ayın "Son güncelleme: …" zaman damgası da temizlenir (yeni ay henüz hiç yüklenmedi).
        Assert.Null(vm.SonGuncelleme);

        api.YuklemeHatasi = null;
        api.AylikRapor = Ay(9);
        await vm.YenileCommand.ExecuteAsync(null);
        Assert.True(vm.VeriVar);
        Assert.Equal(vm.Ay, vm.Rapor!.Ay);
        Assert.Null(vm.Hata);
    }

    /// <summary>Ekran denemesi G-3: Kasalar, Haftalık ve Aylık da öbür sayfalarla aynı biçimi yazar: "Son güncelleme: dd.MM.yyyy
    /// HH:mm" (saniyesiz); eski veride " · güncel olmayabilir" eki kalır.</summary>
    [Fact]
    public void Rapor_son_guncelleme_metni_takip_sayfalariyla_ayni_bicimdedir()
    {
        var an = new DateTimeOffset(2026, 10, 3, 18, 12, 23, TimeSpan.FromHours(3));
        var rapor = new HaftalikViewModel(new SahteApi()) { SonGuncelleme = an };
        var takip = new CekOzetViewModel(new CekTakipViewModelTests.Sahte(), TestOturumu.Ac()) { SonGuncelleme = an };

        Assert.Equal("Son güncelleme: 03.10.2026 18:12", rapor.SonGuncellemeMetni);
        Assert.Equal(takip.SonGuncellemeMetni, rapor.SonGuncellemeMetni);

        rapor.VeriEski = takip.VeriEski = true;
        Assert.Equal("Son güncelleme: 03.10.2026 18:12 · güncel olmayabilir", rapor.SonGuncellemeMetni);
        Assert.Equal(takip.SonGuncellemeMetni, rapor.SonGuncellemeMetni);
    }

    [Fact]
    public async Task Gec_donen_eski_ay_yeni_ayin_raporunu_ezmez()
    {
        var eski = new TaskCompletionSource<AylikRaporDto>();
        var yeni = new TaskCompletionSource<AylikRaporDto>();
        var api = new SahteApi { AylikGetir = (_, ay) => ay == 8 ? eski.Task : yeni.Task };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        var ilk = vm.YukleAsync();
        var sonraki = vm.SonrakiAyCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul);
        Assert.False(vm.VeriVar);

        yeni.SetResult(Ay(9));
        await sonraki;
        eski.SetResult(Ay(8));
        await ilk;

        Assert.Equal(9, vm.Ay);
        Assert.Equal(9, vm.Rapor!.Ay);
        Assert.True(vm.VeriVar);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Eski_istegin_hatasi_yeni_basarili_raporu_gizlemez()
    {
        var eski = new TaskCompletionSource<AylikRaporDto>();
        var api = new SahteApi { AylikGetir = (_, ay) => ay == 8 ? eski.Task : Task.FromResult(Ay(9)) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        var ilk = vm.YukleAsync();
        await vm.SonrakiAyCommand.ExecuteAsync(null);
        eski.SetException(new HttpRequestException());
        await ilk;
        Assert.Null(vm.Hata);
        Assert.True(vm.VeriVar);
        Assert.Equal(9, vm.Rapor!.Ay);
    }

    [Fact]
    public async Task Panel_ilk_yukleme_hatasinda_sifir_bakiye_gosterilmez()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new HttpRequestException() });
        await vm.YukleAsync();
        Assert.False(vm.VeriVar);
        Assert.Null(vm.SonGuncelleme);
        Assert.NotNull(vm.Hata);
    }

    /// <summary>HD-01: yenileme ve hata son başarılı bakiyeyi silmez; hata sonrası veri eski işaretlenir, başarı işareti kaldırır.</summary>
    [Fact]
    public async Task Panel_yenilenirken_ve_hatada_onceki_bakiye_korunur_ve_eski_isaretlenir()
    {
        var api = new SahteApi { Panel = new PanelDto(123m, new List<KanalBakiyeDto>(), 0, 0) };
        var vm = new PanelViewModel(api);
        var yuklendi = 0;
        vm.Yuklendi += (_, _) => yuklendi++;
        await vm.YukleAsync();
        Assert.True(vm.VeriVar);
        Assert.Equal(1, yuklendi);
        var bekleyen = new TaskCompletionSource<PanelDto>();
        api.PanelGetir = () => bekleyen.Task;
        var yenile = vm.YukleAsync();
        Assert.True(vm.VeriVar);
        Assert.True(vm.Mesgul);
        bekleyen.SetException(new HttpRequestException());
        await yenile;
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);
        Assert.Equal(123m, vm.GuncelKasa);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);
        Assert.Equal(1, yuklendi);

        api.PanelGetir = null;
        await vm.YukleAsync();
        Assert.False(vm.VeriEski);
        Assert.DoesNotContain("güncel olmayabilir", vm.SonGuncellemeMetni);
        Assert.Equal(2, yuklendi);
    }

    /// <summary>K-4: oturum değişince (çıkış, yeni giriş) son başarılı veri sıfırlanır; başka kullanıcıyla girişte eski bakiyeler
    /// görünmez.</summary>
    [Fact]
    public async Task Oturum_degisince_panelin_son_verisi_sifirlanir()
    {
        var auth = TestOturumu.Ac();
        var vm = new PanelViewModel(new SahteApi { Panel = new PanelDto(123m, new List<KanalBakiyeDto>(), 0, 0) }, auth: auth);
        await vm.YukleAsync();
        Assert.True(vm.VeriVar);
        Assert.NotNull(vm.SonGuncelleme);

        TestOturumu.YeniOturum(auth, Rol.Editor);

        Assert.False(vm.VeriVar);
        Assert.Null(vm.SonGuncelleme);
        Assert.False(vm.VeriEski);
    }

    [Fact]
    public void Hic_yukleme_yokken_iki_sayfa_ailesi_ayni_metni_yazar()
    {
        Assert.Equal("Henüz yüklenmedi.", new HaftalikViewModel(new SahteApi()).SonGuncellemeMetni);
        Assert.Equal("Henüz yüklenmedi.", new KrediTakipViewModel(new FinansTakipTests.Sahte(), new SahteApi(), TestOturumu.Ac()).SonGuncellemeMetni);
    }

    [Fact]
    public async Task Ayni_ayin_yenilemesinde_rapor_korunur_ay_degisince_kalkar()
    {
        var api = new SahteApi { AylikRapor = Ay(8) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        await vm.YenileCommand.ExecuteAsync(null);
        Assert.Equal(8, vm.Rapor!.Ay);
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);

        vm.Ay = 9;
        Assert.Null(vm.Rapor);
        Assert.False(vm.VeriVar);
        Assert.False(vm.VeriEski);
        // K-5: ay değişince son güncelleme zaman damgası da temizlenir (önceki ayın "Son güncelleme: …" satırı kalmaz).
        Assert.Null(vm.SonGuncelleme);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Sunucunun_dogrulama_ve_cakisma_mesaji_korunur(HttpStatusCode kod)
    {
        var vm = new HaftalikViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(kod, "Kanal kullanımda.") });
        await vm.YukleAsync();
        Assert.Equal("Kanal kullanımda.", vm.Hata);
        Assert.False(vm.VeriVar);
    }

    [Fact]
    public async Task Merkezi_401_bildirimi_auth_durumunu_kapatir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "token") };
        var vm = new AuthViewModel(api);
        await vm.GirisCommand.ExecuteAsync(null);
        var bildirildi = false;
        vm.OturumSonlandi += (_, _) => bildirildi = true;
        api.OturumuSonlandir();
        Assert.False(vm.GirisYapildi);
        Assert.True(bildirildi);
        Assert.Contains("Oturumunuz sona erdi", vm.Hata);
    }
}
