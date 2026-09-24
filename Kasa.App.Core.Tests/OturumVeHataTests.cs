using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Merkezi 401 / oturum bitişi, çıkış, çevrimdışı açılış ve giriş hata mesajları (Y1, Y2, O5, O6, D5).</summary>
public class OturumTests
{
    private static async Task<(SahteApi api, AuthViewModel vm)> GirisYapilmis(string rol = "editor")
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit(rol, "jwt") };
        var vm = new AuthViewModel(api) { Sifre = "s" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.True(vm.GirisYapildi);
        return (api, vm);
    }

    [Fact]
    public async Task Api_401_olayinda_giris_durumu_sifirlanir_ve_turkce_mesaj_gosterilir()
    {
        var (api, vm) = await GirisYapilmis();

        api.OturumuBitir(OturumBitisNedeni.Yetkisiz);

        Assert.False(vm.GirisYapildi);
        Assert.Equal("Oturumunuz sona erdi, tekrar giriş yapın", vm.Hata);
    }

    [Fact]
    public async Task Tum_oturumlari_kapatinca_login_e_doner()
    {
        var (api, vm) = await GirisYapilmis();

        api.OturumuBitir(OturumBitisNedeni.OturumlarKapatildi);

        Assert.False(vm.GirisYapildi);
        Assert.Equal(HataMesaji.OturumlarKapatildi, vm.Hata);
    }

    [Fact]
    public async Task Oturum_bitince_yeniden_giris_GirisYapildi_degisimini_bildirir()
    {
        // P11: GirisYapildi true kalsaydı yeniden girişte PropertyChanged gelmez, menü açılmazdı.
        var (api, vm) = await GirisYapilmis();
        api.OturumuBitir();
        var bildirimler = new List<bool>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AuthViewModel.GirisYapildi)) bildirimler.Add(vm.GirisYapildi); };

        vm.Sifre = "s";
        await vm.GirisCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true }, bildirimler);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Cikis_sunucu_hatasinda_da_yerel_oturumu_kapatir()
    {
        var (api, vm) = await GirisYapilmis();
        api.CikisHatasi = new HttpRequestException("ağ yok");

        await vm.CikisAsync();

        Assert.True(api.CikisCagrildi);
        Assert.False(vm.GirisYapildi);
    }

    [Fact]
    public async Task Acilista_ag_yoksa_cevrimdisi_durumu_login_yerine()
    {
        var api = new SahteApi { MeHatasi = new HttpRequestException("ağ yok") };
        var vm = new AuthViewModel(api);

        var girildi = await vm.AcilistaDogrulaAsync();

        Assert.False(girildi);
        Assert.True(vm.Cevrimdisi);
        Assert.False(vm.GirisYapildi);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Acilista_sunucuya_ulasilamazsa_cevrimdisi(HttpStatusCode kod)
    {
        var vm = new AuthViewModel(new SahteApi { MeHatasi = new KasaApiException(kod) });
        await vm.AcilistaDogrulaAsync();
        Assert.True(vm.Cevrimdisi);
    }

    [Fact]
    public async Task Acilista_zaman_asimi_cevrimdisi()
    {
        var vm = new AuthViewModel(new SahteApi { MeHatasi = new TaskCanceledException("timeout") });
        await vm.AcilistaDogrulaAsync();
        Assert.True(vm.Cevrimdisi);
    }

    [Fact]
    public async Task Acilista_401_cevrimdisi_degil_login()
    {
        var vm = new AuthViewModel(new SahteApi { MeHatasi = new KasaApiException(HttpStatusCode.Unauthorized) });
        await vm.AcilistaDogrulaAsync();
        Assert.False(vm.Cevrimdisi);
        Assert.False(vm.GirisYapildi);
    }

    [Fact]
    public async Task Tekrar_dene_ag_gelince_oturumu_acar()
    {
        var api = new SahteApi { MeHatasi = new HttpRequestException("ağ yok") };
        var vm = new AuthViewModel(api);
        await vm.AcilistaDogrulaAsync();
        Assert.True(vm.Cevrimdisi);

        api.MeHatasi = null;
        api.MeRol = "viewer";
        await vm.TekrarDeneCommand.ExecuteAsync(null);

        Assert.False(vm.Cevrimdisi);
        Assert.True(vm.GirisYapildi);
        Assert.Equal(Rol.Izleyici, vm.AktifRol);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public void Cevrimdisi_ekranindan_giris_formuna_gecilebilir()
    {
        var vm = new AuthViewModel(new SahteApi()) { Cevrimdisi = true };
        vm.GirisFormunaGecCommand.Execute(null);
        Assert.False(vm.Cevrimdisi);
    }

    [Fact]
    public async Task Login_429_sunucu_mesajini_gosterir()
    {
        var api = new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.TooManyRequests, "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.") };
        var vm = new AuthViewModel(api) { Sifre = "x" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.", vm.Hata);
    }

    [Fact]
    public async Task Login_500_sunucu_hatasi_der_bilgi_hatasi_demez()
    {
        var vm = new AuthViewModel(new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.InternalServerError) }) { Sifre = "x" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.SunucuHatasi, vm.Hata);
    }

    [Fact]
    public async Task Login_401_bilgi_hatasi_der()
    {
        var vm = new AuthViewModel(new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.Unauthorized) }) { Sifre = "x" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.GirisBasarisiz, vm.Hata);
    }

    [Fact]
    public async Task Login_ag_yoksa_ulasilamadi_der()
    {
        var vm = new AuthViewModel(new SahteApi { LoginHatasi = new HttpRequestException() }) { Sifre = "x" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
    }
}

public class HataMesajiTests
{
    [Fact]
    public void Durum_kodlari_anlamli_mesaja_cevrilir()
    {
        Assert.Equal(HataMesaji.OturumSonaErdi, HataMesaji.Coz(new KasaApiException(HttpStatusCode.Unauthorized)));
        Assert.Equal(HataMesaji.Yetkisiz, HataMesaji.Coz(new KasaApiException(HttpStatusCode.Forbidden)));
        Assert.Equal(HataMesaji.Bulunamadi, HataMesaji.Coz(new KasaApiException(HttpStatusCode.NotFound)));
        Assert.Equal(HataMesaji.GecersizIstek, HataMesaji.Coz(new KasaApiException(HttpStatusCode.BadRequest)));
        Assert.Equal(HataMesaji.SunucuHatasi, HataMesaji.Coz(new KasaApiException(HttpStatusCode.InternalServerError)));
        Assert.Equal(HataMesaji.Ulasilamadi, HataMesaji.Coz(new KasaApiException(HttpStatusCode.BadGateway)));
        Assert.Equal(HataMesaji.Ulasilamadi, HataMesaji.Coz(new HttpRequestException()));
        Assert.Equal(HataMesaji.Ulasilamadi, HataMesaji.Coz(new TaskCanceledException()));
    }

    [Fact]
    public void Sunucu_aciklamasi_varsa_o_gosterilir()
    {
        Assert.Equal("Tutar sıfırdan büyük olmalı.", HataMesaji.Coz(new KasaApiException(HttpStatusCode.BadRequest, "Tutar sıfırdan büyük olmalı.")));
        Assert.Equal("Aynı adda kanal var.", HataMesaji.Coz(new KasaApiException(HttpStatusCode.Conflict, "Aynı adda kanal var.")));
    }

    [Fact]
    public void Dogrulama_hatasi_mesaji_aynen_gosterilir()
        => Assert.Equal("Kart seçin.", HataMesaji.Coz(new DogrulamaHatasi("Kart seçin.")));

    [Fact]
    public async Task Okuma_sayfasinda_401_oturum_mesaji_gosterir_genel_ag_mesaji_degil()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.Unauthorized) });
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.OturumSonaErdi, vm.Hata);
    }
}

public class MesgulSayaciTests
{
    [Fact]
    public async Task Mesgul_ancak_tum_istekler_bitince_iner()
    {
        var ilk = new TaskCompletionSource<AylikRaporDto>();
        var ikinci = new TaskCompletionSource<AylikRaporDto>();
        var kuyruk = new Queue<TaskCompletionSource<AylikRaporDto>>(new[] { ilk, ikinci });
        var api = new SahteApi { AylikUret = (_, _) => kuyruk.Dequeue().Task };
        var vm = new AylikViewModel(api);

        var t1 = vm.YukleAsync();
        var t2 = vm.YukleAsync();
        ikinci.SetResult(new AylikRaporDto(2026, 9, new List<KanalAylikDto>()));
        await t2;
        Assert.True(vm.Mesgul);                  // ilk istek sürüyor

        ilk.SetResult(new AylikRaporDto(2026, 8, new List<KanalAylikDto>()));
        await t1;
        Assert.False(vm.Mesgul);
    }
}
