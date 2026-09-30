using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kabuk menüsünün öğe şablonu (AppShell.xaml Shell.FlyoutContent, x:DataType MenuOgesi) gerçek uygulama kaynaklarıyla
/// yüklenir. Zemin durumun rengidir: normal saydam, fareyle üzerinde SidebarHover, seçili (fare üzerinde olsa da)
/// SidebarActive. Zemin BackgroundColor'dan gelen düz boyadır; Background (Brush) yazılmaz (dotnet/maui#38813 dersi). Açık
/// nokta yalnız seçili öğede görünür. Eski Shell.ItemTemplate ile görünüm eşdeğerliği testi, menü bilerek değiştiği için
/// kaldırıldı (tasarım 2026-09-30 §1); korunan sözleşme durum → zemin rengidir.
/// </summary>
public partial class GorunumEsdegerligiTests
{
    private static Grid MenuOgesiYukle(MenuOgesi oge)
    {
        var sablon = Regex.Match(GorunumOrtami.Oku("AppShell.xaml"), @"<DataTemplate x:DataType=""core:MenuOgesi"">(.*?)</DataTemplate>", RegexOptions.Singleline);
        Assert.True(sablon.Success, "AppShell.xaml'da MenuOgesi şablonu bulunamadı.");
        return Assert.IsType<Grid>(GorunumOrtami.Yukle(sablon.Groups[1].Value, oge).Content);
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, true, "SidebarHover")]
    [InlineData(true, false, "SidebarActive")]
    [InlineData(true, true, "SidebarActive")]
    public void Kabuk_menu_ogesi_zemini_durumun_rengidir(bool secili, bool uzerinde, string? renkAnahtari)
    {
        var oge = new MenuOgesi(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar", _ => { }) { Secili = secili, Uzerinde = uzerinde };
        var kok = MenuOgesiYukle(oge);
        var beklenen = renkAnahtari is null ? Colors.Transparent : (Color)Application.Current!.Resources[renkAnahtari];
        Assert.Equal(beklenen.ToArgbHex(true), Zemin(kok));
        Assert.True(Brush.IsNullOrEmpty(kok.Background), "Menü öğesi Background (Brush) yazmamalı (dotnet/maui#38813).");
        Assert.Equal(secili, kok.Children.OfType<Ellipse>().Single().IsVisible);
    }

    [Fact]
    public void Kabuk_menu_ogesi_zemini_durum_degisince_guncellenir_ve_dugme_basligi_tasir()
    {
        var oge = new MenuOgesi(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar", _ => { });
        var kok = MenuOgesiYukle(oge);
        oge.UzerineGelCommand.Execute(null);
        Assert.Equal(((Color)Application.Current!.Resources["SidebarHover"]).ToArgbHex(true), Zemin(kok));
        oge.Secili = true;
        Assert.Equal(((Color)Application.Current!.Resources["SidebarActive"]).ToArgbHex(true), Zemin(kok));
        oge.Secili = false;
        oge.AyrilCommand.Execute(null);
        Assert.Equal(Colors.Transparent.ToArgbHex(true), Zemin(kok));
        var dugme = kok.Children.OfType<Button>().Single();
        Assert.Equal("Kartlar", SemanticProperties.GetDescription(dugme));
        Assert.Same(oge.SecCommand, dugme.Command);
        Assert.All(kok.Children.OfType<Label>(), l => Assert.True(l.InputTransparent));
    }

    /// <summary>İşleyicinin çizdiği zemin: IView.Background düz boya olmalı; rengi ARGB onaltılık.</summary>
    private static string Zemin(IView gorunum)
    {
        var boya = Assert.IsType<SolidPaint>(gorunum.Background, exactMatch: false);
        return boya.Color.ToArgbHex(true);
    }
}
