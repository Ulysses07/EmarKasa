using System.Collections.ObjectModel;
using System.Globalization;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using Kasa.App.Converters;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Tablo satırı (docs/specs/2026-11-26-slop-temizligi.md §6): liste satırında solda başlık ve gri özet, sağda işaretli tutar sütunu
/// ve en çok bir hafif eylem bağlantısı (44 px'lik ikincil düğme yok); İşlemler satırında düzenle, sil ve kaynak PDF yerine satıra dokunma ve
/// tek "⋯". Satır tutarı kasa etkisi yönüyle: gider eksi, gelir artı.</summary>
public class TabloSatiriTests
{
    public sealed class Baglam
    {
        public ObservableCollection<object> Satirlar { get; } = [];
    }

    public sealed record DuzSatir(string Baslik, string Ozet);

    private static AylikGiderSatiri Gider(decimal tutar) => new(new AylikGiderSatirDto(1, 1, "Kira", "Kira", tutar, new DateOnly(2026, 9, 1),
        DagilimBicimleri.Genel, [], AylikGiderDurumlari.Planlandi));

    [Fact]
    public void Liste_satirinda_tutar_sutunu_isaretli_ve_eylem_hafif_baglantidir()
    {
        GorunumOrtami.Kur();
        var baglam = new Baglam();
        baglam.Satirlar.Add(Gider(12_000m));
        baglam.Satirlar.Add(new DuzSatir("Bildirim", "Kart kesimi"));
        var liste = (Layout)TakipUi.Liste<object>(nameof(Baglam.Satirlar), _ => Task.CompletedTask, "Öde");
        liste.BindingContext = baglam;

        var satirlar = liste.Children.OfType<Layout>().ToArray();
        Assert.Equal(2, satirlar.Length);
        var giderTutari = satirlar[0].GetVisualTreeDescendants().OfType<Label>().Single(l => l.Style == (Style)Application.Current!.Resources["LblMoney"]);
        Assert.Equal(("−12.000,00", (Color)Application.Current!.Resources["Neg"], true), (giderTutari.Text, giderTutari.TextColor, giderTutari.IsVisible));
        var duzTutar = satirlar[1].GetVisualTreeDescendants().OfType<Label>().Single(l => l.Style == (Style)Application.Current!.Resources["LblMoney"]);
        Assert.False(duzTutar.IsVisible);

        var eylemler = liste.GetVisualTreeDescendants().OfType<Button>().ToArray();
        Assert.All(eylemler, b => Assert.Same((Style)Application.Current!.Resources["BtnLink"], b.Style));
        Assert.Equal(["Öde", "Öde"], eylemler.Select(b => b.Text));
    }

    [Theory]
    [InlineData("1500", "+1.500,00")]
    [InlineData("-553.15", "−553,15")]
    [InlineData("0", "0,00")]
    public void ParaBicim_isaretli_parametresiyle_yonu_yazar(string tutar, string beklenen)
        => Assert.Equal(beklenen, new ParaBicimConverter().Convert(decimal.Parse(tutar, CultureInfo.InvariantCulture), typeof(string),
            ParaBicimConverter.Isaretli, CultureInfo.InvariantCulture));

    [Fact]
    public void Satir_tutari_kasa_etkisi_yonuyle_baslik_tutari_yazmaz()
    {
        var gider = Gider(12_000m);
        Assert.Equal((-12_000m, "Kira · Ödeme bekliyor"), (gider.SatirTutari, gider.Baslik));
        var taksit = new TaksitSatiri(new KrediPlanTaksitDto(5, 3, new DateOnly(2026, 10, 1), 2_500m, TaksitDurumlari.Bekliyor, null, []));
        Assert.Equal((-2_500m, "3. taksit · 01.10.2026"), (taksit.SatirTutari, taksit.Baslik));
    }

    /// <summary>İşlemler satırında düzenle/sil/kaynak düğmeleri yok: satıra dokunmak düzenler, öteki eylemler tek "⋯" menüsünde.</summary>
    [Fact]
    public void Islemler_satirinda_tek_eylem_menusu_ve_satira_dokunma_var()
    {
        var xaml = GorunumOrtami.Oku("Views/IslemlerPage.xaml");
        var sablon = xaml[xaml.IndexOf("<DataTemplate x:DataType=\"api:IslemDto\">", StringComparison.Ordinal)..];
        sablon = sablon[..sablon.IndexOf("</DataTemplate>", StringComparison.Ordinal)];
        Assert.DoesNotContain("BtnRowEdit", sablon);
        Assert.DoesNotContain("BtnRowDelete", sablon);
        Assert.Contains("<TapGestureRecognizer Tapped=\"SatirTiklandi\" />", sablon);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sablon, "<Button\\b"));
        Assert.Contains("Clicked=\"EylemlerTiklandi\"", sablon);
    }
}
