using System.Text.RegularExpressions;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Arayüz metni (docs/specs/2026-11-26-slop-temizligi.md §2): kodla yazılmış sayfalarda açıklama paragrafı yok; düğme ve alan
/// etiketinde tek fiil ya da tek ad (eğik çizgi, uzun tire, parantezli niteleyici yok); ekranda kalan kısa ipucu en çok 80 karakter,
/// uzun kural açıklaması "Ayrıntılar"ın arkasında (aşamalı gösterim).</summary>
public partial class MetinKurallariTests
{
    private static IEnumerable<string> GorunumDosyalari()
        => Directory.GetFiles(Path.Combine(GorunumOrtami.DepoKoku(), "Kasa.App", "Views"), "*.cs").Where(f => !f.EndsWith(".xaml.cs", StringComparison.Ordinal));

    /// <summary>Etiket ya da düğme metni alan çağrıların ilk dizge bağımsız değişkeni.</summary>
    [GeneratedRegex(@"\b(?:Alan|Dugme|Tikla|Onay|Kart|FormDugmesi|AltBaslik|IkincilDugme|AltBolum)\(\s*""(?<metin>[^""]*)""")]
    private static partial Regex EtiketCagrisi();

    [GeneratedRegex(@"\bMetin\(\s*""(?<metin>[^""]*)""")]
    private static partial Regex MetinCagrisi();

    [Fact]
    public void Dugme_ve_alan_etiketinde_egik_cizgi_uzun_tire_ve_parantez_yok()
    {
        var ihlal = GorunumDosyalari().SelectMany(f => EtiketCagrisi().Matches(File.ReadAllText(f))
                .Select(m => m.Groups["metin"].Value)
                .Where(m => m.Contains(" / ", StringComparison.Ordinal) || m.Contains('—') || m.Contains('('))
                .Select(m => $"{Path.GetFileName(f)}: {m}"))
            .ToList();
        Assert.True(ihlal.Count == 0, string.Join("\n", ihlal));
    }

    [Fact]
    public void Ekranda_kalan_ipucu_kisa_uzun_aciklama_ayrintilarda()
    {
        var uzun = GorunumDosyalari().SelectMany(f => MetinCagrisi().Matches(File.ReadAllText(f))
                .Select(m => m.Groups["metin"].Value).Where(m => m.Length > 80).Select(m => $"{Path.GetFileName(f)}: {m}"))
            .ToList();
        Assert.True(uzun.Count == 0, string.Join("\n", uzun));
    }

    [GeneratedRegex(@"<(?:Button|Label|Span)\b[^>]*?\bText=""(?<metin>[^""{]*)""", RegexOptions.Singleline)]
    private static partial Regex XamlMetni();

    /// <summary>XAML sayfalarının sabit metinleri: eğik çizgi, uzun tire yok; BÜYÜK HARF üst etiket ve numaralı başlık ("1 · …") yok.</summary>
    [Fact]
    public void Xaml_sabit_metinlerinde_egik_cizgi_uzun_tire_ve_buyuk_harf_etiket_yok()
    {
        var dosyalar = Directory.GetFiles(Path.Combine(GorunumOrtami.DepoKoku(), "Kasa.App", "Views"), "*.xaml")
            .Append(Path.Combine(GorunumOrtami.DepoKoku(), "Kasa.App", "AppShell.xaml"));
        var ihlal = dosyalar.SelectMany(f => XamlMetni().Matches(File.ReadAllText(f)).Select(m => m.Groups["metin"].Value)
                .Where(m => m.Contains(" / ", StringComparison.Ordinal) || m.Contains('—') || Regex.IsMatch(m, @"^\d+ ·")
                    || (m.Count(char.IsLetter) >= 3 && !m.Any(char.IsLower)))
                .Select(m => $"{Path.GetFileName(f)}: {m}"))
            .ToList();
        Assert.True(ihlal.Count == 0, string.Join("\n", ihlal));
    }

    [Fact]
    public void Takip_sayfasi_aciklama_paragrafi_almaz()
    {
        var kurucu = typeof(TakipSayfasi<>).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        Assert.DoesNotContain(kurucu.GetParameters(), p => p.Name == "aciklama");
    }

    [Fact]
    public void Ayrintilar_baglantisi_aciklamayi_acar_ve_kapatir()
    {
        GorunumOrtami.Kur();
        var yardim = (Layout)TakipUi.Yardim("Uzun kural açıklaması.");
        var dugme = yardim.Children.OfType<Button>().Single();
        var aciklama = yardim.Children.OfType<Label>().Single();
        Assert.Equal(("Ayrıntılar", false), (dugme.Text, aciklama.IsVisible));
        ((IButtonController)dugme).SendClicked();
        Assert.Equal(("Ayrıntıları gizle", true), (dugme.Text, aciklama.IsVisible));
        ((IButtonController)dugme).SendClicked();
        Assert.False(aciklama.IsVisible);
        Assert.Same((Style)Application.Current!.Resources["BtnLink"], dugme.Style);
    }
}
