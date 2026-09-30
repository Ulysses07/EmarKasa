using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kabuk menü şablonları (Shell.ItemTemplate, Shell.MenuItemTemplate) Background (Brush) yazmaz: MAUI 10.0.100–10.0.110'da
/// (dotnet/maui#38813) menü öğesinin görsel durumu Background setter'ı uygulayınca fırçanın kaynak bildirimi
/// (VisualElement.NotifyBackgroundChanges → OnResourcesChanged) menü öğesini (Windows ShellFlyoutItemView) yeniden görsel
/// duruma sokar; UI iş parçacığı bu döngüde %100 CPU ile askıda kalır (Release'te girişten birkaç saniye sonra). Düzeltme
/// (dotnet/maui#38887) .NET 10 SR12'de (10.0.120) gelir. O zamana dek şablon kökü, içindeki öğeler, görsel durum setter'ları
/// ve öğelere uygulanan stiller düz renk için BackgroundColor kullanır (görünüm aynı: düz dolgu).
/// </summary>
public partial class MauiKayitTutarliligiTests
{
    private static readonly string[] KabukSablonuOzellikleri = ["Shell.ItemTemplate", "Shell.MenuItemTemplate"];

    [Fact]
    public void Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz()
    {
        var genelStiller = new[] { "Resources/Styles/Styles.xaml", "App.xaml" }
            .SelectMany(d => XDocument.Load(Path.Combine(Uygulama, d), LoadOptions.SetLineInfo).Descendants().Where(e => e.Name.LocalName == "Style"))
            .ToList();
        var hatalar = new List<string>();
        var sablonSayisi = 0;
        foreach (var dosya in Directory.GetFiles(Uygulama, "*.xaml", SearchOption.AllDirectories)
                     .Where(d => !d.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj")))
        {
            var belge = XDocument.Load(dosya, LoadOptions.SetLineInfo);
            var ad = Path.GetRelativePath(Uygulama, dosya).Replace('\\', '/');
            var stiller = genelStiller.Concat(belge.Descendants().Where(e => e.Name.LocalName == "Style")).ToList();
            var sablonlar = belge.Descendants().Where(e => KabukSablonuOzellikleri.Contains(e.Name.LocalName)
                || (e.Name.LocalName == "Setter" && KabukSablonuOzellikleri.Contains((string?)e.Attribute("Property"))));
            foreach (var sablon in sablonlar)
            {
                sablonSayisi++;
                foreach (var oge in sablon.Descendants())
                    hatalar.AddRange(ArkaPlanYazimlari(oge, stiller).Select(h => $"{ad}:{Satir(oge)} ({sablon.Name.LocalName}) {h}"));
            }
        }
        Assert.True(sablonSayisi >= 2, $"Kabuk menü şablonları okunamadı ({sablonSayisi}); AppShell.xaml'da Shell.ItemTemplate ve Shell.MenuItemTemplate beklenir.");
        Assert.True(hatalar.Count == 0,
            "Kabuk menü şablonunda Background (Brush) yazılıyor; MAUI 10.0.100–10.0.110'da menü öğesi UI iş parçacığını sonsuz döngüye sokar "
            + "(dotnet/maui#38813). Düz renk için BackgroundColor kullanın:\n" + string.Join("\n", hatalar));
    }

    [Fact]
    public void Kabuk_menu_sablonu_denetimi_Background_yazimini_yakalar()
    {
        const string xaml = """
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui" xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
              <Shell.Resources>
                <Style x:Key="Taban" TargetType="Grid"><Setter Property="Background" Value="Red" /></Style>
                <Style x:Key="Ogeli" TargetType="Grid" BasedOn="{StaticResource Taban}" />
                <Style TargetType="Label"><Setter Property="VisualElement.Background" Value="Red" /></Style>
                <Style x:Key="Renkli" TargetType="Grid"><Setter Property="BackgroundColor" Value="Red" /></Style>
              </Shell.Resources>
              <Shell.ItemTemplate>
                <DataTemplate>
                  <Grid Background="Transparent">
                    <VisualStateManager.VisualStateGroups>
                      <VisualStateGroup x:Name="CommonStates">
                        <VisualState x:Name="Selected">
                          <VisualState.Setters>
                            <Setter Property="Background" Value="Red" />
                            <Setter Property="BackgroundColor" Value="Red" />
                          </VisualState.Setters>
                        </VisualState>
                      </VisualStateGroup>
                    </VisualStateManager.VisualStateGroups>
                    <Grid Style="{StaticResource Ogeli}" />
                    <Grid Style="{StaticResource Renkli}" BackgroundColor="Red" />
                    <Label />
                    <Border><Border.Background><SolidColorBrush Color="Red" /></Border.Background></Border>
                  </Grid>
                </DataTemplate>
              </Shell.ItemTemplate>
            </Shell>
            """;
        var belge = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        var stiller = belge.Descendants().Where(e => e.Name.LocalName == "Style").ToList();
        var sablon = belge.Descendants().Single(e => e.Name.LocalName == "Shell.ItemTemplate");
        var bulunan = sablon.Descendants().SelectMany(o => ArkaPlanYazimlari(o, stiller)).ToList();
        Assert.Equal(5, bulunan.Count);
        Assert.Contains(bulunan, h => h.Contains("Background=\"Transparent\"", StringComparison.Ordinal));
        Assert.Contains(bulunan, h => h.Contains("<Setter Property=\"Background\"", StringComparison.Ordinal));
        Assert.Contains(bulunan, h => h.Contains("'Ogeli' stili", StringComparison.Ordinal) && h.Contains("'Taban'", StringComparison.Ordinal));
        Assert.Contains(bulunan, h => h.Contains("örtük Label stili", StringComparison.Ordinal));
        Assert.Contains(bulunan, h => h.Contains("<Border.Background>", StringComparison.Ordinal));
    }

    /// <summary>Öğenin Background (Brush) yazımları: öznitelik, özellik öğesi (&lt;X.Background&gt;), Setter ve öğeye
    /// uygulanan stil (x:Key'li Style="{StaticResource}" ya da örtük TargetType stili, BasedOn zinciriyle).</summary>
    private static IEnumerable<string> ArkaPlanYazimlari(XElement oge, List<XElement> stiller)
    {
        var tur = oge.Name.LocalName;
        if (oge.Attribute("Background") is { } a)
            yield return $"<{tur} Background=\"{a.Value}\">";
        if (ArkaPlanOzelligi().IsMatch(tur))
            yield return $"<{tur}>";
        if (tur == "Setter" && ArkaPlanOzelligi().IsMatch((string?)oge.Attribute("Property") ?? ""))
            yield return $"<Setter Property=\"{oge.Attribute("Property")!.Value}\">";
        if (tur.Contains('.', StringComparison.Ordinal) || tur is "Setter" or "VisualState" or "VisualStateGroup" or "DataTemplate")
            yield break;
        var anahtar = StilAnahtari().Match((string?)oge.Attribute("Style") ?? "") is { Success: true } m ? m.Groups[1].Value : null;
        var stil = anahtar is not null
            ? stiller.LastOrDefault(s => (string?)s.Attribute(Xaml + "Key") == anahtar)
            : stiller.LastOrDefault(s => s.Attribute(Xaml + "Key") is null && (string?)s.Attribute("TargetType") == tur);
        var ad = anahtar is not null ? $"'{anahtar}' stili" : $"örtük {tur} stili";
        for (var zincir = new List<string>(); stil is not null && zincir.Count < 10;)
        {
            zincir.Add((string?)stil.Attribute(Xaml + "Key") ?? $"örtük {stil.Attribute("TargetType")?.Value}");
            if (stil.Descendants().Any(s => s.Name.LocalName == "Setter" && ArkaPlanOzelligi().IsMatch((string?)s.Attribute("Property") ?? "")))
            {
                yield return $"<{tur}> {ad} Background yazıyor ({string.Join(" → ", zincir.Select(z => $"'{z}'"))})";
                yield break;
            }
            var taban = StilAnahtari().Match((string?)stil.Attribute("BasedOn") ?? "") is { Success: true } b ? b.Groups[1].Value : null;
            stil = taban is null ? null : stiller.LastOrDefault(s => (string?)s.Attribute(Xaml + "Key") == taban);
        }
    }

    private static int Satir(XElement e) => ((IXmlLineInfo)e).LineNumber;

    /// <summary>Background ya da tür önekli Background (VisualElement.Background, Grid.Background); BackgroundColor değil.</summary>
    [GeneratedRegex(@"^(?:\w+\.)?Background$")]
    private static partial Regex ArkaPlanOzelligi();
    [GeneratedRegex(@"^\{(?:StaticResource|DynamicResource)\s+(?:Key=)?([^\s,}]+)\s*\}$")]
    private static partial Regex StilAnahtari();
}
