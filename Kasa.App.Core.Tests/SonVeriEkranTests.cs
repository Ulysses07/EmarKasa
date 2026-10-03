using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>HD-01 ekran ekran (tasarım 2026-10-02 §3): yükleme hata verince son başarılı veri silinmez, eski işaretlenir; bağlantı
/// kopukken bağlantı hatası sayfaya yazılmaz.</summary>
public class SonVeriEkranTests
{
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    [Fact]
    public async Task Kartlar_hatada_son_listeyi_korur_ve_eski_isaretler()
    {
        var finans = Finans();
        var vm = new KartTakipViewModel(new FinansTakipTests.Sahte(), finans, Editor());
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();

        Assert.Single(vm.Kartlar);
        Assert.True(vm.VeriEski);
        Assert.True(vm.GovdeGorunur);
        Assert.Contains("Sunucuya ulaşılamadı", vm.SayfaHatasi);
    }

    [Fact]
    public async Task Krediler_kopukken_hatayi_sayfaya_yazmaz_son_listeyi_korur()
    {
        var finans = Finans();
        var auth = Editor();
        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth);
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();
        auth.Baglanti.Ulasilamadi();

        await vm.YukleAsync();

        Assert.Single(vm.Krediler);
        Assert.True(vm.VeriEski);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Cekler_hatada_son_listeyi_korur()
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [CekTakipViewModelTests.Cek(1)] };
        var finans = Finans();
        var vm = new CekTakipViewModel(api, finans, Editor(), new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 25)));
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();

        Assert.Single(vm.Cekler);
        Assert.True(vm.VeriEski);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Islemler_ayni_suzgecin_yenilemesinde_listeyi_korur_kopukken_hata_yazmaz()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)], IslemlerListe = [new IslemDto(1, new DateOnly(2026, 7, 8), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null)] };
        var auth = Editor();
        var vm = new IslemlerViewModel(api, auth, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15)));
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();
        Assert.Single(vm.Islemler);
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);
        Assert.Contains("Sunucuya ulaşılamadı", vm.YuklemeHatasi);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);

        auth.Baglanti.Ulasilamadi();
        await vm.YukleAsync();
        Assert.Null(vm.YuklemeHatasi);
        Assert.Single(vm.Islemler);

        api.YuklemeHatasi = null;
        await vm.YukleAsync();
        Assert.False(vm.VeriEski);
    }

    [Fact]
    public async Task Alislar_hatada_son_listeyi_ve_govdeyi_korur()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [] };
        var vm = new AlislarViewModel(api, new SahteApi(), Editor());
        await vm.YukleAsync();
        api.ListeGetir = () => Task.FromException<IReadOnlyList<AlisDto>>(new HttpRequestException());

        await vm.YukleAsync();

        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Aylik_giderler_hatada_son_veriyi_korur()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());

        await vm.YukleAsync();

        Assert.Single(vm.Kayitlar);
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
    }
}
