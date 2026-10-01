namespace Kasa.App.Core.Tests;

/// <summary>Kabuk menüsü öğe şablonundaki rozet (AppShell.xaml) gerçek uygulama kaynaklarıyla yüklenir: sayı varken görünür, zemini
/// SidebarAccent düz boyadır (Background fırçası yazılmaz), girdiyi geçirir; öğe düğmesi ekran okuyucuya rozetli adı verir.</summary>
public partial class GorunumEsdegerligiTests
{
    [Fact]
    public void Kabuk_menu_ogesi_rozeti_sayi_varken_gorunur_ve_dugme_erisim_adini_tasir()
    {
        var oge = new MenuOgesi(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler", _ => { });
        var kok = MenuOgesiYukle(oge);
        var rozet = kok.Children.OfType<Border>().Single();
        Assert.False(rozet.IsVisible);
        oge.Rozet = 5;
        Assert.True(rozet.IsVisible);
        Assert.Equal("5", Assert.IsType<Label>(rozet.Content).Text);
        Assert.Equal(((Color)Application.Current!.Resources["SidebarAccent"]).ToArgbHex(true), rozet.BackgroundColor.ToArgbHex(true));
        Assert.True(Brush.IsNullOrEmpty(rozet.Background), "Rozet Background (Brush) yazmamalı (dotnet/maui#38813).");
        Assert.True(rozet.InputTransparent);
        Assert.Equal("Bildirimler, 5 okunmamış", SemanticProperties.GetDescription(kok.Children.OfType<Button>().Single()));
    }
}
