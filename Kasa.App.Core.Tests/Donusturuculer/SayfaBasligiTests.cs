using CommunityToolkit.Mvvm.Input;
using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Sayfanın tek başlık satırı (docs/specs/2026-11-26-slop-temizligi.md §3): solda başlık, sağda küçük "Yenile"; "Son güncelleme"
/// yalnız veri eskiyken, gösterge yalnız meşgulken, hata ve ileti yalnız doluyken görünür. Yenile sayfanın yenilemesidir (gömülü
/// bölümler dahil); sayfa yoksa bağlı komut.</summary>
public class SayfaBasligiTests
{
    private static bool Gorunur(Element e) => e is not VisualElement { IsVisible: false } && (e.Parent is null || Gorunur(e.Parent));
    private static IEnumerable<string?> GorunenMetinler(Element kok)
        => kok.GetVisualTreeDescendants().OfType<Label>().Where(Gorunur).Select(l => l.Text).Where(t => !string.IsNullOrEmpty(t));

    [Fact]
    public void Baslik_ve_yenile_gorunur_son_guncelleme_yalniz_veri_eskiyken()
    {
        GorunumOrtami.Kur();
        var b = new SayfaBasligi { Baslik = "Kasalar", SonGuncellemeMetni = "Son güncelleme: 27.11.2026 10:00" };
        Assert.Equal(["Kasalar"], GorunenMetinler(b));
        Assert.True(Gorunur(b.YenileDugmesi));
        Assert.Equal("Yenile", b.YenileDugmesi.Text);
        b.VeriEski = true;
        Assert.Contains("Son güncelleme: 27.11.2026 10:00", GorunenMetinler(b));
    }

    /// <summary>Dar ekranda (telefon) uzun "Son güncelleme … güncel olmayabilir" düğmelerin yanında başlığı ezmez: başlık satırının altında
    /// kendi satırındadır, sağdaki yatay yığında değildir.</summary>
    [Fact]
    public void Son_guncelleme_baslik_satirinin_altinda_kendi_satirinda()
    {
        GorunumOrtami.Kur();
        const string metin = "Son güncelleme: 27.11.2026 10:00 · güncel olmayabilir";
        var b = new SayfaBasligi { Baslik = "Kasalar", SonGuncellemeMetni = metin, VeriEski = true };
        var etiket = b.GetVisualTreeDescendants().OfType<Label>().Single(l => l.Text == metin);
        Assert.IsNotType<HorizontalStackLayout>(etiket.Parent);
        Assert.Equal((1, 2), (Grid.GetRow(etiket), Grid.GetColumnSpan(etiket)));
    }

    [Fact]
    public void Gosterge_yalniz_mesgulken_doner_ve_yenile_kapanir()
    {
        GorunumOrtami.Kur();
        var b = new SayfaBasligi { Baslik = "Kasalar" };
        var gosterge = b.GetVisualTreeDescendants().OfType<ActivityIndicator>().Single();
        Assert.False(Gorunur(gosterge));
        b.Mesgul = true;
        Assert.True(Gorunur(gosterge) && gosterge.IsRunning);
        Assert.False(b.YenileDugmesi.IsEnabled);
    }

    [Fact]
    public void Hata_ve_ileti_yalniz_doluyken_gorunur_hata_kutusu_secilebilir()
    {
        GorunumOrtami.Kur();
        var b = new SayfaBasligi { Baslik = "Alışlar" };
        Assert.False(Gorunur(b.HataEtiketi));
        Assert.False(Gorunur(b.MesajEtiketi));
        b.Hata = "Sunucuya ulaşılamadı.";
        b.Mesaj = "Kaydedildi.";
        Assert.True(Gorunur(b.HataEtiketi) && Gorunur(b.MesajEtiketi));
        Assert.DoesNotContain(b.GetVisualTreeDescendants().OfType<Border>(), Gorunur);
        b.HataKutusu = true;
        Assert.Contains(b.GetVisualTreeDescendants().OfType<Border>(), k => Gorunur(k) && k.GetVisualTreeDescendants().Contains(b.HataEtiketi));
    }

    private sealed class Sayfa : ContentPage, IYenilenebilir
    {
        public int Yenilendi;
        public Task YenileAsync() { Yenilendi++; return Task.CompletedTask; }
    }

    [Fact]
    public void Yenile_sayfanin_yenilemesini_cagirir_sayfa_yoksa_komutu()
    {
        GorunumOrtami.Kur();
        var komut = 0;
        var b = new SayfaBasligi { Baslik = "Ayarlar", YenileCommand = new RelayCommand(() => komut++) };
        var sayfa = new Sayfa { Content = new VerticalStackLayout { Children = { b } } };
        ((IButtonController)b.YenileDugmesi).SendClicked();
        Assert.Equal((1, 0), (sayfa.Yenilendi, komut));

        var yalniz = new SayfaBasligi { Baslik = "Ayarlar", YenileCommand = new RelayCommand(() => komut++) };
        ((IButtonController)yalniz.YenileDugmesi).SendClicked();
        Assert.Equal(1, komut);
    }

    [Fact]
    public void Yenile_gizlenebilir_baslik_ekran_okuyucuda_birinci_duzey()
    {
        GorunumOrtami.Kur();
        var b = new SayfaBasligi { Baslik = "Ekstre içe aktar", YenileGorunur = false };
        Assert.False(Gorunur(b.YenileDugmesi));
        var baslik = b.GetVisualTreeDescendants().OfType<Label>().Single(l => l.Text == "Ekstre içe aktar");
        Assert.Equal(SemanticHeadingLevel.Level1, SemanticProperties.GetHeadingLevel(baslik));
        Assert.Same((Style)Application.Current!.Resources["LblPageTitle"], baslik.Style);
    }
}
