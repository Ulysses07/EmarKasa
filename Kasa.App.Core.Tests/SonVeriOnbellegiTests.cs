using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ekran denemesi H-1: Shell menüden geçişte sayfayı ve görünüm modelini yeniden kurar. Aynı oturumda ikinci kez kurulan
/// model, yüklemesi hata verse de aynı sorgunun son başarılı verisini eski (soluk) gösterir; oturum değişince göstermez.</summary>
public class SonVeriOnbellegiTests
{
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    private static void EskiVeriGosterir(OturumluViewModel vm)
    {
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.StartsWith("Son güncelleme: ", vm.SonGuncellemeMetni);
        Assert.EndsWith(Bicim.EskiVeriEki, vm.SonGuncellemeMetni);
    }

    private static void HicVeriYok(OturumluViewModel vm)
    {
        Assert.False(vm.GovdeGorunur);
        Assert.False(vm.VeriEski);
        Assert.Equal(Bicim.HenuzYuklenmedi, vm.SonGuncellemeMetni);
    }

    [Fact]
    public async Task Kartlar_yeniden_kurulunca_son_listeyi_ve_acik_karti_gosterir_oturum_degisince_gostermez()
    {
        var auth = TestOturumu.Ac();
        var finans = Finans();
        var api = new FinansTakipTests.Sahte();
        var ilk = new KartTakipViewModel(api, finans, auth);
        await ilk.YukleAsync();
        await ilk.KutuSecCommand.ExecuteAsync(ilk.Kartlar[0]);
        finans.YuklemeHatasi = new HttpRequestException();
        auth.Baglanti.Ulasilamadi();

        var ikinci = new KartTakipViewModel(api, finans, auth);
        await ikinci.YukleAsync();

        Assert.Single(ikinci.Kartlar);
        Assert.Equal(1, ikinci.AcikKartId);
        Assert.Null(ikinci.Hata);
        EskiVeriGosterir(ikinci);

        TestOturumu.YeniOturum(auth, Rol.Editor);
        var ucuncu = new KartTakipViewModel(api, finans, auth);
        await ucuncu.YukleAsync();
        Assert.Empty(ucuncu.Kartlar);
        HicVeriYok(ucuncu);
    }

    /// <summary>Bağlıyken de (18b): yeniden kurulan Kartlar, en son açık kartı açık getirir; yükleme başarılıysa veri güncel olur.</summary>
    [Fact]
    public async Task Kartlar_bagliyken_donuste_acik_kart_korunur_veri_guncellenir()
    {
        var auth = TestOturumu.Ac();
        var api = new FinansTakipTests.Sahte();
        var ilk = new KartTakipViewModel(api, Finans(), auth);
        await ilk.YukleAsync();
        await ilk.KutuSecCommand.ExecuteAsync(ilk.Kartlar[0]);

        var ikinci = new KartTakipViewModel(api, Finans(), auth);
        await ikinci.YukleAsync();

        Assert.Equal(1, ikinci.AcikKartId);
        Assert.False(ikinci.VeriEski);
        Assert.Equal(KartFormu.Yok, ikinci.AcikForm);
    }

    [Fact]
    public async Task Krediler_yeniden_kurulunca_son_listeyi_gosterir()
    {
        var auth = TestOturumu.Ac();
        var finans = Finans();
        await new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth).YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth);
        await vm.YukleAsync();

        Assert.Single(vm.Krediler);
        Assert.Single(vm.Kanallar);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        EskiVeriGosterir(vm);
    }

    [Fact]
    public async Task Cekler_yeniden_kurulunca_ayni_suzgecin_son_listesini_gosterir()
    {
        var auth = TestOturumu.Ac();
        var finans = Finans();
        var api = new CekTakipViewModelTests.Sahte { Liste = [CekTakipViewModelTests.Cek(1)] };
        var zaman = new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 25));
        await new CekTakipViewModel(api, finans, auth, zaman).YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        var vm = new CekTakipViewModel(api, finans, auth, zaman);
        await vm.YukleAsync();

        Assert.Single(vm.Cekler);
        Assert.NotNull(vm.Ozet);
        EskiVeriGosterir(vm);

        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);   // başka süzgeç: önbellekte yok, eski liste gösterilmez
        Assert.Empty(vm.Cekler);
        Assert.Equal(Bicim.HenuzYuklenmedi, vm.SonGuncellemeMetni);
    }

    [Fact]
    public async Task Aylik_giderler_yeniden_kurulunca_ayin_son_verisini_gosterir()
    {
        var auth = TestOturumu.Ac();
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        await new AylikGiderViewModel(api, Finans(), auth).YukleAsync();
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());

        var vm = new AylikGiderViewModel(api, Finans(), auth);
        await vm.YukleAsync();

        Assert.Single(vm.Kayitlar);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        EskiVeriGosterir(vm);
    }

    [Fact]
    public async Task Alislar_yeniden_kurulunca_son_listeyi_gosterir_form_bos_acilir()
    {
        var auth = TestOturumu.Ac();
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [AlislarViewModelTests.Alis()] };
        await new AlislarViewModel(api, new SahteApi(), auth).YukleAsync();
        api.ListeGetir = () => Task.FromException<IReadOnlyList<AlisDto>>(new HttpRequestException());

        var vm = new AlislarViewModel(api, new SahteApi(), auth);
        await vm.YukleAsync();

        Assert.Equal(7, Assert.Single(vm.Alislar).Veri.Id);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        EskiVeriGosterir(vm);
    }

    [Fact]
    public async Task Bildirimler_yeniden_kurulunca_son_listeyi_gosterir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun), SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun)];
        await new BildirimViewModel(o.Api, o.Auth, o.Nobetci).YukleAsync();
        o.Api.ListeHatasi = new HttpRequestException();

        var vm = new BildirimViewModel(o.Api, o.Auth, o.Nobetci);
        await vm.YukleAsync();

        Assert.Equal(2, vm.Bildirimler.Count);
        EskiVeriGosterir(vm);
    }

    /// <summary>İşlemler: yeniden kurulan sayfada son liste ve kanal çipleri (filtre ve gider formu) kopukken de gelir; gider formunda
    /// kanal seçilebilir.</summary>
    [Fact]
    public async Task Islemler_yeniden_kurulunca_son_listeyi_ve_kanal_ciplerini_gosterir()
    {
        var auth = TestOturumu.Ac();
        var api = new SahteApi
        {
            KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)],
            IslemlerListe = [new IslemDto(1, new DateOnly(2026, 7, 8), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null)],
        };
        var zaman = new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15));
        await new IslemlerViewModel(api, auth, zaman: zaman).YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();
        auth.Baglanti.Ulasilamadi();

        var vm = new IslemlerViewModel(api, auth, zaman: zaman);
        await vm.YukleAsync();

        Assert.Single(vm.Islemler);
        Assert.True(vm.VeriVar);
        Assert.Contains(vm.FiltreKanallari, c => c.Ad == "MEZAT");
        Assert.Contains(vm.GiderKanallari, c => c.Ad == "MEZAT");
        Assert.Equal(3, vm.TipCipleri.Count);
        Assert.Null(vm.YuklemeHatasi);
        Assert.True(vm.VeriEski);
        Assert.EndsWith(Bicim.EskiVeriEki, vm.SonGuncellemeMetni);

        TestOturumu.YeniOturum(auth, Rol.Editor);
        var yeni = new IslemlerViewModel(api, auth, zaman: zaman);
        await yeni.YukleAsync();
        Assert.Empty(yeni.Islemler);
        Assert.Empty(yeni.GiderKanallari);
    }

    [Fact]
    public async Task Haftalik_ve_aylik_yeniden_kurulunca_son_raporu_gosterir_oturum_degisince_gostermez()
    {
        var auth = TestOturumu.Ac();
        var bas = new DateOnly(2026, 9, 28);
        var api = new SahteApi
        {
            HaftalikListe = [new(new(bas, bas.AddDays(6), 2026, 40), Array.Empty<KanalHaftalikDto>(), 0, 0, 0, 900, 0, null)],
            AylikRapor = new AylikRaporDto(DateTime.Today.Year, DateTime.Today.Month, Array.Empty<KanalAylikDto>()),
        };
        await new HaftalikViewModel(api, auth: auth).YukleAsync();
        await new AylikViewModel(api, auth: auth).YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        var haftalik = new HaftalikViewModel(api, auth: auth);
        var aylik = new AylikViewModel(api, auth: auth);
        await haftalik.YukleAsync();
        await aylik.YukleAsync();

        Assert.Single(haftalik.Donemler);
        Assert.NotNull(aylik.Rapor);
        foreach (var vm in new RaporViewModel[] { haftalik, aylik })
        {
            Assert.True(vm.VeriVar);
            Assert.True(vm.VeriEski);
            Assert.EndsWith(Bicim.EskiVeriEki, vm.SonGuncellemeMetni);
        }

        TestOturumu.YeniOturum(auth, Rol.Editor);
        var yeni = new HaftalikViewModel(api, auth: auth);
        await yeni.YukleAsync();
        Assert.Empty(yeni.Donemler);
        Assert.False(yeni.VeriVar);
        Assert.Equal(Bicim.HenuzYuklenmedi, yeni.SonGuncellemeMetni);
    }

    [Fact]
    public void Onbellek_tur_uyusmazsa_ya_da_temizlenince_veri_vermez()
    {
        var onbellek = new SonVeriOnbellegi();
        var an = new DateTimeOffset(2026, 10, 3, 18, 0, 0, TimeSpan.FromHours(3));
        onbellek.Yaz("a", "metin", an);

        Assert.True(onbellek.Oku<string>("a", out var v, out var z));
        Assert.Equal(("metin", an), (v, z));
        Assert.False(onbellek.Oku<int>("a", out _, out _));
        onbellek.Temizle();
        Assert.False(onbellek.Oku<string>("a", out _, out _));
    }

    [Fact]
    public void Rol_degisince_onbellek_temizlenir()
    {
        var auth = TestOturumu.Ac();
        auth.SonVeri.Yaz("a", 1, DateTimeOffset.Now);

        auth.AktifRol = Rol.Izleyici;

        Assert.False(auth.SonVeri.Oku<int>("a", out _, out _));
    }

    /// <summary>Küçük-7: önbellek sınırsız büyümez; ekran başına (anahtarın "|" öncesi) en son kullanılan 10 sorgu tutulur, eskiler
    /// silinir. Okuma da kullanım sayılır; başka ekranın kayıtları etkilenmez.</summary>
    [Fact]
    public void Ekran_basina_son_on_sorgu_tutulur_en_eski_kullanilan_silinir()
    {
        var onbellek = new SonVeriOnbellegi();
        var an = DateTimeOffset.Now;
        onbellek.Yaz("Diger|x", 0, an);
        for (var i = 1; i <= 10; i++)
            onbellek.Yaz($"Aylik|{i}", i, an);
        Assert.True(onbellek.Oku<int>("Aylik|1", out _, out _));   // 1 yeniden kullanıldı: en eski artık 2

        onbellek.Yaz("Aylik|11", 11, an);
        onbellek.Yaz("Aylik|12", 12, an);

        Assert.Equal(10, SonVeriOnbellegi.EkranBasinaSinir);
        Assert.True(onbellek.Oku<int>("Aylik|1", out _, out _));
        Assert.False(onbellek.Oku<int>("Aylik|2", out _, out _));
        Assert.False(onbellek.Oku<int>("Aylik|3", out _, out _));
        for (var i = 4; i <= 12; i++)
            Assert.True(onbellek.Oku<int>($"Aylik|{i}", out _, out _));
        Assert.True(onbellek.Oku<int>("Diger|x", out _, out _));

        onbellek.Sil("Aylik|12");
        onbellek.Yaz("Aylik|13", 13, an);   // silinen yer açtı: başka kayıt düşmez
        Assert.True(onbellek.Oku<int>("Aylik|4", out _, out _));
    }
}
