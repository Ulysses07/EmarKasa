using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

public partial class MauiKayitTutarliligiTests
{
    /// <summary>Kasa.App'in kendi kaynak dosyaları (derleme çıktısı hariç).</summary>
    private static IEnumerable<string> UygulamaKaynaklari(params string[] desenler)
    {
        var ayrac = Path.DirectorySeparatorChar;
        return desenler.SelectMany(desen => Directory.GetFiles(Uygulama, desen, SearchOption.AllDirectories))
            .Where(d => !d.Contains($"{ayrac}obj{ayrac}") && !d.Contains($"{ayrac}bin{ayrac}"));
    }

    /// <summary>Paketteki OpenSans'ın karakter tablosunda ₺ (U+20BA) yoktu: PlexSans* takma adları OpenSans'a bağlıyken tutarlar
    /// sistem yedek fontuyla, farklı görünümde çiziliyordu. Yazı ailesi tek kaynak sözlüğü anahtarından gelir: Windows 11'in
    /// Segoe UI Variable Text ailesi, onun olmadığı Windows 10'da Segoe UI (ikisinin de karakter tablosunda ₺ var). Sayfa ve
    /// stiller aileyi kendisi yazmaz; paketlenmiş ₺'siz font ve kaydı kalmaz.</summary>
    [Fact]
    public void Yazi_ailesi_tek_anahtardan_gelir_ve_lira_isaretsiz_font_kaydi_kalmaz()
    {
        var aile = YaziAilesiTanimi().Match(Oku("Resources/Styles/Styles.xaml"));
        Assert.True(aile.Success, "Styles.xaml'da <x:String x:Key=\"YaziAilesi\"> tanımı yok.");
        Assert.Equal(new[] { "Segoe UI Variable Text", "Segoe UI" }, aile.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));

        var dogrudan = UygulamaKaynaklari("*.xaml", "*.cs")
            .SelectMany(d => AileKullanimi().Matches(File.ReadAllText(d)).Select(m => (Dosya: Path.GetFileName(d), Deger: m.Groups[1].Value)))
            .Where(k => k.Deger != "{StaticResource YaziAilesi}").Select(k => $"{k.Dosya}: {k.Deger}").ToList();
        Assert.True(dogrudan.Count == 0, "YaziAilesi anahtarı yerine doğrudan yazılmış yazı ailesi: " + string.Join(", ", dogrudan));

        var program = Oku("MauiProgram.cs");
        Assert.DoesNotContain("AddFont(", program);
        Assert.DoesNotContain("OpenSans", program);
        Assert.DoesNotContain("MauiFont", Oku("Kasa.App.csproj"));
        Assert.Empty(UygulamaKaynaklari("*.ttf", "*.otf"));
    }

    [GeneratedRegex(@"<x:String x:Key=""YaziAilesi"">([^<]+)</x:String>")]
    private static partial Regex YaziAilesiTanimi();
    // XAML özniteliği (FontFamily="…"), stil ayarlayıcısı (Property="FontFamily" Value="…") ve C# ataması (FontFamily = "…").
    [GeneratedRegex(@"FontFamily""?\s*(?:Value\s*)?=\s*""([^""]*)""")]
    private static partial Regex AileKullanimi();
}
