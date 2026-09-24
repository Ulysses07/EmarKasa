using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket E: kişisel giriş, iki adımlı kod adımı, oturumdaki kişi, geçmişte kişi + cihaz.</summary>
public class PaketEGirisTests
{
    private static AuthViewModel Vm(SahteApi api) => new(api) { Kullanici = "emar", Sifre = "gizli-sifre" };

    [Fact]
    public async Task Kisisel_giris_ad_ve_kullanici_idsini_tutar()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("viewer", "jwt"), GirisAd = "AHMET", GirisKullaniciId = 7 };
        var vm = Vm(api);

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.True(vm.GirisYapildi);
        Assert.Equal(Rol.Izleyici, vm.AktifRol);
        Assert.Equal("AHMET", vm.AktifAd);
        Assert.Equal(7, vm.AktifKullaniciId);
        Assert.Equal("AHMET · İzleyici", vm.AktifKimlik);
        Assert.Equal(("emar", "gizli-sifre", (string?)null), api.GirisCagrilari.Single());
    }

    [Fact]
    public async Task Ortak_sifrede_kimlik_yalniz_rol()
    {
        var vm = Vm(new SahteApi { LoginYaniti = new LoginYanit("viewer", "jwt") });
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Null(vm.AktifAd);
        Assert.Equal("İzleyici", vm.AktifKimlik);
    }

    [Fact]
    public async Task Iki_adimli_hesapta_once_kod_istenir_giris_yapilmaz()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), IkiAdimGerekli = true };
        var vm = Vm(api);

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.False(vm.GirisYapildi);
        Assert.True(vm.KodGerekli);
        Assert.Null(vm.Hata);                                   // ilk adım hata değil
        Assert.Equal("İki adımlı giriş kodunu girin.", vm.KodBilgisi);
        Assert.Equal("gizli-sifre", vm.Sifre);                  // şifre korunur: kodla birlikte yeniden gönderilir
    }

    [Fact]
    public async Task Dogru_kodla_ikinci_denemede_giris_yapilir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), IkiAdimGerekli = true, GirisAd = "EMAR" };
        var vm = Vm(api);
        await vm.GirisCommand.ExecuteAsync(null);

        vm.Kod = " 123456 ";
        await vm.GirisCommand.ExecuteAsync(null);

        Assert.True(vm.GirisYapildi);
        Assert.False(vm.KodGerekli);
        Assert.Null(vm.Kod);
        Assert.Equal("", vm.Sifre);
        Assert.Equal(Rol.Editor, vm.AktifRol);
        Assert.Equal(("emar", "gizli-sifre", "123456"), api.GirisCagrilari[1]);
    }

    [Fact]
    public async Task Hatali_kod_sunucu_mesajini_gosterir_kod_adiminda_kalir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), IkiAdimGerekli = true };
        var vm = Vm(api);
        await vm.GirisCommand.ExecuteAsync(null);

        vm.Kod = "000000";
        await vm.GirisCommand.ExecuteAsync(null);

        Assert.False(vm.GirisYapildi);
        Assert.True(vm.KodGerekli);
        Assert.Equal("Kod hatalı. Uygulamadaki güncel kodu girin.", vm.Hata);
        Assert.Null(vm.Kod);
    }

    [Fact]
    public async Task Kilit_mesaji_kod_bilgisinde_gorunur()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), IkiAdimGerekli = true, KodIstekMesaji = "Çok fazla hatalı kod denendi; 15 dakika sonra tekrar deneyin." };
        var vm = Vm(api);
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Çok fazla hatalı kod denendi; 15 dakika sonra tekrar deneyin.", vm.KodBilgisi);
    }

    [Fact]
    public async Task Kod_iptal_sifre_adimina_doner()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), IkiAdimGerekli = true };
        var vm = Vm(api);
        await vm.GirisCommand.ExecuteAsync(null);

        vm.KodIptalCommand.Execute(null);

        Assert.False(vm.KodGerekli);
        Assert.Null(vm.KodBilgisi);
        Assert.Equal("", vm.Sifre);
    }

    [Fact]
    public async Task Pasif_hesap_sunucu_mesaji_gosterilir()
    {
        var vm = Vm(new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.Unauthorized, "Bu hesap pasif. Editörle görüşün.") });
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Bu hesap pasif. Editörle görüşün.", vm.Hata);
    }

    [Fact]
    public async Task Acilista_me_kisi_adini_tutar_cikista_temizlenir()
    {
        var api = new SahteApi { MeRol = "editor", MeAd = "EMAR", MeKullaniciId = 1 };
        var vm = new AuthViewModel(api);

        Assert.True(await vm.AcilistaDogrulaAsync());
        Assert.Equal("EMAR", vm.AktifAd);
        Assert.Equal("EMAR · Editör", vm.AktifKimlik);

        await vm.CikisAsync();
        Assert.Null(vm.AktifAd);
        Assert.Null(vm.AktifKullaniciId);
    }

    [Fact]
    public async Task Oturum_bitince_yarim_kod_adimi_temizlenir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt"), GirisAd = "EMAR" };
        var vm = Vm(api);
        await vm.GirisCommand.ExecuteAsync(null);

        api.OturumuBitir();

        Assert.False(vm.GirisYapildi);
        Assert.Null(vm.AktifAd);
        Assert.False(vm.KodGerekli);
    }

    [Fact]
    public void Gecmis_satiri_kisi_ve_cihazi_gosterir()
    {
        var d = new DegisiklikDto(1, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), "editor", "İşlem", 5, "Eklendi", "özet",
            null, "{}", false, null, false, Kullanici: "EMAR", Cihaz: "EMAR-LAPTOP");
        var s = new GecmisSatiri(d, TimeZoneInfo.Utc);
        Assert.Equal("Editör · EMAR · EMAR-LAPTOP", s.Kim);
        Assert.Equal("24.09.2026 09:00 · Editör · EMAR · EMAR-LAPTOP · İşlem", s.Ayrinti);
    }

    [Fact]
    public void Eski_gecmis_satiri_yalniz_rol_gosterir()
    {
        var d = new DegisiklikDto(1, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), "viewer", "Soru", 5, "Eklendi", "özet",
            null, "{}", false, null, false);
        var s = new GecmisSatiri(d, TimeZoneInfo.Utc);
        Assert.Equal("İzleyici", s.Kim);
        Assert.Equal("24.09.2026 09:00 · İzleyici · Soru", s.Ayrinti);
    }

    [Fact]
    public void Sorular_iki_rolde_de_gorunur_gecmisten_once()
    {
        foreach (var rol in new[] { Rol.Izleyici, Rol.Editor })
        {
            var b = SekmeModeli.Bolumler(rol).ToList();
            Assert.Contains(Bolum.Sorular, b);
            Assert.True(b.IndexOf(Bolum.Sorular) < b.IndexOf(Bolum.Gecmis));
        }
    }

    [Fact]
    public void Yonlendirme_sorular_rotasini_tanir()
        => Assert.Equal("//sorular", Yonlendirme.RotaCoz("sorular"));
}
