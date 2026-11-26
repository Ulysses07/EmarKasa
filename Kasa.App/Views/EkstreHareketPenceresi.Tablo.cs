using System.Globalization;
using Kasa.App.Converters;
using Kasa.App.Core;

namespace Kasa.App.Views;

// Hareket tablosu: tek kaydırıcı (sayfada başka kaydırıcı yok); satırda düğme yok, satıra dokunmak paneli açar ya da kapatır, kutu yalnız işaret.
public sealed partial class EkstreHareketPenceresi
{
    private CollectionView _tablo = null!;
    private static readonly GridLength SeritGenisligi = new(3), KutuGenisligi = new(36), TarihGenisligi = new(56), TurGenisligi = new(230),
        TutarGenisligi = new(120);

    /// <summary>Bu genişliğin altında (telefon, paneli açık dar pencere) sabit sütunlar açıklamayı ezer: tür · kanal açıklamanın altına iner.</summary>
    private const double DarTablo = 680;

    private static Grid SatirIzgarasi() => new()
    {
        ColumnSpacing = 10,
        ColumnDefinitions = { new(SeritGenisligi), new(KutuGenisligi), new(TarihGenisligi), new(GridLength.Star), new(TurGenisligi), new(TutarGenisligi) },
        RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
    };

    /// <summary>Dar tabloda tür · kanal sütunu kalkar: satırda açıklamanın altına iner, başlıkta gizlenir. Başlık ve satırlar aynı kuralla hizalı kalır.</summary>
    private static void SutunlariUyarla(Grid izgara, View tur, bool baslik)
    {
        var dar = izgara.Width is > 0 and < DarTablo;
        izgara.ColumnSpacing = dar ? 6 : 10;
        izgara.ColumnDefinitions[4].Width = dar ? new GridLength(0) : TurGenisligi;
        if (baslik)
            tur.IsVisible = !dar;
        else
        {
            Grid.SetColumn(tur, dar ? 3 : 4);
            Grid.SetRow(tur, dar ? 1 : 0);
        }
    }

    private View TabloBolumu()
    {
        var baslik = SatirIzgarasi();
        baslik.Padding = new Thickness(0, 4, 12, 6);
        foreach (var (metin, sutun) in new[] { ("Tarih", 2), ("Açıklama", 3), ("Tür · kanal", 4), ("Tutar", 5) })
        {
            var etiket = Etiket("LblRowSecondary");
            etiket.Text = metin;
            if (sutun == 5)
                etiket.HorizontalTextAlignment = TextAlignment.End;
            if (sutun == 4)
                baslik.SizeChanged += (_, _) => SutunlariUyarla(baslik, etiket, baslik: true);
            baslik.Add(etiket, sutun);
        }
        _tablo = new CollectionView
        {
            AutomationId = "HareketTablosu",
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(Satir),
            EmptyView = new Label { Text = "Bu süzgeçte satır yok.", Style = Stil("LblTakipKucuk"), Margin = new Thickness(0, 16) },
        };
        _tablo.SetBinding(ItemsView.ItemsSourceProperty, nameof(_vm.Gorunenler));
        var kart = new Border { Padding = new Thickness(12, 8, 0, 0), Content = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, Children = { baslik } } };
        kart.SetDynamicResource(StyleProperty, "Card");
        var icerik = (Grid)kart.Content;
        icerik.Add(_tablo, 0, 1);
        return kart;
    }

    private View Satir()
    {
        var izgara = SatirIzgarasi();
        izgara.Padding = new Thickness(0, 7, 12, 7);
        izgara.MinimumHeightRequest = 44;
        var serit = new BoxView { Color = Renk("UyariKenar") };
        serit.SetBinding(IsVisibleProperty, nameof(EkstreSatirEditor.KontrolNedeni), converter: new DoluIseConverter());
        izgara.Add(serit);
        Grid.SetRowSpan(serit, 2);
        var kutu = new CheckBox { MinimumWidthRequest = 0, VerticalOptions = LayoutOptions.Center };
        kutu.SetBinding(CheckBox.IsCheckedProperty, nameof(EkstreSatirEditor.Secili));
        kutu.SetBinding(IsEnabledProperty, nameof(EkstreSatirEditor.Secilebilir));
        kutu.SetBinding(IsVisibleProperty, nameof(EkstreSatirEditor.Kapali), converter: new TersIseConverter());
        kutu.SetBinding(SemanticProperties.DescriptionProperty, new Binding(nameof(EkstreSatirEditor.Ozet), stringFormat: "Kaydetmek için seç: {0}"));
        izgara.Add(kutu, 1);
        Grid.SetRowSpan(kutu, 2);
        var tarih = Etiket("LblRowPrimary");
        tarih.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.TarihGosterimi));
        var aciklama = Etiket("LblRowPrimary");
        aciklama.MaxLines = 1;
        aciklama.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.Aciklama));
        var tur = Etiket("LblRowPrimary");
        tur.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.TurKanalMetni));
        var neden = Etiket("LblRowSecondary");
        neden.TextColor = Renk("UyariMetin");
        neden.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.KontrolNedeni));
        neden.SetBinding(IsVisibleProperty, nameof(EkstreSatirEditor.KontrolNedeni), converter: new DoluIseConverter());
        var kaynak = Etiket("LblRowSecondary");
        kaynak.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.KaynakEtiketi));
        kaynak.SetBinding(IsVisibleProperty, new MultiBinding
        {
            Converter = new KaynakGorunurConverter(),
            Bindings = { new Binding(nameof(EkstreSatirEditor.KaynakEtiketi)), new Binding(nameof(EkstreSatirEditor.KontrolNedeni)) },
        });
        var tutar = Etiket("LblRowPrimary");
        tutar.HorizontalTextAlignment = TextAlignment.End;
        tutar.SetBinding(Label.TextProperty, nameof(EkstreSatirEditor.TutarGosterimi));
        foreach (var (yon, renk) in new[] { ("Giris", "Pos"), ("Cikis", "Neg"), ("Belirsiz", "UyariMetin") })
        {
            var tetik = new DataTrigger(typeof(Label)) { Binding = new Binding(nameof(EkstreSatirEditor.EtkinYon)), Value = yon };
            tetik.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Renk(renk) });
            tutar.Triggers.Add(tetik);
        }
        var turYigini = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { tur, neden, kaynak } };
        foreach (var (hucre, sutun) in new (View, int)[] { (tarih, 2), (aciklama, 3), (turYigini, 4), (tutar, 5) })
        {
            hucre.VerticalOptions = LayoutOptions.Center;
            izgara.Add(hucre, sutun);
            var dokunus = new TapGestureRecognizer();
            dokunus.Tapped += (_, _) => { if (izgara.BindingContext is EkstreSatirEditor s) SatiriAcKapat(s); };
            hucre.GestureRecognizers.Add(dokunus);
        }
        izgara.SizeChanged += (_, _) => SutunlariUyarla(izgara, turYigini, baslik: false);
        izgara.SetBinding(BackgroundColorProperty, new MultiBinding
        {
            Converter = new AcikSatirRengi(Renk("GreenSoft")),
            Bindings = { new Binding("."), new Binding(nameof(_vm.SeciliSatir), source: _vm) },
        });
        var ayirici = new BoxView { HeightRequest = 1, Color = Renk("RowLine") };
        return new VerticalStackLayout { Spacing = 0, Children = { izgara, ayirici } };
    }

    /// <summary>Kaynak etiketi yalnız "Kontrol edin" nedeni yokken ve doluyken görünür.</summary>
    private sealed class KaynakGorunurConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
            values is [string { Length: > 0 }, null or ""];
        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Paneli açık satırın zemini.</summary>
    private sealed class AcikSatirRengi(Color acik) : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
            values is [{ } satir, { } secili] && ReferenceEquals(satir, secili) ? acik : Colors.Transparent;
        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
