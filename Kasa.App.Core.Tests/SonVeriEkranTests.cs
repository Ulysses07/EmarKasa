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
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [AlislarViewModelTests.Alis()] };
        var vm = new AlislarViewModel(api, new SahteApi(), Editor());
        await vm.YukleAsync();
        Assert.Single(vm.Alislar);
        api.ListeGetir = () => Task.FromException<IReadOnlyList<AlisDto>>(new HttpRequestException());

        await vm.YukleAsync();

        Assert.Equal(7, Assert.Single(vm.Alislar).Veri.Id);
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.NotNull(vm.Hata);
    }

    /// <summary>Yükleme hatasından sonra soluk formda yazılan değer, sayfadaki "Yenile" ile sorulmadan silinmez (VeriHazir inmiş olsa da).</summary>
    [Fact]
    public async Task Alislar_hatadan_sonra_yazilan_form_yenilemede_korunur()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [AlislarViewModelTests.Alis()] };
        var vm = new AlislarViewModel(api, new SahteApi(), Editor());
        await vm.YukleAsync();
        api.ListeGetir = () => Task.FromException<IReadOnlyList<AlisDto>>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.False(vm.VeriHazir);
        vm.Tedarikci = "Yeni firma";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        api.ListeGetir = null;

        await vm.YukleAsync();

        Assert.Equal("Yeni firma", vm.Tedarikci);
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        Assert.Contains("Kaydedilmemiş değişiklikler var", vm.Hata);
    }

    /// <summary>Kısmi hata yarım ekran bırakmaz: kart, gider ya da alıcı isteği hata verirse kanallar ve alışlar da eski kalır.</summary>
    [Fact]
    public async Task Alislar_kismi_hatada_ekran_eski_veriyle_butun_kalir()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [AlislarViewModelTests.Alis()], AliciListe = [new AliciDto(5, "ayse", "Ayşe", true)] };
        var finans = new SahteApi { KrediKartlariListe = [new KrediKartiDto(2, "Takipli", new(2026, 1, 10), new(2026, 1, 20), 0, 0, YeniTakip: true, Aktif: true)] };
        var vm = new AlislarViewModel(api, finans, Editor());
        await vm.YukleAsync();
        Assert.Equal(2, vm.OdemeKartlari.Count);
        Assert.Single(vm.Alicilar);
        api.Liste = [AlislarViewModelTests.Alis(), AlislarViewModelTests.Alis() with { Id = 8 }];
        api.AliciListe = [];
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();

        Assert.Equal(7, Assert.Single(vm.Alislar).Veri.Id);
        Assert.Equal(new int?[] { null, 2 }, vm.OdemeKartlari.Select(k => k.Id));
        Assert.Equal(5, Assert.Single(vm.Alicilar).Id);
        Assert.Equal(2, vm.Kanallar.Count);
        Assert.True(vm.VeriEski);
        Assert.True(vm.GovdeGorunur);
    }

    /// <summary>Çekler'de son veri yalnız aynı sorgu içindir: başka süzgecin yüklemesi hata verirse önceki süzgecin listesi ve özeti
    /// yeni çipin altında "eski veri" diye gösterilmez.</summary>
    [Fact]
    public async Task Cekler_baska_suzgecte_hata_onceki_listeyi_gostermez()
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [CekTakipViewModelTests.Cek(1)] };
        var finans = Finans();
        var vm = new CekTakipViewModel(api, finans, Editor(), new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 25)));
        await vm.YukleAsync();
        Assert.Single(vm.Cekler);
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);

        Assert.Empty(vm.Cekler);
        Assert.Empty(vm.OncekiSatirlar);
        Assert.Null(vm.Ozet);
        Assert.False(vm.VeriEski);
        Assert.NotNull(vm.Hata);

        await vm.YukleAsync();                                     // aynı (yeni) süzgecin yenilemesi de hata: eski liste geri gelmez
        Assert.Empty(vm.Cekler);
        Assert.False(vm.VeriEski);

        finans.YuklemeHatasi = null;
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[0]);
        Assert.Single(vm.Cekler);
        Assert.False(vm.VeriEski);
    }

    /// <summary>İşlemler'de başka süzgecin hatası listeyi boşaltır; "Son güncelleme … · güncel olmayabilir" de kalmaz.</summary>
    [Fact]
    public async Task Islemler_baska_suzgecte_hata_eski_isareti_ve_son_guncellemeyi_kaldirir()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)], IslemlerListe = [new IslemDto(1, new DateOnly(2026, 7, 8), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null)] };
        var vm = new IslemlerViewModel(api, Editor(), zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15)));
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();
        await vm.YukleAsync();
        Assert.True(vm.VeriEski);

        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.First(c => c.Ad == "MEZAT"));

        Assert.Empty(vm.Islemler);
        Assert.False(vm.VeriEski);
        Assert.Equal(Bicim.HenuzYuklenmedi, vm.SonGuncellemeMetni);
    }

    /// <summary>Soluk (eski) listede "Öde" ve "Ödemeyi iptal et" aynı ölçütle durur ve aynı iletiyi verir.</summary>
    [Fact]
    public async Task Aylik_giderler_eski_listede_ode_ve_iptal_ayni_iletiyle_durur()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        var bekleyen = vm.Kayitlar[0];
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.True(vm.VeriEski);

        vm.OdemeSec(bekleyen);
        Assert.Null(vm.SeciliOdeme);
        Assert.Equal(AylikGiderViewModel.ListeGuncelDegil, vm.Hata);

        var odenmis = new AylikGiderSatiri(bekleyen.Veri with { Durum = "Odendi", OdemeId = 2 });
        vm.Hata = null;
        await vm.IptalAsync(odenmis, "Yanlış ödeme", vm.OturumNesli);
        Assert.Equal(0, api.IptalSayisi);
        Assert.Equal(AylikGiderViewModel.ListeGuncelDegil, vm.Hata);
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
