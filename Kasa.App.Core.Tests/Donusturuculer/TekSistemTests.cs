using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Tek görsel sistem (docs/specs/2026-11-26-slop-temizligi.md §7): kodla yazılmış sayfalar XAML sayfalarının ölçeğini ve form alanı
/// görünümünü kullanır. Kart başlığı bölüm başlığıyla aynıdır (14 kalın); 28 ve 18 punto takip başlıkları kalktı; metin, tutar, seçim ve
/// tarih girdisi FieldBorder çerçevesindedir (Windows'ta WinUI'nin kendi kenarı kaldırıldığı için görünür tek sınır budur).</summary>
public class TekSistemTests
{
    private static Style Stil(string anahtar) => (Style)Application.Current!.Resources[anahtar];

    public static TheoryData<string> Sayfalar => ["Kartlar · kart bilgisi", "Kartlar · ödeme formu", "Krediler", "Krediler · seçili", "Çekler · yeni",
        "Aylık giderler · yeni şablon", "Cariler", "Bildirimler", "Rapor"];

    /// <summary>Görünen her girdinin bir FieldBorder atası vardır.</summary>
    [Theory]
    [MemberData(nameof(Sayfalar))]
    public async Task Gorunen_her_girdi_alan_cercevesindedir(string ad)
    {
        GorunumOrtami.Kur();
        var sayfa = await DugmeVeRenkKurallariTests.SayfaKur(ad);
        var cercevesiz = sayfa.GetVisualTreeDescendants().OfType<View>()
            .Where(v => TakipUi.GirdiMi(v) && DugmeVeRenkKurallariTests.Gorunur(v) && !CercevesiVar(v))
            .Select(v => $"{v.GetType().Name} {SemanticProperties.GetDescription(v) ?? (v as Entry)?.Placeholder ?? (v as Picker)?.Title}")
            .ToList();
        Assert.True(cercevesiz.Count == 0, $"{ad}: " + string.Join(", ", cercevesiz));
    }

    internal static bool CercevesiVar(Element e)
    {
        for (var u = e.Parent; u is not null; u = u.Parent)
            if (u is Border b && ReferenceEquals(b.Style, Stil("FieldBorder")))
                return true;
        return false;
    }

    /// <summary>XAML sayfalarında da her girdi FieldBorder çerçevesinde ya da çerçeveli FormAlani'ndadır.</summary>
    [Fact]
    public void Xaml_sayfalarinda_her_girdi_alan_cercevesindedir()
    {
        string[] girdiler = ["Entry", "Picker", "DatePicker", "TimePicker", "ParaGirisi", "Editor"];
        var kok = GorunumOrtami.DepoKoku();
        var dosyalar = Directory.GetFiles(Path.Combine(kok, "Kasa.App", "Views"), "*.xaml").Append(Path.Combine(kok, "Kasa.App", "AppShell.xaml"));
        var cercevesiz = new List<string>();
        foreach (var dosya in dosyalar)
        {
            foreach (var e in System.Xml.Linq.XDocument.Load(dosya).Descendants().Where(e => girdiler.Contains(e.Name.LocalName)))
            {
                var cerceveli = e.Ancestors().Any(u => u.Name.LocalName == "Border" && ((string?)u.Attribute("Style") ?? "").Contains("FieldBorder", StringComparison.Ordinal)
                    || u.Name.LocalName == "FormAlani" && (string?)u.Attribute("Cerceveli") != "False");
                if (!cerceveli)
                    cercevesiz.Add($"{Path.GetFileName(dosya)}: {e.Name.LocalName} {(string?)e.Attribute("Placeholder") ?? (string?)e.Attribute("Title")}");
            }
        }
        Assert.True(cercevesiz.Count == 0, string.Join("\n", cercevesiz));
    }

    [Fact]
    public void Kart_basligi_bolum_basligiyla_ayni_olcekte()
    {
        GorunumOrtami.Kur();
        var kart = TakipUi.Kart("Kanal alt limitleri");
        var baslik = kart.GetVisualTreeDescendants().OfType<Label>().First();
        Assert.Same(Stil("LblSection"), baslik.Style);
        Assert.Equal((14d, FontAttributes.Bold), (baslik.FontSize, baslik.FontAttributes));
    }

    [Fact]
    public void Takip_ailesinde_28_ve_18_punto_baslik_kalmadi()
    {
        GorunumOrtami.Kur();
        Assert.False(Application.Current!.Resources.ContainsKey("LblTakipSayfaBaslik"));
        foreach (var anahtar in new[] { "LblTakipKartBaslik", "LblTakipBuyuk", "LblTakipMetin", "LblTakipKucuk" })
        {
            var boyut = new Label { Style = Stil(anahtar) }.FontSize;
            Assert.InRange(boyut, 12, 15);
        }
    }
}
