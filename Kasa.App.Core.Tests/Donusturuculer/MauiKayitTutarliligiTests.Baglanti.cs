using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

/// <summary>Kabuğun bağlantı şeridi ve sayfadan çıkış onayı (tasarım 2026-10-02 §2–3). AppShell Windows'a bağlıdır ve test projesinde
/// derlenmez; kaynak düzeyinde denetlenir.</summary>
public partial class MauiKayitTutarliligiTests
{
    [Fact]
    public void Kabuk_baglanti_seridini_tek_yerde_kurar_ve_acik_sayfayi_yeniler()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Contains("BaglantiDurumu baglanti)", kod);
        Assert.Contains("SetTitleView(this, _baglantiSeridi);", kod);
        Assert.Contains("baglanti.BaglantiGeldi += async (_, _) => await OtomatikYenileAsync();", kod);
        Assert.Contains("_baglantiSeridi.YenidenDeneIstendi += async (_, _) => await ElleYenileAsync();", kod);
        Assert.Matches(@"private async Task AcikSayfayiYenileAsync\(\)\s*\{\s*if \(_yenileniyor \|\| CurrentPage is not IYenilenebilir sayfa\)\s*return;", kod);
    }

    /// <summary>Ö-1: otomatik yenileme (bağlantı geldiğinde) kaydedilmemiş formu ezmez; K-1: alt sınır OtomatikYenilemeKarari'nda
    /// (TimeProvider, sondaki kenarda tek tetik) test edilir, burada yalnız kabuğun onu doğru sürdüğü denetlenir.</summary>
    [Fact]
    public void Otomatik_yenileme_kaydedilmemis_formu_ezmez_elle_yenileme_birakma_onayi_sorar()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Contains("new OtomatikYenilemeKarari(TimeProvider.System, TimeSpan.FromSeconds(3))", kod);
        Assert.Matches(@"private async Task OtomatikYenileAsync\(\)\s*\{[\s\S]*?var kirli = CurrentPage\?\.BindingContext is IKaydedilmemisForm \{ KaydedilmemisDegisiklikVar: true \};[\s\S]*?if \(_otomatikYenileme\.Sor\(kirli, out var bekle\)\)[\s\S]*?await AcikSayfayiYenileAsync\(\);[\s\S]*?else if \(bekle > TimeSpan\.Zero\)[\s\S]*?Dispatcher\.DispatchDelayed\(bekle, \(\) => _ = OtomatikYenileAsync\(\)\);", kod);
        Assert.Matches(@"if \(_otomatikYenileme\.Sor\(kirli, out var bekle\)\)\s*\{\s*//.*\s*var kopma = _baglanti\.KopmaSayisi;\s*await AcikSayfayiYenileAsync\(\);\s*_otomatikYenileme\.YenilemeBitti\(kopusla: _baglanti\.KopmaSayisi != kopma\);", kod);
        Assert.Matches(@"private async Task ElleYenileAsync\(\)\s*\{\s*//.*\s*if \(CurrentPage is not IYenilenebilir\)\s*\{\s*await _baglanti\.HemenYoklaAsync\(\);\s*return;\s*\}\s*if \(CurrentPage\?\.BindingContext is IKaydedilmemisForm \{ KaydedilmemisDegisiklikVar: true \} form\)\s*\{\s*if \(!await DisplayAlertAsync\(KaydedilmemisDegisiklik\.Baslik, KaydedilmemisDegisiklik\.Ileti, KaydedilmemisDegisiklik\.Birak, KaydedilmemisDegisiklik\.FormaDon\)\)\s*return;\s*form\.DegisiklikleriBirak\(\);", kod);
    }

    /// <summary>Kaydedilmemiş değişiklikte sayfadan çıkış: MAUI Shell gezinme ertelemesi (GetDeferral → Cancel/Complete). Erteleme
    /// her yolda tamamlanır (yoksa sonraki GoToAsync InvalidOperationException verir); girişe dönüş sorulmaz.</summary>
    [Fact]
    public void Kabuk_sayfadan_cikista_kaydedilmemis_degisiklik_onayini_ertelemeyle_sorar()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Matches(@"protected override async void OnNavigating\(ShellNavigatingEventArgs args\)", kod);
        Assert.Contains("CurrentPage?.BindingContext is not IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true } form", kod);
        Assert.Contains("Contains(\"login\", StringComparison.Ordinal)", kod);
        Assert.Matches(@"var erteleme = args\.GetDeferral\(\);[\s\S]*?args\.Cancel\(\);[\s\S]*?finally\s*\{[\s\S]*?erteleme\.Complete\(\);", kod);
    }

    /// <summary>Ö-3: onay penceresi açıkken (erteleme beklerken) oturum düşerse GiriseDonAsync beklemeye alınır (GoToAsync
    /// erteleme beklerken InvalidOperationException verir); pencere kapanınca (erteleme tamamlanınca) OnNavigating'in finally'si
    /// bunu tekrar dener.</summary>
    [Fact]
    public void Onay_penceresi_acikken_oturum_duserse_girise_donus_pencere_kapaninca_denenir()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Matches(@"private async Task GiriseDonAsync\(\)\s*\{\s*if \(_giriseDonuluyor\)\s*return;\s*if \(_birakmaSoruluyor\)\s*\{\s*_girisDonusuBekliyor = true;\s*return;\s*\}", kod);
        Assert.Matches(@"finally\s*\{\s*_birakmaSoruluyor = false;\s*erteleme\.Complete\(\);[\s\S]*?if \(_girisDonusuBekliyor\)\s*\{\s*_girisDonusuBekliyor = false;\s*_ = GiriseDonAsync\(\);\s*\}", kod);
    }

    /// <summary>"Yeniden dene" her menü sayfasında çalışır: sayfa ya TakipSayfasi'ndan türer ya da IYenilenebilir'dir.</summary>
    [Fact]
    public void Kabuktaki_her_menu_sayfasi_yenilenebilir()
    {
        var sayfalar = KabukSayfasi().Matches(Oku("AppShell.xaml")).Select(m => m.Groups[1].Value).Distinct().Where(s => s != "LoginPage").ToList();
        Assert.True(sayfalar.Count >= 12);
        var eksik = sayfalar.Where(s =>
        {
            var dosya = File.Exists(Path.Combine(Uygulama, "Views", s + ".xaml.cs")) ? s + ".xaml.cs" : s + ".cs";
            var kod = Oku(Path.Combine("Views", dosya));
            return !Regex.IsMatch(kod, $@"class {s} : (TakipSayfasi<\w+>|ContentPage, Controls\.IYenilenebilir)");
        }).ToList();
        Assert.True(eksik.Count == 0, "Yenilenemeyen sayfalar: " + string.Join(", ", eksik));
    }
}
