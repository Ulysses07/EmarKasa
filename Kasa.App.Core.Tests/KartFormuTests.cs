using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kartlar: yeni kart ve "Kartı düzenle" formu (tasarım 2026-10-02 §1-2; KR-04): başlık, alan hataları ve kaydedilmemiş
/// değişiklikte başka karta geçiş / yeni kart onayı.</summary>
public class KartFormuTests
{
    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var ikinci = FinansTakipTests.Sahte.OrnekKart() with { Id = 2, Ad = "World" };
        api.KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([api.Kart, ikinci]);
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KartTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Yeni_kart_formu_bos_kayitta_alanlari_ayri_ayri_soyler()
    {
        var (vm, api) = await Kur();
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(("Yeni kart", "Kartı kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
        vm.KesimGunu = 0;
        vm.SonOdemeGunu = 40;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        Assert.Equal("Kesim günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.KesimGunu)]);
        Assert.Equal("Son ödeme günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.SonOdemeGunu)]);
        Assert.Equal(nameof(vm.Ad), vm.KartHatalari.IlkAlan);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);

        vm.Ad = "Bonus";
        Assert.Null(vm.KartHatalari[nameof(vm.Ad)]);
        vm.VazgecCommand.Execute(null);
        Assert.False(vm.KartHatalari.Var);
    }

    [Fact]
    public async Task Duzenleme_basligi_kartin_adini_soyler()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(("Düzenleniyor: Kart", "Değişikliği kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
    }

    /// <summary>AppShell'in otomatik/elle yenileme koruması (K-1) <see cref="KartTakipViewModel.KaydedilmemisDegisiklikVar"/>'ı
    /// okuyarak "Kartı düzenle" formu açıkken arka plan yenilemesini (YukleAsync → Sec(mevcut)) erteler; bu yenileme olmasa da
    /// forma yazılan "Ad" alanı sunucu değeriyle ezilirdi (ürün sahibinin işaret ettiği kusur). Bayrağın doğru çalıştığını, yani
    /// korumanın devreye gireceğini burada sınıyoruz: form açılınca kapalı, alan değişince açık, sunucu değeriyle aynıya dönünce
    /// yine kapalı.</summary>
    [Fact]
    public async Task Kart_bilgisi_formu_acikken_ad_degisince_kaydedilmemis_degisiklik_var_olur()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Ad = "Değişti";
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        vm.Ad = "Kart";   // sunucudaki (açılıştaki) değere geri döndü: değişiklik yok sayılır
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Yazilmis_form_baska_karta_ve_yeni_karta_gecmeden_once_onay_ister_ayni_kartin_formlari_arasinda_sormaz()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 250m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((1, KartFormu.Odeme, 250m), (vm.AcikKartId, vm.AcikForm, vm.OdemeTutari));
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.AcikKartId);

        vm.FormAcCommand.Execute(KartFormu.Harcama);   // aynı kart: sorulmaz
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        Assert.Equal(2, sorulan);

        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Yeni ad";
        cevap = true;
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((2, KartFormu.Yok), (vm.AcikKartId, vm.AcikForm));
        Assert.Equal("World", vm.Ad);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(3, sorulan);
    }

    /// <summary>Görev 14-19 incelemesi: "Vazgeç" yalnız açık formun kirliliğini bırakır; aynı kartta önceki formdan taşınan
    /// kirlilik (<c>_oncekiFormKirli</c>) Vazgeç'le düşmez, kart değişimine kadar kalır.</summary>
    [Fact]
    public async Task Vazgec_tasinan_kirliligi_dusurmez_baska_karta_gecis_onay_ister()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 250m;   // ödeme formu yazılır

        vm.FormAcCommand.Execute(KartFormu.Harcama);   // aynı kart: ödeme formunun kirliliği taşınır
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        vm.VazgecCommand.Execute(null);   // yalnız açık (Harcama) formun kirliliğini bırakır, onay sormaz
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.True(vm.KaydedilmemisDegisiklikVar);   // taşınan kirlilik hâlâ var

        var sorulan = 0;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(true); };   // "Bırak"
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);   // başka karta geçiş: onay sorulur
        Assert.Equal(1, sorulan);
        Assert.Equal(2, vm.AcikKartId);
    }

    [Fact]
    public async Task Degisiklikleri_birakmak_formu_kapatir_ve_kartin_kayitli_degerlerine_doner()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Değişti";
        vm.Limit = 5m;

        vm.DegisiklikleriBirak();

        Assert.Equal((KartFormu.Yok, "Kart", 1000m), (vm.AcikForm, vm.Ad, vm.Limit));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    /// <summary>Liste yenilemesi (YukleAsync → aynı kartın yeniden seçilmesi) açık kart bilgileri formunun alanlarını ezmez.</summary>
    [Fact]
    public async Task Kart_bilgisi_formu_acikken_yenileme_alanlari_ezmez()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Yazılan";
        vm.Limit = 5m;
        vm.KesimGunu = 3;
        vm.SonOdemeGunu = 20;

        await vm.YukleAsync();

        Assert.Equal((KartFormu.KartBilgisi, "Yazılan", 5m, 3, 20), (vm.AcikForm, vm.Ad, vm.Limit, vm.KesimGunu, vm.SonOdemeGunu));
        Assert.True(vm.KaydedilmemisDegisiklikVar);
    }

    /// <summary>Ödeme formunda seçilen ekstre yenilemeden sonra da seçili kalır (yenilenen listedeki aynı ekstre); harcama payları korunur.</summary>
    [Fact]
    public async Task Odeme_formunda_ekstre_secimi_ve_harcama_paylari_yenilemede_korunur()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.PayEkle(vm.HarcamaPaylari);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeEkstresi = vm.Ekstreler[0];
        vm.OdemeTutari = 50m;

        await vm.YukleAsync();

        Assert.Equal(7, vm.OdemeEkstresi?.Veri.Id);
        Assert.Contains(vm.OdemeEkstresi, vm.Ekstreler);
        Assert.Single(vm.HarcamaPaylari);
        Assert.Equal((KartFormu.Odeme, 50m), (vm.AcikForm, vm.OdemeTutari));
    }

    /// <summary>"Yeni kart ekle" kutusu yazılmış yeni kart formu açıkken tıklanıp "Bırak" denirse form kapanır (boş açılmaz).</summary>
    [Fact]
    public async Task Yeni_kart_kutusu_birakildiginda_formu_kapatir()
    {
        var (vm, _) = await Kur();
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        vm.Ad = "Yazılan";
        vm.BirakmaOnayi = _ => Task.FromResult(true);

        await vm.YeniKartAcCommand.ExecuteAsync(null);

        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.YeniKartFormuAcik);
        Assert.Equal("", vm.Ad);
    }

    /// <summary>Aynı kartın formları arasında geçiş sorulmaz ama önceki formda yazılanlar kirli sayılmaya devam eder: başka karta
    /// geçişte onay sorulur.</summary>
    [Fact]
    public async Task Ayni_kartta_form_gecisi_kirliligi_tasir_baska_kartta_onay_sorulur()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 250m;
        var sorulan = 0;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(false); };

        vm.FormAcCommand.Execute(KartFormu.Harcama);
        Assert.Equal(0, sorulan);
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal(1, sorulan);
        Assert.Equal((1, 250m), (vm.AcikKartId, vm.OdemeTutari));
    }

    /// <summary>Sayfanın "Yenile / tekrar dene" düğmesi kirli formda önce onay sorar: "Forma dön" yenilemez, "Bırak" bırakır.</summary>
    [Fact]
    public async Task Yenile_dugmesi_kirli_formda_onay_sorar()
    {
        var (vm, _) = await Kur();
        Assert.True(await vm.YenilemedenOnceBirakilabilirAsync());
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Yazılan";
        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);

        Assert.False(await vm.YenilemedenOnceBirakilabilirAsync());
        Assert.Equal("Yazılan", vm.Ad);

        cevap = true;
        Assert.True(await vm.YenilemedenOnceBirakilabilirAsync());
        Assert.Equal((KartFormu.Yok, "Kart"), (vm.AcikForm, vm.Ad));
    }
}
