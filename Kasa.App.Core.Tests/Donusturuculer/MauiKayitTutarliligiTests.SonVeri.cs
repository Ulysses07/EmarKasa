using System.Text.RegularExpressions;
using Kasa.App.Views;

namespace Kasa.App.Core.Tests;

/// <summary>Rapor sayfalarında son veri (tasarım 2026-10-02 §3; HD-01): VeriVar'a bağlı her kart, son yükleme hata verince
/// (VeriEski) takip sayfalarıyla aynı opaklıkta soluk gösterilir.</summary>
public partial class MauiKayitTutarliligiTests
{
    [Theory]
    [InlineData("PanelPage.xaml", 2)]
    [InlineData("HaftalikPage.xaml", 1)]
    [InlineData("AylikPage.xaml", 1)]
    public void Rapor_sayfalarinin_veri_kartlari_eski_veride_soluktur(string dosya, int kartSayisi)
    {
        var xaml = Oku(Path.Combine("Views", dosya));
        var kartlar = Regex.Matches(xaml, @"<Border Style=""\{StaticResource (Card|CardHero)\}"" IsVisible=""\{Binding VeriVar\}"">\s*<!--[^>]*-->\s*<Border\.Triggers>\s*"
            + @"<DataTrigger TargetType=""Border"" Binding=""\{Binding VeriEski\}"" Value=""True"">\s*<Setter Property=""Opacity"" Value=""([0-9.]+)"" />");
        Assert.Equal(kartSayisi, Regex.Matches(xaml, @"IsVisible=""\{Binding VeriVar\}""").Count);
        Assert.Equal(kartSayisi, kartlar.Count);
        Assert.All(kartlar, k => Assert.Equal(TakipUi.EskiVeriOpakligi, double.Parse(k.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Kasalar alt bölümleri (takip özeti, kasa kontrolü, çekler) her başarılı panel yüklemesinden sonra yenilenir; panelin
    /// hatası onların son verisini silmez (VeriVar artık hatada inmez).</summary>
    [Fact]
    public void Kasalar_alt_bolumleri_panel_yuklendikce_yenilenir()
    {
        var kod = Oku(Path.Combine("Views", "PanelPage.xaml.cs"));
        Assert.Contains("_vm.Yuklendi += async (_, _) =>", kod);
        Assert.DoesNotContain("nameof(vm.VeriVar)", kod);
    }
}
