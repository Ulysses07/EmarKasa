using Kasa.App.Controls;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Form alanı (FormAlani, TakipUi.Alan, TakipUi.FormHatasi) gerçek MAUI bağlama motoruyla: AlanHatalari dizinleyicisine
/// bağlanan hata alanın altında görünür, çerçeve kırmızılaşır, ekran okuyucu adı iletiyi içerir; hata kalkınca hepsi geri döner.</summary>
public class FormAlaniTests
{
    public sealed class Baglam
    {
        public AlanHatalari Hatalar { get; } = new();
        public string Ad { get; set; } = "";
    }

    private static Brush Firca(string anahtar) => (Brush)Application.Current!.Resources[anahtar];

    [Fact]
    public void Xaml_ailesi_hata_varken_kirmizi_cerceve_ileti_ve_erisilebilir_ad_gosterir()
    {
        GorunumOrtami.Kur();
        var girdi = new Entry();
        var alan = new FormAlani { Baslik = "Açıklama", Alan = "Ad", Icerik = girdi };
        alan.SetBinding(FormAlani.HataProperty, "Hatalar[Ad]");
        var baglam = new Baglam();
        alan.BindingContext = baglam;
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();
        var ileti = alan.GetVisualTreeDescendants().OfType<Label>().Last();

        Assert.Same(girdi, cerceve.Content);
        Assert.Same((Style)Application.Current!.Resources["FieldBorder"], cerceve.Style);
        Assert.False(ileti.IsVisible);
        // K-2: girdinin Description'ı her zaman başlıktır (hata yokken de); ClearValue ile silinmez.
        Assert.Equal("Açıklama", SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Ayarla("Ad", "Açıklama boş olamaz.");
        Assert.Equal("Açıklama boş olamaz.", alan.Hata);
        Assert.True(ileti.IsVisible);
        Assert.Equal("Açıklama boş olamaz.", ileti.Text);
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        Assert.Equal("Açıklama. Açıklama boş olamaz.", SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Temizle("Ad");
        Assert.Null(alan.Hata);
        Assert.False(ileti.IsVisible);
        Assert.Same(Firca("BrushFieldStroke"), cerceve.Stroke);
        Assert.Equal("Açıklama", SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Ayarla("Ad", "x");
        baglam.Hatalar.Temizle();
        Assert.Null(alan.Hata);
    }

    [Fact]
    public void Cercevesiz_xaml_alani_cip_grubunu_sarar_cerceve_yalniz_hatada_gorunur()
    {
        GorunumOrtami.Kur();
        var cipler = new HorizontalStackLayout();
        var alan = new FormAlani { Baslik = "Kanal", Alan = "Kanal", Icerik = cipler, Cerceveli = false, Hata = "Kanal seçin." };
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();

        Assert.Same((Style)Application.Current!.Resources["LblField"], alan.GetVisualTreeDescendants().OfType<Label>().First().Style);
        Assert.Null(cerceve.Style);
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        alan.Hata = null;
        Assert.Same(Brush.Transparent, cerceve.Stroke);
    }

    [Fact]
    public void Takip_ailesi_cercevesi_yalniz_hatada_gorunur_genel_hata_kutusu_yalniz_doluyken()
    {
        GorunumOrtami.Kur();
        var baglam = new Baglam();
        var alan = TakipUi.Alan("Tutar", TakipUi.Girdi("Ad"), nameof(Baglam.Hatalar), "Tutar");
        var kutu = TakipUi.FormHatasi("Hatalar.Genel");
        var panel = new VerticalStackLayout { Children = { kutu, alan }, BindingContext = baglam };
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();

        Assert.Equal("Tutar", alan.Alan);
        Assert.Same((Style)Application.Current!.Resources["LblTakipKucuk"], alan.GetVisualTreeDescendants().OfType<Label>().First().Style);
        Assert.Same(Brush.Transparent, cerceve.Stroke);
        Assert.False(kutu.IsVisible);

        baglam.Hatalar.Ayarla("Tutar", "Tutar sıfırdan büyük olmalı.");
        baglam.Hatalar.Genel = "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.";
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        Assert.True(kutu.IsVisible);
        Assert.Same((Style)Application.Current!.Resources["ErrorBox"], kutu.Style);
        Assert.Equal(baglam.Hatalar.Genel, ((Label)kutu.Content!).Text);

        baglam.Hatalar.Temizle();
        Assert.Same(Brush.Transparent, cerceve.Stroke);
        Assert.False(kutu.IsVisible);
        Assert.NotNull(panel);
    }
}
