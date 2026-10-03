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
        // Bağlantı geldiğinde otomatik yenileme alt sınırlıdır (ürün sahibi kararı 2026-10-03): şerit gidip gelirken yenileme
        // fırtınası olmaz; "Yeniden dene" düğmesi bu sınıra bağlı değildir.
        Assert.Contains("baglanti.BaglantiGeldi += async (_, _) => await AcikSayfayiYenileAsync(otomatik: true);", kod);
        Assert.Contains("_baglantiSeridi.YenidenDeneIstendi += async (_, _) => await AcikSayfayiYenileAsync();", kod);
        Assert.Matches(@"private async Task AcikSayfayiYenileAsync\(bool otomatik = false\)\s*\{\s*if \(_yenileniyor \|\| CurrentPage is not IYenilenebilir sayfa\)\s*return;", kod);
        Assert.Contains("DateTimeOffset.UtcNow - once < OtomatikYenilemeAraligi", kod);
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
