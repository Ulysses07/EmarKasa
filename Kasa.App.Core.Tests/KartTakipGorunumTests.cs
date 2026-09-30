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
        AuthViewModel? auth = null)
    {
        api ??= new FinansTakipTests.Sahte();
        var vm = new KartTakipViewModel(api, Finans(), auth ?? TestOturumu.Ac(rol));
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
        var (vm, api) = await Vm();
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
        Assert.Equal(api.Kart.Id, vm.AcikKartId);
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
}
