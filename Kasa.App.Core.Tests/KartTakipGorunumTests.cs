using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kartlar ekranının arayüz durumu (tasarım 2026-09-30 §2): kutuya tıklamak kartı açar/kapatır, aynı anda tek form açıktır,
/// başka düğme açık formu değiştirir, başarılı kayıt ve Vazgeç formu kapatır, hata olursa form açık kalır ve hata formun
/// içinde gösterilir, kart değişince form kapanır, izleyici form açamaz, başka ekrandan gelen kart açık gelir. Mevcut
/// ödeme/harcama/masraf/geçiş testleri (FinansTakipTests, TakipKomutlariTests, OnizlemeOnayTests …) değişmeden geçer.
/// </summary>
public class KartTakipGorunumTests
{
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Vm(Rol rol = Rol.Editor, FinansTakipTests.Sahte? api = null,
        AuthViewModel? auth = null, IBenzerKayitApi? benzerlik = null, IKasaKontrolApi? kontrol = null)
    {
        api ??= new FinansTakipTests.Sahte();
        var vm = new KartTakipViewModel(api, Finans(), auth ?? TestOturumu.Ac(rol), benzerlik, kontrol);
        await vm.YukleAsync();
        return (vm, api);
    }

    private static FinansTakipTests.Sahte IkiKartli()
    {
        var kart = FinansTakipTests.Sahte.OrnekKart();
        return new FinansTakipTests.Sahte { KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([kart, kart with { Id = 2, Ad = "Akbank" }]) };
    }

    [Fact]
    public async Task Baslangicta_acik_kart_form_yok_varsayilan_sekme_ekstreler()
    {
        var (vm, _) = await Vm();
        Assert.Null(vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.FormAcik);
        Assert.Equal(KartSekmesi.Ekstreler, vm.SeciliSekme);
        Assert.Equal(new[] { true, false, false }, vm.Sekmeler.Select(s => s.Secili));
        Assert.Equal(new[] { "Ekstreler", "Harcamalar", "Ödemeler" }, vm.Sekmeler.Select(s => s.Ad));
    }

    [Fact]
    public async Task Kutuya_tiklamak_karti_acar_ayni_kutuya_tekrar_tiklamak_kapatir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Equal(1, vm.AcikKartId);
        Assert.True(vm.KartSecili);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Null(vm.AcikKartId);
        Assert.False(vm.KartSecili);
        Assert.False(vm.YeniKartFormuAcik);
    }

    [Fact]
    public async Task Ayni_anda_tek_form_acik_baska_dugme_acik_formu_degistirir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        Assert.True(vm.FormAcik);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
    }

    [Fact]
    public async Task Vazgec_formu_kapatir_ve_form_hatasini_temizler()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        vm.VazgecCommand.Execute(null);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Basarili_odeme_kaydindan_sonra_form_kapanir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);   // önizleme formu kapatmaz
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Single(api.OdemeIstekleri);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Contains("kaydedildi", vm.Mesaj);
        Assert.Equal(1, vm.AcikKartId);   // kart açık kalır
    }

    [Fact]
    public async Task Basarili_kart_bilgisi_ve_ekstre_kaydindan_sonra_form_kapanir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Limit = 2000;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.KartKayitSayisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);

        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.Equal(KartFormu.Ekstre, vm.AcikForm);
        vm.Gerekce = "Banka ekstresiyle kontrol edildi";
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.EkstreKayitSayisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.DuzenlenenEkstre);
    }

    [Fact]
    public async Task Hata_olursa_form_acik_kalir_ve_hata_formun_icinde_gosterilir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("Kart adını, limiti ve 1–31 arası günleri kontrol edin.", vm.FormHatasi);
        Assert.Null(vm.SayfaHatasi);
        vm.VazgecCommand.Execute(null);
        vm.Hata = "Sunucuya ulaşılamadı.";
        Assert.Null(vm.FormHatasi);
        Assert.Equal("Sunucuya ulaşılamadı.", vm.SayfaHatasi);
    }

    [Fact]
    public async Task Kart_degisince_form_kapanir_ve_sekme_ekstrelere_doner()
    {
        var (vm, _) = await Vm(api: IkiKartli());
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.SekmeSecCommand.Execute(vm.Sekmeler[2]);
        vm.KutuSecCommand.Execute(vm.Kartlar[1]);
        Assert.Equal(2, vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Equal(KartSekmesi.Ekstreler, vm.SeciliSekme);
    }

    [Fact]
    public async Task Izleyici_karti_acar_ama_form_acamaz()
    {
        var (vm, _) = await Vm(Rol.Izleyici);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Equal(1, vm.AcikKartId);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        vm.YeniKartAcCommand.Execute(null);
        Assert.False(vm.YeniKartFormuAcik);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.DuzenlenenEkstre);
    }

    [Fact]
    public async Task Rol_izleyiciye_donunce_acik_form_kapanir()
    {
        var auth = TestOturumu.Ac(Rol.Editor);
        var (vm, _) = await Vm(auth: auth);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        auth.AktifRol = Rol.Izleyici;
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Eski_takipte_yalniz_gecis_ve_kart_bilgisi_formu_acilir()
    {
        var api = new FinansTakipTests.Sahte { Kart = FinansTakipTests.Sahte.OrnekKart() with { YeniTakip = false } };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        foreach (var form in new[] { KartFormu.Odeme, KartFormu.Harcama, KartFormu.Masraf })
        {
            vm.FormAcCommand.Execute(form);
            Assert.Equal(KartFormu.Yok, vm.AcikForm);
        }
        vm.FormAcCommand.Execute(KartFormu.Gecis);
        Assert.Equal(KartFormu.Gecis, vm.AcikForm);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);   // eski takipte ekstre bilgisi düzenlenmez
        Assert.Null(vm.DuzenlenenEkstre);
        Assert.Equal(KartFormu.Gecis, vm.AcikForm);
    }

    [Fact]
    public async Task Ekstre_formundan_baska_forma_gecince_duzenlenen_ekstre_birakilir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.NotNull(vm.DuzenlenenEkstre);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        Assert.Null(vm.DuzenlenenEkstre);
        vm.FormAcCommand.Execute(KartFormu.Ekstre);   // seçili ekstre yokken ekstre formu açılmaz
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
    }

    [Fact]
    public async Task Liste_yenilenince_birakilan_ekstrenin_formu_kapanir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        await vm.YukleAsync();   // aynı kart yeniden seçilir (Sec), düzenlenen ekstre bırakılır
        Assert.Equal(1, vm.AcikKartId);
        Assert.Null(vm.DuzenlenenEkstre);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Baska_ekrandan_gelen_kart_acik_olur()
    {
        var (vm, _) = await Vm(api: IkiKartli());
        Assert.True(vm.IdIleSec(2));
        Assert.Equal(2, vm.AcikKartId);
        Assert.True(vm.KartSecili);
    }

    [Fact]
    public async Task Yeni_kart_kutusu_bos_kart_formunu_acar_kayittan_sonra_yeni_kart_acik_gelir()
    {
        var api = new FinansTakipTests.Sahte();
        api.YeniKartYaniti = api.Kart with { Id = 3, Ad = "Yeni kart" };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.YeniKartAcCommand.Execute(null);
        Assert.Null(vm.Secili);
        Assert.True(vm.YeniKartFormuAcik);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("", vm.Ad);
        vm.Ad = "Yeni kart";
        vm.Limit = 5000;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.KartKayit!.Value.Id);   // yeni kart olarak gitti
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.YeniKartFormuAcik);
        Assert.Equal(3, vm.AcikKartId);
        Assert.Equal(2, vm.Kartlar.Count);
    }

    [Fact]
    public async Task Yeni_kart_kutusuna_tekrar_tiklamak_formu_kapatir()
    {
        var (vm, _) = await Vm();
        vm.YeniKartAcCommand.Execute(null);
        vm.YeniKartAcCommand.Execute(null);
        Assert.False(vm.YeniKartFormuAcik);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Sekme_secimi_tek_sekmeyi_isaretler()
    {
        var (vm, _) = await Vm();
        vm.SekmeSecCommand.Execute(vm.Sekmeler[2]);
        Assert.Equal(KartSekmesi.Odemeler, vm.SeciliSekme);
        Assert.Equal(new[] { false, false, true }, vm.Sekmeler.Select(s => s.Secili));
        vm.SekmeSecCommand.Execute(vm.Sekmeler[1]);
        Assert.Equal(KartSekmesi.Harcamalar, vm.SeciliSekme);
        Assert.Equal(new[] { false, true, false }, vm.Sekmeler.Select(s => s.Secili));
    }

    // --- Hata yönlendirmesi: yalnız açık formun kendi komutunun hatası formda görünür ---

    [Fact]
    public async Task Form_acikken_iptal_reddi_ve_iptal_sunucu_hatasi_sayfada_gosterilir()
    {
        var harcama = new KartHarcamaDto(20, null, new DateOnly(2026, 9, 20), "Mal", 50, 1, false, [new TakipKanalPayi(1, "MEZAT", 50)]);
        var api = new FinansTakipTests.Sahte();
        api.Kart = api.Kart with { Harcamalar = [harcama, harcama with { Id = 21, EkstreKayitId = 5 }] };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);

        await vm.HarcamaIptalAsync(vm.Harcamalar.Single(h => h.Veri.Id == 21));   // istemci reddi
        Assert.Contains("Ekstre İçe Aktar", vm.SayfaHatasi);
        Assert.Null(vm.FormHatasi);

        vm.Gerekce = "Yanlış kayıt";
        api.IptalHatasi = new KasaApiException(HttpStatusCode.Conflict, "Kart başka bir işlemle değişti.");
        await vm.HarcamaIptalAsync(vm.Harcamalar.Single(h => h.Veri.Id == 20));   // sunucu 409
        Assert.Equal("Kart başka bir işlemle değişti.", vm.SayfaHatasi);
        Assert.Null(vm.FormHatasi);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
    }

    [Fact]
    public async Task Form_acikken_yukleme_hatasi_sayfada_gosterilir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        api.KartlarYaniti = Task.FromException<IReadOnlyList<KartTakipDto>>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.NotNull(vm.SayfaHatasi);
        Assert.Null(vm.FormHatasi);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
    }

    [Fact]
    public async Task Odeme_kaydi_sunucu_hatasi_formda_gosterilir_form_acik_kalir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        api.OdemeHata = true;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        Assert.NotNull(vm.FormHatasi);
        Assert.Null(vm.SayfaHatasi);
    }

    // --- Başarı yalnız işlemi başlatan formu kapatır ---

    [Fact]
    public async Task Odeme_kaydi_surerken_acilan_harcama_formu_odeme_basarisiyla_kapanmaz()
    {
        var bekleyen = new TaskCompletionSource<KartTakipDto>();
        var (vm, api) = await Vm();
        api.OdemeYaniti = bekleyen.Task;
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        var kayit = vm.OdemeKaydetCommand.ExecuteAsync(null);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        bekleyen.SetResult(api.Kart);
        await kayit;
        Assert.Contains("kaydedildi", vm.Mesaj);
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
    }

    [Fact]
    public async Task Odeme_kaydi_surerken_form_degisirse_odeme_hatasi_sayfada_gosterilir()
    {
        var bekleyen = new TaskCompletionSource<KartTakipDto>();
        var (vm, api) = await Vm();
        api.OdemeYaniti = bekleyen.Task;
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        var kayit = vm.OdemeKaydetCommand.ExecuteAsync(null);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        bekleyen.SetException(new HttpRequestException());
        await kayit;
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        Assert.NotNull(vm.SayfaHatasi);
        Assert.Null(vm.FormHatasi);
    }

    [Fact]
    public async Task Ekstre_kaydi_surerken_secilen_baska_ekstrenin_formu_acik_kalir()
    {
        var bekleyen = new TaskCompletionSource<KartTakipDto>();
        var api = new FinansTakipTests.Sahte();
        var ilk = api.Kart.Ekstreler[0];
        api.Kart = api.Kart with { Ekstreler = [ilk, ilk with { Id = 8, KesimTarihi = ilk.KesimTarihi.AddMonths(-1) }] };
        api.EkstreYaniti = bekleyen.Task;
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.EkstreSecCommand.Execute(vm.Ekstreler.Single(e => e.Veri.Id == 7));
        vm.Gerekce = "Banka ekstresiyle kontrol edildi";
        var kayit = vm.EkstreKaydetCommand.ExecuteAsync(null);
        vm.EkstreSecCommand.Execute(vm.Ekstreler.Single(e => e.Veri.Id == 8));
        bekleyen.SetResult(api.Kart);
        await kayit;
        Assert.Equal(KartFormu.Ekstre, vm.AcikForm);
        Assert.Equal(8, vm.DuzenlenenEkstre!.Veri.Id);
    }

    [Fact]
    public async Task Basarili_harcama_kaydindan_sonra_form_kapanir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        vm.HarcamaTutari = 10;
        vm.HarcamaAciklama = "Mal";
        await vm.HarcamaKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.Harcama);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Basarili_masraf_kaydindan_sonra_form_kapanir()
    {
        var kontrol = new KasaKontrolVeAylikGiderTests.Sahte();
        var (vm, _) = await Vm(kontrol: kontrol);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Masraf);
        vm.MasrafEkstresi = vm.MasrafEkstreleri[0];
        vm.MasrafTutari = 10;
        vm.MasrafAciklama = "Banka faizi";
        await vm.MasrafOnizleCommand.ExecuteAsync(null);
        await vm.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Single(kontrol.Masraflar);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Basarili_durum_degisiminden_sonra_kart_bilgisi_formu_kapanir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Gerekce = "Kart kapatıldı";
        await vm.DurumDegistirAsync();
        Assert.Contains("kullanım durumu", vm.Mesaj);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Basarili_gecis_onayindan_sonra_form_kapanir()
    {
        var api = new FinansTakipTests.Sahte { Kart = FinansTakipTests.Sahte.OrnekKart() with { YeniTakip = false } };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Gecis);
        vm.GecisAciklama = "Eski borç kontrol edildi";
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.NotNull(api.KartGecis);
        Assert.Equal(1, vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Basarili_devir_duzeltmesinden_sonra_kart_bilgisi_formu_kapanir()
    {
        var tarih = new DateOnly(2026, 9, 25);
        var api = new FinansTakipTests.Sahte
        {
            Devir = new(11, tarih, 100m, 80m, 0m, [new TakipKanalPayi(1, "MEZAT", 100m)], "IslemTarihi", 80m, 0m, 0m, 80m, 80m, true, null),
        };
        api.Kart = api.Kart with { Gecis = new KartGecisDto("IslemTarihi", "Banka", null) };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        await vm.DevirYukleCommand.ExecuteAsync(null);
        vm.DevirAciklama = "Banka ekstresine göre";
        await vm.DevirDuzeltCommand.ExecuteAsync(null);
        Assert.Single(api.DevirDuzeltmeleri);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    // --- Benzer kayıt uyarısı ---

    [Fact]
    public async Task Benzer_odeme_uyarisinda_form_acik_kalir_form_degisince_uyari_temizlenir()
    {
        var (vm, api) = await Vm(benzerlik: new BenzerKayitTests.Sahte());
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.True(vm.OdemeBenzerlik.UyariVar);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        Assert.False(vm.OdemeBenzerlik.UyariVar);
    }

    [Fact]
    public async Task Benzer_harcama_uyarisinda_form_acik_kalir_vazgecince_uyari_temizlenir()
    {
        var (vm, api) = await Vm(benzerlik: new BenzerKayitTests.Sahte());
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        vm.HarcamaTutari = 10;
        vm.HarcamaAciklama = "Mal";
        await vm.HarcamaKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Harcama);
        Assert.True(vm.HarcamaBenzerlik.UyariVar);
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        vm.VazgecCommand.Execute(null);
        Assert.False(vm.HarcamaBenzerlik.UyariVar);
    }

    // --- Oturum, bildirim, güvenli giriş ---

    [Fact]
    public async Task Yeni_oturum_acik_formu_kapatir_ve_sekmeyi_sifirlar()
    {
        var auth = TestOturumu.Ac(Rol.Editor);
        var (vm, _) = await Vm(auth: auth);
        vm.SekmeSecCommand.Execute(vm.Sekmeler[2]);
        vm.YeniKartAcCommand.Execute(null);   // kart seçili değilken açık form: Secili değişmeden kapanmalı
        Assert.True(vm.YeniKartFormuAcik);
        TestOturumu.YeniOturum(auth, Rol.Editor);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Equal(KartSekmesi.Ekstreler, vm.SeciliSekme);
        Assert.Equal(new[] { true, false, false }, vm.Sekmeler.Select(s => s.Secili));
    }

    [Fact]
    public async Task Arayuz_durumu_degisiklikleri_bildirilir()
    {
        var (vm, _) = await Vm();
        var bildirilen = new List<string?>();
        vm.PropertyChanged += (_, e) => bildirilen.Add(e.PropertyName);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Contains(nameof(vm.AcikKartId), bildirilen);

        bildirilen.Clear();
        vm.YeniKartAcCommand.Execute(null);
        Assert.Contains(nameof(vm.YeniKartFormuAcik), bildirilen);
        Assert.Contains(nameof(vm.AcikKartId), bildirilen);

        bildirilen.Clear();
        await vm.KaydetCommand.ExecuteAsync(null);   // boş ad: formun hatası
        Assert.NotNull(vm.FormHatasi);
        Assert.Contains(nameof(vm.FormHatasi), bildirilen);
        Assert.Contains(nameof(vm.SayfaHatasi), bildirilen);

        bildirilen.Clear();
        vm.VazgecCommand.Execute(null);
        Assert.Null(vm.FormHatasi);
        Assert.Contains(nameof(vm.FormHatasi), bildirilen);
    }

    [Fact]
    public async Task Bos_kutu_secimi_hicbir_sey_yapmaz()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(null);
        Assert.Null(vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }
}
