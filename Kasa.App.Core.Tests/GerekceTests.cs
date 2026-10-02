using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Gerekçe penceresi (<see cref="OturumluViewModel.GerekceyleAsync"/>): sayfalar gerekçeyi (iptal, durum değişimi, belge kaldırma,
/// ay kilidi) yalnız bu yoldan ister. Oturum pencere açılmadan önce yakalanır; pencere açıkken oturum değişirse işlem gönderilmez
/// ve gerekçe yeni oturumun formuna yazılmaz. Vazgeçilirse ya da gerekçe boşsa işlem yapılmaz.
/// </summary>
public class GerekceTests
{
    private const string EskiGerekce = "Önceki oturumun gerekçesi";

    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    private static async Task<(KartTakipViewModel Vm, TakipKomutlariTests.KaydedenTakipApi Api, AuthViewModel Auth)> KartVm()
    {
        var (vekil, api) = TakipKomutlariTests.KaydedenTakipApi.Olustur();
        var auth = TestOturumu.Ac();
        var vm = new KartTakipViewModel(vekil, Finans(), auth);
        await vm.YukleAsync();
        vm.Sec(vm.Kartlar[0]);
        return (vm, api, auth);
    }

    private static async Task<(KrediTakipViewModel Vm, TakipKomutlariTests.KaydedenTakipApi Api, AuthViewModel Auth)> KrediVm()
    {
        var (vekil, api) = TakipKomutlariTests.KaydedenTakipApi.Olustur();
        var auth = TestOturumu.Ac();
        var vm = new KrediTakipViewModel(vekil, Finans(), auth);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Krediler[0]);
        return (vm, api, auth);
    }

    /// <summary>KartTakipPage ve KrediTakipPage'in eski gerekçe akışı: pencereden sonra vm.Gerekce yazılıp işlem çağrılıyordu;
    /// oturum pencere açılmadan yakalanmıyordu.</summary>
    private static async Task EskiSayfaAkisi(Task<string?> pencere, Action<string> gerekceYaz, Func<Task> islem)
    {
        var reason = await pencere;
        if (!string.IsNullOrWhiteSpace(reason))
        { gerekceYaz(reason); await islem(); }
    }

    [Fact]
    public async Task Gerekce_verilince_islem_ayni_oturumda_gerekceyle_gonderilir()
    {
        var (vm, api, _) = await KartVm();
        var yakalanan = -1;
        await vm.GerekceyleAsync(() => Task.FromResult<string?>("  Kart kapandı  "), (g, oturum) => { yakalanan = oturum; vm.Gerekce = g; return vm.DurumDegistirAsync(); });
        Assert.Equal("Kart kapandı", api.Tek<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKartDurumAsync)).Govde.Aciklama);
        Assert.Equal(vm.OturumNesli, yakalanan);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Vazgecilince_ya_da_gerekce_bossa_islem_yapilmaz(string? yanit)
    {
        var (vm, api, _) = await KartVm();
        var cagrildi = false;
        await vm.GerekceyleAsync(() => Task.FromResult(yanit), (_, _) => { cagrildi = true; return Task.CompletedTask; });
        Assert.False(cagrildi);
        Assert.Empty(api.Cagrilar);
    }

    [Fact]
    public async Task Bos_gerekce_kabul_edilen_islemde_yalniz_vazgecme_durdurur()
    {
        var (vm, _, _) = await KartVm();
        var gelen = new List<string>();
        await vm.GerekceyleAsync(() => Task.FromResult<string?>(null), (g, _) => { gelen.Add(g); return Task.CompletedTask; }, bosGerekceGecerli: true);
        await vm.GerekceyleAsync(() => Task.FromResult<string?>(""), (g, _) => { gelen.Add(g); return Task.CompletedTask; }, bosGerekceGecerli: true);
        Assert.Equal([""], gelen);
    }

    /// <summary>Bulunan hata: pencere açıkken oturum değişir ve yeni oturumda aynı kart yeniden açılırsa eski sayfa akışı işlemi
    /// eski oturumun gerekçesiyle yeni oturum adına gönderiyordu. GerekceyleAsync göndermez.</summary>
    [Fact]
    public async Task Pencere_acikken_oturum_degisirse_islem_yeni_oturumda_gonderilmez()
    {
        var (eskiVm, eskiApi, eskiAuth) = await KartVm();
        var eskiPencere = new TaskCompletionSource<string?>();
        var eskiAkis = EskiSayfaAkisi(eskiPencere.Task, g => eskiVm.Gerekce = g, eskiVm.DurumDegistirAsync);
        await YeniOturumdaAyniKart(eskiVm, eskiAuth);
        eskiPencere.SetResult(EskiGerekce);
        await eskiAkis;
        Assert.Equal(EskiGerekce, eskiApi.Tek<TakipDurumYaz>(nameof(IFinansTakipApi.TakipKartDurumAsync)).Govde.Aciklama);

        var (vm, api, auth) = await KartVm();
        var pencere = new TaskCompletionSource<string?>();
        var akis = vm.GerekceyleAsync(() => pencere.Task, (g, _) => { vm.Gerekce = g; return vm.DurumDegistirAsync(); });
        await YeniOturumdaAyniKart(vm, auth);
        pencere.SetResult(EskiGerekce);
        await akis;
        Assert.Empty(api.Cagrilar);
        Assert.Equal("", vm.Gerekce);
        Assert.Null(vm.Hata);

        static async Task YeniOturumdaAyniKart(KartTakipViewModel vm, AuthViewModel auth)
        {
            auth.OturumSurumu++;
            await vm.YukleAsync();
            vm.Sec(vm.Kartlar[0]);
        }
    }

    /// <summary>Bulunan hata: pencere açıkken oturum sona ererse eski sayfa akışı eski oturumun gerekçesini sıfırlanmış forma
    /// yazıyordu (kredide "Değişiklik açıklaması" / "Kapama açıklaması" alanı); sonraki girişte bu metin formda kalıyordu.</summary>
    [Fact]
    public async Task Pencere_acikken_oturum_sona_ererse_gerekce_forma_yazilmaz()
    {
        var (eskiVm, _, eskiAuth) = await KrediVm();
        var eskiPencere = new TaskCompletionSource<string?>();
        var eskiAkis = EskiSayfaAkisi(eskiPencere.Task, g => eskiVm.Gerekce = g, eskiVm.DurumDegistirAsync);
        eskiAuth.OturumSurumu++;
        eskiPencere.SetResult(EskiGerekce);
        await eskiAkis;
        Assert.Equal(EskiGerekce, eskiVm.Gerekce);

        var (vm, api, auth) = await KrediVm();
        var pencere = new TaskCompletionSource<string?>();
        var akis = vm.GerekceyleAsync(() => pencere.Task, (g, _) => { vm.Gerekce = g; return vm.DurumDegistirAsync(); });
        auth.OturumSurumu++;
        pencere.SetResult(EskiGerekce);
        await akis;
        Assert.Equal("", vm.Gerekce);
        Assert.Empty(api.Cagrilar);
        Assert.Null(vm.Hata);
    }
}
