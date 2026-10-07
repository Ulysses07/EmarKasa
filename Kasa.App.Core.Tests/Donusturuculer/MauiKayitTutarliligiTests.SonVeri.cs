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

    /// <summary>Kasalar'daki takipte olmayan kayıt uyarısı da son yüklemeden kalır; eski veride panel kartlarıyla aynı soluklukta.</summary>
    [Fact]
    public void Kasalar_takipsiz_uyarisi_eski_veride_soluktur()
    {
        var xaml = Oku(Path.Combine("Views", "PanelPage.xaml"));
        var uyari = Regex.Match(xaml, @"<Border Style=""\{StaticResource CardForm\}"" IsVisible=""\{Binding TakipsizVar\}"">\s*<!--[^>]*-->\s*<Border\.Triggers>\s*"
            + @"<DataTrigger TargetType=""Border"" Binding=""\{Binding VeriEski\}"" Value=""True"">\s*<Setter Property=""Opacity"" Value=""([0-9.]+)"" />");
        Assert.True(uyari.Success);
        Assert.Equal(TakipUi.EskiVeriOpakligi, double.Parse(uyari.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>XAML ile yazılmış takip/liste sayfalarının eski veri tetikleri de (Alışlar gövdesi, İşlemler listesi) kodla yazılan
    /// sayfalarla aynı opaklığı kullanır.</summary>
    [Theory]
    [InlineData("AlislarPage.xaml", "ctl:UyumluBolmeler", 1)]
    [InlineData("IslemlerPage.xaml", "CollectionView", 1)]
    public void Liste_sayfalarinin_eski_veri_tetigi_ortak_opakligi_kullanir(string dosya, string hedef, int sayi)
    {
        var xaml = Oku(Path.Combine("Views", dosya));
        var tetikler = Regex.Matches(xaml, @"<DataTrigger TargetType=""" + hedef + @""" Binding=""\{Binding VeriEski\}"" Value=""True"">\s*<Setter Property=""Opacity"" Value=""([0-9.]+)"" />");
        Assert.Equal(sayi, Regex.Matches(xaml, @"Binding=""\{Binding VeriEski\}""").Count);
        Assert.Equal(sayi, tetikler.Count);
        Assert.All(tetikler, t => Assert.Equal(TakipUi.EskiVeriOpakligi, double.Parse(t.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Kasalar alt bölümleri (takip özeti, çekler, kasa kontrolü) panelle tutarlı soluklaşır: gövdeleri son veri gövdesidir
    /// (GovdeGorunur'a bağlı, VeriEski'de soluk; davranışı TakipSayfasiSonVeriTests sınar). Panele bağlanmaları ve kendi hatalarında
    /// son veriyi korumaları AnaSayfaVeRaporIptalTests'te davranışla sınanır; sayfa yalnız bağlar.</summary>
    [Fact]
    public void Kasalar_alt_bolumleri_son_veri_govdesi_kullanir()
    {
        var panel = Oku(Path.Combine("Views", "PanelPage.xaml.cs"));
        var kontrol = Oku(Path.Combine("Views", "KasaKontrolAlanlari.cs"));
        Assert.Contains("_vm.AltBolumleriBagla(takip, kontrol, cekler);", panel);
        Assert.DoesNotContain("_vm.Yuklendi", panel);
        Assert.Equal(2, Regex.Matches(panel, @"TakipUi\.SonVeriGovdesi\(").Count);   // takip içeriği ve çek satırları
        Assert.DoesNotContain("VeriHazir", panel);
        Assert.Contains("SonVeriGovdesi(govde)", kontrol);
        Assert.Contains("Durum(vm, vm.YukleAsync, body, sonVeri: true)", kontrol);
    }
}
