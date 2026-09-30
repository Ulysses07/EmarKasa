using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kabuk menü öğesinin zemini Background (SolidColorBrush) yerine BackgroundColor ile yazılır (dotnet/maui#38813 geçici
/// çözümü; AppShell.xaml). ESKİ şablon (aşağıda, düzeltmeden önceki hâli) ile AppShell.xaml'daki BUGÜNKÜ şablon aynı
/// uygulama kaynaklarıyla yüklenir; her görsel durumda (Normal, Selected, PointerOver) platform işleyicisinin okuduğu zemin
/// (IView.Background: Background boşsa BackgroundColor'dan SolidColorBrush) aynı renktir ve zemin dışındaki bütün
/// görsel özellikler birebir aynıdır. Windows işleyicisi ikisini de aynı düz dolguya (WinUI SolidColorBrush) çevirir.
/// </summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record MenuBaglami(string Title);

    /// <summary>AppShell.xaml Shell.ItemTemplate, düzeltmeden önce (Background setter'ları).</summary>
    private const string EskiMenuOgesi = """
        <Grid Margin="10,2" Padding="12,0" HeightRequest="38" ColumnDefinitions="Auto,*" ColumnSpacing="10">
            <VisualStateManager.VisualStateGroups>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal">
                        <VisualState.Setters>
                            <Setter Property="Background" Value="Transparent" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Selected">
                        <VisualState.Setters>
                            <Setter Property="Background" Value="{StaticResource SidebarActive}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="PointerOver">
                        <VisualState.Setters>
                            <Setter Property="Background" Value="{StaticResource SidebarHover}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateManager.VisualStateGroups>
            <Ellipse WidthRequest="6" HeightRequest="6" Fill="{StaticResource SidebarAccent}"
                     VerticalOptions="Center" />
            <Label Grid.Column="1" Text="{Binding Title}" FontSize="13.5"
                   TextColor="{StaticResource SidebarText}" VerticalOptions="Center" />
        </Grid>
        """;

    [Theory]
    [InlineData("Normal", null)]
    [InlineData("Selected", "SidebarActive")]
    [InlineData("PointerOver", "SidebarHover")]
    public void Kabuk_menu_ogesi_zemini_her_gorsel_durumda_ayni(string durum, string? renkAnahtari)
    {
        var sablon = Regex.Match(GorunumOrtami.Oku("AppShell.xaml"), @"<Shell\.ItemTemplate>\s*<DataTemplate>(.*?)</DataTemplate>", RegexOptions.Singleline);
        Assert.True(sablon.Success, "AppShell.xaml'da Shell.ItemTemplate bulunamadı.");
        var baglam = new MenuBaglami("Kasalar");
        var eski = Assert.IsType<Grid>(GorunumOrtami.Yukle(EskiMenuOgesi, baglam).Content);
        var yeni = Assert.IsType<Grid>(GorunumOrtami.Yukle(sablon.Groups[1].Value, baglam).Content);
        Assert.True(VisualStateManager.GoToState(eski, durum), $"Eski şablonda {durum} durumu yok.");
        Assert.True(VisualStateManager.GoToState(yeni, durum), $"Bugünkü şablonda {durum} durumu yok.");

        var beklenen = renkAnahtari is null ? Colors.Transparent : (Color)Application.Current!.Resources[renkAnahtari];
        Assert.Equal(beklenen.ToArgbHex(true), Zemin(eski));
        Assert.Equal(beklenen.ToArgbHex(true), Zemin(yeni));
        Assert.True(Brush.IsNullOrEmpty(yeni.Background), "Bugünkü şablon Background (Brush) yazmamalı (dotnet/maui#38813).");

        var once = ZeminDisi().Replace(GorunumOrtami.Dok(eski), "");
        var sonra = ZeminDisi().Replace(GorunumOrtami.Dok(yeni), "");
        Assert.Contains("Label", once, StringComparison.Ordinal);
        Assert.True(once == sonra, $"Zemin dışında görünüm farklı.\nÖNCE:\n{once}\nSONRA:\n{sonra}");
    }

    /// <summary>İşleyicinin çizdiği zemin: IView.Background düz boya olmalı; rengi ARGB onaltılık.</summary>
    private static string Zemin(IView gorunum)
    {
        var boya = Assert.IsType<SolidPaint>(gorunum.Background, exactMatch: false);
        return boya.Color.ToArgbHex(true);
    }

    /// <summary>Zemini yazan alanlar ve görsel durum setter'ları (özellik adı Background ↔ BackgroundColor) dökümden atılır;
    /// zemin <see cref="Zemin"/> ile ayrıca karşılaştırılır.</summary>
    [GeneratedRegex(@" (?:BackgroundColor|Background|Durumlar)=\S+")]
    private static partial Regex ZeminDisi();
}
