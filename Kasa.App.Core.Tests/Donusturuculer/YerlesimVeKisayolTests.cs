using Kasa.ApiClient;
using Kasa.App.Controls;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Yerleşim ve kısayollar (docs/specs/2026-11-26-slop-temizligi.md §8): formlar yalnız istenince açılır ve "Vazgeç" ile
/// temizlenip kapanır, Aylık giderler'de tek satırlık ay gezgini, Rapor'da biçim seçimi ve tek "Dışa aktar", F5 / Ctrl+N / Ctrl+S.</summary>
public class YerlesimVeKisayolTests
{
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0)] };

    private static async Task Bekle(Func<bool> kosul)
    {
        for (var i = 0; i < 100 && !kosul(); i++)
            await Task.Delay(10);
        Assert.True(kosul());
    }

    [Fact]
    public async Task Yeni_kredi_formu_yalniz_Kredi_ekle_ile_acilir_kayit_ve_Vazgec_kapatir()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = new KrediTakipViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        Assert.False(vm.YeniKredi);

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.True(vm.YeniKredi);
        vm.Ad = "Ziraat";
        vm.YeniKapatCommand.Execute(null);
        Assert.Equal((false, "", false), (vm.YeniKredi, vm.Ad, vm.KaydedilmemisDegisiklikVar));

        await vm.YeniCommand.ExecuteAsync(null);
        vm.Ad = "Banka";
        vm.CekilenTutar = 100;
        vm.AylikOdeme = 12;
        vm.TaksitSayisi = 10;
        vm.MevcutKredi = true;
        vm.Kanallar[0].Secili = true;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.KrediKayit);
        Assert.Equal((false, false), (vm.YeniFormAcik, vm.YeniKredi));
    }

    [Fact]
    public async Task Erken_kapama_formu_istenince_acilir_Vazgec_ve_kredi_degisimi_kapatir()
    {
        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), Finans(), Editor());
        await vm.YukleAsync();
        await vm.SecCommand.ExecuteAsync(vm.Krediler.First(k => k.Veri.YeniTakip));
        Assert.Equal((false, true), (vm.KapamaGorunur, vm.KapamaAcilabilir));

        vm.KapamaAcCommand.Execute(null);
        Assert.Equal((true, false), (vm.KapamaGorunur, vm.KapamaAcilabilir));
        vm.KapatmaTutari = 500;
        vm.KapatmaOnay = true;
        vm.KapamaKapatCommand.Execute(null);
        Assert.Equal((false, 0m, false), (vm.KapamaGorunur, vm.KapatmaTutari, vm.KapatmaOnay));

        vm.KapamaAcCommand.Execute(null);
        await vm.YeniCommand.ExecuteAsync(null);   // seçim kalkar
        Assert.False(vm.KapamaAcik);
    }

    [Fact]
    public async Task Sablon_formu_yalniz_istenince_acilir_kayit_ve_Vazgec_kapatir()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        Assert.False(vm.SablonFormuAcik);

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.True(vm.SablonFormuAcik);
        vm.Ad = "Kira";
        vm.SablonKapatCommand.Execute(null);
        Assert.Equal((false, "", false), (vm.SablonFormuAcik, vm.Ad, vm.KaydedilmemisDegisiklikVar));

        await vm.SablonSecAsync(new AylikSablonSatiri(new AylikGiderSablonDto(7, 1, "Kira", vm.Turler[0].Kod, 100, 1, vm.DagilimTurleri[0].Kod, [],
            new DateOnly(2026, 11, 1), true)));
        Assert.True(vm.SablonFormuAcik);
        vm.SablonKapatCommand.Execute(null);

        await vm.YeniCommand.ExecuteAsync(null);
        vm.Ad = "Kira";
        vm.Tur = vm.Turler[0];
        vm.DagilimTuru = vm.DagilimTurleri[0];
        vm.Tutar = 100;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.Sablon);
        Assert.False(vm.SablonFormuAcik);
    }

    [Fact]
    public void Ay_basligi_Turkce_ay_adi_ve_yildir()
    {
        var vm = new AylikGiderViewModel(new KasaKontrolVeAylikGiderTests.Sahte(), Finans(), Editor()) { AyTarihi = new DateTime(2026, 11, 1) };
        Assert.Equal("Kasım 2026", vm.AyBasligi);
        var degisen = new List<string?>();
        vm.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        vm.AyTarihi = new DateTime(2026, 12, 1);
        Assert.Equal("Aralık 2026", vm.AyBasligi);
        Assert.Contains(nameof(vm.AyBasligi), degisen);
    }

    [Fact]
    public async Task Aylik_giderler_ay_gezgini_oklu_tek_satirdir()
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur("Aylık giderler");
        var vm = (AylikGiderViewModel)sayfa.BindingContext;
        var dugmeler = sayfa.GetVisualTreeDescendants().OfType<Button>().Select(b => b.Text).ToList();
        Assert.Contains("‹", dugmeler);
        Assert.Contains("›", dugmeler);
        Assert.DoesNotContain("Seçilen ayı göster", dugmeler);
        Assert.DoesNotContain("Önceki ay", dugmeler);
        Assert.DoesNotContain(sayfa.GetVisualTreeDescendants().OfType<DatePicker>(), DugmeVeRenkKurallariTests.Gorunur);
        Assert.Contains(sayfa.GetVisualTreeDescendants().OfType<Label>(), l => l.Text == vm.AyBasligi);
    }

    [Fact]
    public async Task Rapor_bicim_secimi_Tum_kanallar_secenegi_ve_tek_Disa_aktar_dugmesi_sunar()
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur("Rapor");
        var vm = (DisariAktarViewModel)sayfa.BindingContext;
        var dugmeler = sayfa.GetVisualTreeDescendants().OfType<Button>().Where(DugmeVeRenkKurallariTests.Gorunur).Select(b => b.Text).ToList();
        Assert.Single(dugmeler, d => d == "Dışa aktar");
        Assert.DoesNotContain(dugmeler, d => d.Contains("Excel", StringComparison.Ordinal) || d.Contains("CSV", StringComparison.Ordinal) || d == "Tüm kanallar");

        var secicler = sayfa.GetVisualTreeDescendants().OfType<Picker>().ToList();
        var bicim = secicler.Single(p => p.ItemDisplayBinding is null);
        Assert.Equal(["Excel", "CSV", "PDF (yazdırılabilir)"], bicim.ItemsSource.Cast<string>());
        Assert.Equal(0, bicim.SelectedIndex);

        var kanal = secicler.Single(p => p.ItemDisplayBinding is not null);
        var adlar = kanal.ItemsSource.Cast<object>().Select(o => (string)o.GetType().GetProperty("Ad")!.GetValue(o)!).ToList();
        Assert.Equal(["Tüm kanallar", .. vm.Kanallar.Select(k => k.Ad)], adlar);
        kanal.SelectedIndex = 1;
        Assert.Same(vm.Kanallar[0], vm.Kanal);
        kanal.SelectedIndex = 0;
        Assert.Null(vm.Kanal);

        // Sayfa her açılışta kanalları yeniden yükler: seçili kanal korunur.
        kanal.SelectedIndex = 2;
        await vm.YukleAsync();
        Assert.Equal((2, vm.Kanallar[1]), (kanal.SelectedIndex, vm.Kanal));
    }

    [Fact]
    public async Task Rapor_yenilemede_sunucuda_kalmayan_kanal_secimden_duser()
    {
        var finans = Finans();
        var vm = new DisariAktarViewModel(new YeniAkisTests.Sahte(), finans, Editor());
        await vm.YukleAsync();
        vm.Kanal = vm.Kanallar[1];
        finans.KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)];
        await vm.YukleAsync();
        Assert.Null(vm.Kanal);
    }

    /// <summary>Gizli formun sarmalayıcısı da gizlidir: içi gizli ama kendisi görünür sarmalayıcı dikey yığında boşluk bırakır
    /// (kartlar arasında fazladan 20 px; ekran denemesi 2026-11-27).</summary>
    [Theory]
    [MemberData(nameof(DugmeVeRenkKurallariTests.TakipSayfalari), MemberType = typeof(DugmeVeRenkKurallariTests))]
    public async Task Ici_gizli_sarmalayici_gorunur_kalmaz(string ad, int _)
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur(ad);
        var bos = sayfa.GetVisualTreeDescendants().OfType<ContentView>()
            .Where(c => DugmeVeRenkKurallariTests.Gorunur(c) && c.Content is VisualElement { IsVisible: false }).ToList();
        Assert.True(bos.Count == 0, $"{ad}: {bos.Count} boş sarmalayıcı görünür");
    }

    /// <summary>Ctrl+S yalnız ilgili form açıkken vardır (tuş yutulmaz); Ctrl+N sayfanın "yeni" düğmesiyle aynı formu açar.</summary>
    [Theory]
    [InlineData("Krediler")]
    [InlineData("Çekler")]
    [InlineData("Aylık giderler")]
    public async Task Ctrl_S_yalniz_form_acikken_vardir_Ctrl_N_formu_acar(string ad)
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur(ad);
        var hedef = Assert.IsAssignableFrom<IKisayolHedefi>(sayfa);
        bool FormAcik() => sayfa.BindingContext switch
        {
            KrediTakipViewModel k => k.YeniKredi,
            CekTakipViewModel c => c.FormAcik,
            AylikGiderViewModel a => a.SablonFormuAcik,
            _ => throw new InvalidOperationException(),
        };
        Assert.False(FormAcik());
        Assert.Null(hedef.KisayolKomutu(Kisayol.Kaydet));
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Kaydet));

        Assert.True(Kisayollar.Uygula(sayfa, Kisayol.Yeni));
        await Bekle(FormAcik);
        Assert.NotNull(hedef.KisayolKomutu(Kisayol.Kaydet));
        Assert.Null(hedef.KisayolKomutu(Kisayol.Ara));
    }

    /// <summary>Sayfa meşgulken (kayıt ya da yükleme sürerken gövde kapalıdır) Ctrl+N / Ctrl+S / F5 çalışmaz: yoksa bekleyen yanıt
    /// döndüğünde yeni açılan formu ezerdi (Codex incelemesi, #76).</summary>
    [Fact]
    public async Task Sayfa_mesgulken_kisayol_calismaz()
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur("Krediler");
        var vm = (KrediTakipViewModel)sayfa.BindingContext;
        vm.Mesgul = true;
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Yeni));
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Yenile));
        await Task.Delay(50);
        Assert.False(vm.YeniKredi);

        vm.Mesgul = false;
        Assert.True(Kisayollar.Uygula(sayfa, Kisayol.Yeni));
        await Bekle(() => vm.YeniKredi);
    }

    [Fact]
    public async Task Sayfa_ustu_pencere_acikken_kisayol_alttaki_sayfaya_gitmez()
    {
        GorunumOrtami.Kur();
        var sayfa = (ContentPage)await DugmeVeRenkKurallariTests.SayfaKur("Aylık giderler");
        var vm = (AylikGiderViewModel)sayfa.BindingContext;
        var pencere = new SayfaUstuPencere(sayfa, new Label());
        Assert.True(SayfaUstuPencere.Acik(sayfa));
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Yeni));
        Assert.False(vm.SablonFormuAcik);

        pencere.Dispose();
        Assert.False(SayfaUstuPencere.Acik(sayfa));
        Assert.True(Kisayollar.Uygula(sayfa, Kisayol.Yeni));
        await Bekle(() => vm.SablonFormuAcik);
    }

    [Fact]
    public void F5_baslik_satirinin_Yenilesidir_is_surerken_ya_da_Yenile_yokken_calismaz()
    {
        GorunumOrtami.Kur();
        var sayim = 0;
        var baslik = new SayfaBasligi { Baslik = "Deneme", YenileCommand = new Command(() => sayim++) };
        var sayfa = new ContentPage { Content = new VerticalStackLayout { Children = { baslik } } };
        Assert.Equal("Yenile (F5)", ToolTipProperties.GetText(baslik.YenileDugmesi));

        Assert.True(Kisayollar.Uygula(sayfa, Kisayol.Yenile));
        Assert.Equal(1, sayim);
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Kaydet));

        baslik.Mesgul = true;
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Yenile));
        baslik.Mesgul = false;
        baslik.YenileGorunur = false;
        Assert.False(Kisayollar.Uygula(sayfa, Kisayol.Yenile));
        Assert.Equal(1, sayim);
    }
}
