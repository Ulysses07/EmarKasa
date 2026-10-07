namespace Kasa.App.Core.Tests;

public class UygulamaGuncellemeTests
{
    private static readonly UygulamaGuncelleme Yeni = new("paket-2.5.0", "2.5.0");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Guncelleyici : IUygulamaGuncelleyici
    {
        public bool UygulamaIciKurulum { get; set; } = true;
        public string KanalAciklamasi => "TestFlight kanalında kontrol edin.";
        public string HariciKanalMetni => "Yayın kanalını aç";
        public int KontrolSayisi, IndirmeSayisi, KurulumSayisi, HariciSayisi;
        public Func<CancellationToken, Task<UygulamaGuncelleme?>> Kontrol { get; set; } = _ => Task.FromResult<UygulamaGuncelleme?>(Yeni);
        public Func<IProgress<int>, CancellationToken, Task> Indir { get; set; } = (p, _) => { p.Report(100); return Task.CompletedTask; };
        public Func<CancellationToken, Task> Kur { get; set; } = _ => Task.CompletedTask;
        public Task<UygulamaGuncelleme?> KontrolEtAsync(CancellationToken ct) { KontrolSayisi++; return Kontrol(ct); }
        public Task IndirAsync(UygulamaGuncelleme g, IProgress<int> p, CancellationToken ct) { IndirmeSayisi++; return Indir(p, ct); }
        public Task KurVeYenidenBaslatAsync(UygulamaGuncelleme g, CancellationToken ct) { KurulumSayisi++; return Kur(ct); }
        public Task HariciKanaliAcAsync(CancellationToken ct) { HariciSayisi++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Kontrol_yalniz_surumu_getirir_indirme_ve_kurulum_baslatmaz()
    {
        var servis = new Guncelleyici();
        var vm = new UygulamaGuncellemeViewModel(servis);
        await vm.KontrolEtAsync(Token);
        Assert.Equal(Yeni, vm.Guncelleme);
        Assert.Equal(1, servis.KontrolSayisi);
        Assert.Equal(0, servis.IndirmeSayisi);
        Assert.Equal(0, servis.KurulumSayisi);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Indirme_hatasi_paketi_korur_yeniden_deneme_hazir_paket_yapar()
    {
        var servis = new Guncelleyici { Indir = (_, _) => throw new HttpRequestException() };
        var vm = new UygulamaGuncellemeViewModel(servis);
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        Assert.Equal(Yeni, vm.Guncelleme);
        Assert.NotNull(vm.Hata);
        servis.Indir = (p, _) => { p.Report(100); return Task.CompletedTask; };
        await vm.IndirAsync(Token);
        Assert.True(vm.Guncelleme!.Indirildi);
        Assert.Equal(100, vm.Ilerleme);
        Assert.Null(vm.Hata);
        Assert.Equal(2, servis.IndirmeSayisi);
    }

    [Fact]
    public async Task Kurulum_acik_onay_ister_ertelemede_paket_kalir_hatada_yeniden_denenir()
    {
        var servis = new Guncelleyici { Kur = _ => throw new IOException() };
        var vm = new UygulamaGuncellemeViewModel(servis);
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        await vm.KurAsync(Token);
        Assert.Equal(0, servis.KurulumSayisi); // Onay delegesi bile yoksa uygulama kapanmaz.
        vm.KurulumOnayi = _ => Task.FromResult(false);
        await vm.KurAsync(Token);
        Assert.True(vm.Guncelleme!.Indirildi);
        Assert.Equal(0, servis.KurulumSayisi);
        vm.KurulumOnayi = _ => Task.FromResult(true);
        await vm.KurAsync(Token);
        Assert.NotNull(vm.Hata);
        Assert.True(vm.Guncelleme.Indirildi);
        servis.Kur = _ => Task.CompletedTask;
        await vm.KurAsync(Token);
        Assert.Equal(2, servis.KurulumSayisi);
    }

    [Fact]
    public async Task Mesgulken_diger_guncelleme_islemleri_yeniden_girmez()
    {
        var bekle = new TaskCompletionSource<UygulamaGuncelleme?>();
        var servis = new Guncelleyici { Kontrol = _ => bekle.Task };
        var vm = new UygulamaGuncellemeViewModel(servis);
        var ilk = vm.KontrolEtAsync(Token);
        Assert.True(vm.Mesgul);
        await vm.KontrolEtAsync(Token);
        await vm.HariciKanaliAcAsync(Token);
        await vm.IndirAsync(Token);
        Assert.Equal(1, servis.KontrolSayisi);
        Assert.Equal(0, servis.HariciSayisi);
        bekle.SetResult(Yeni);
        await ilk;
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Iptalden_sonra_gec_gelen_sonuc_hata_ve_ilerleme_yansimaz()
    {
        var bekle = new TaskCompletionSource<UygulamaGuncelleme?>();
        var servis = new Guncelleyici { Kontrol = _ => bekle.Task };
        var vm = new UygulamaGuncellemeViewModel(servis);
        var kontrol = vm.KontrolEtAsync(Token);
        vm.IptalEt();
        bekle.SetResult(Yeni);
        await kontrol;
        Assert.Null(vm.Guncelleme);
        Assert.Null(vm.Hata);
        servis.Kontrol = _ => Task.FromResult<UygulamaGuncelleme?>(Yeni);
        await vm.KontrolEtAsync(Token);
        var indirme = new TaskCompletionSource();
        IProgress<int>? ilerleme = null;
        servis.Indir = (p, _) => { ilerleme = p; return indirme.Task; };
        var islem = vm.IndirAsync(Token);
        ilerleme!.Report(50);
        Assert.Equal(50, vm.Ilerleme);
        vm.IptalEt();
        ilerleme.Report(99);
        indirme.SetException(new IOException("Geç gelen eski hata"));
        await islem;
        Assert.Equal(Yeni, vm.Guncelleme);
        Assert.NotEqual(99, vm.Ilerleme);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Onay_beklerken_iptal_edilirse_kurulum_baslamaz()
    {
        var servis = new Guncelleyici();
        var onay = new TaskCompletionSource<bool>();
        var vm = new UygulamaGuncellemeViewModel(servis) { KurulumOnayi = _ => onay.Task };
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        var kurulum = vm.KurAsync(Token);
        vm.IptalEt();
        onay.SetResult(true);
        await kurulum;
        Assert.Equal(0, servis.KurulumSayisi);
        Assert.True(vm.Guncelleme!.Indirildi);
    }

    [Fact]
    public async Task TestFlight_ve_ZIP_surumunde_sahte_surum_kontrolu_yapilmaz_kanal_acilir()
    {
        var servis = new Guncelleyici { UygulamaIciKurulum = false };
        var vm = new UygulamaGuncellemeViewModel(servis);
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        await vm.KurAsync(Token);
        Assert.Equal(0, servis.KontrolSayisi);
        Assert.Equal(0, servis.IndirmeSayisi);
        Assert.Equal(0, servis.KurulumSayisi);
        Assert.Contains("TestFlight", vm.Durum);
        await vm.HariciKanaliAcAsync(Token);
        Assert.Equal(1, servis.HariciSayisi);
    }
    private sealed class Form : TemelViewModel, IKaydedilmemisForm
    {
        public bool KaydedilmemisDegisiklikVar { get; set; }
        public bool YenilemeFormuKorur => false;
        public int BirakmaSayisi;
        public void DegisiklikleriBirak() { BirakmaSayisi++; KaydedilmemisDegisiklikVar = false; }
    }
    [Fact]
    public async Task Kurulum_guvenli_ekran_ve_bos_form_ister_taslaklari_kendi_birakmaz()
    {
        var servis = new Guncelleyici();
        var form = new Form { KaydedilmemisDegisiklikVar = true };
        var guvenliEkran = false;
        var sorulan = 0;
        var vm = new UygulamaGuncellemeViewModel(servis)
        {
            KurulumEngeli = () => UygulamaKurulumKarari.Engel(guvenliEkran, [form]),
            KurulumOnayi = _ => { sorulan++; return Task.FromResult(true); }
        };
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        await vm.KurAsync(Token);
        Assert.Contains("Haftalık rapor", vm.Hata);
        guvenliEkran = true;
        await vm.KurAsync(Token);
        Assert.Contains("kaydedilmemiş", vm.Hata);
        Assert.Equal(0, form.BirakmaSayisi);
        form.KaydedilmemisDegisiklikVar = false;
        form.Mesgul = true;
        await vm.KurAsync(Token);
        Assert.Contains("Devam eden işlem", vm.Hata);
        Assert.Equal(0, sorulan);
        Assert.Equal(0, servis.KurulumSayisi);
        form.Mesgul = false;
        await vm.KurAsync(Token);
        Assert.Equal(1, servis.KurulumSayisi);
    }
    [Fact]
    public async Task Onay_acikken_sayfa_veya_form_durumu_degistiyse_kurulum_engellenir()
    {
        var servis = new Guncelleyici();
        var onay = new TaskCompletionSource<bool>();
        var guvenliEkran = true;
        var vm = new UygulamaGuncellemeViewModel(servis)
        {
            KurulumOnayi = _ => onay.Task,
            KurulumEngeli = () => UygulamaKurulumKarari.Engel(guvenliEkran, [])
        };
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        var kurulum = vm.KurAsync(Token);
        guvenliEkran = false;
        onay.SetResult(true);
        await kurulum;
        Assert.Equal(0, servis.KurulumSayisi);
        Assert.True(vm.Guncelleme!.Indirildi);
        Assert.Contains("Haftalık rapor", vm.Hata);
    }
    [Fact]
    public async Task Yeniden_kontrol_hatasi_indirilmis_paketi_silmez()
    {
        var servis = new Guncelleyici();
        var vm = new UygulamaGuncellemeViewModel(servis);
        await vm.KontrolEtAsync(Token);
        await vm.IndirAsync(Token);
        var hazir = vm.Guncelleme;
        servis.Kontrol = _ => throw new HttpRequestException();
        await vm.KontrolEtAsync(Token);
        Assert.Same(hazir, vm.Guncelleme);
        Assert.True(vm.Guncelleme!.Indirildi);
        Assert.NotNull(vm.Hata);
        Assert.False(vm.Mesgul);
    }
    [Fact]
    public void Native_alan_kontrol_indirme_ve_kapatma_eylemlerini_baglar()
    {
        GorunumOrtami.Kur();
        var servis = new Guncelleyici();
        var vm = new UygulamaGuncellemeViewModel(servis);
        var alan = new Kasa.App.Views.UygulamaGuncellemeAlani(vm);
        var dugmeler = alan.GetVisualTreeDescendants().OfType<Button>().ToList();
        var kontrol = dugmeler.Single(b => b.Text == "Güncellemeleri kontrol et");
        var indir = dugmeler.Single(b => b.Text == "Güncellemeyi indir");
        var kur = dugmeler.Single(b => b.Text == "Kur ve yeniden başlat");
        Assert.Same(vm, alan.BindingContext);
        Assert.False(indir.IsVisible);
        ((IButtonController)kontrol).SendClicked();
        Assert.Equal(Yeni, vm.Guncelleme);
        Assert.True(indir.IsVisible);
        Assert.True(indir.IsEnabled);
        Assert.False(kur.IsEnabled);
        ((IButtonController)indir).SendClicked();
        Assert.True(vm.Guncelleme!.Indirildi);
        Assert.True(kur.IsEnabled);
        Assert.False(indir.IsEnabled);
        Assert.Equal(0, servis.KurulumSayisi);
        var kapandi = 0;
        alan.KapatIstendi += (_, _) => kapandi++;
        ((IButtonController)dugmeler.Single(b => b.Text == "Kapat")).SendClicked();
        Assert.Equal(1, kapandi);
        Assert.Equal(0, servis.KurulumSayisi);
    }
}
