using System.Windows.Input;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>Sayfanın durum şeridi (XAML sayfalarının tek durum başlığı): yükleniyor göstergesi, hata, bilgi iletisi ve son
/// güncelleme + "Yenile / tekrar dene". Eski RaporDurumu ile İşlemler/Alışlar sayfalarındaki kopyaların birleşimidir;
/// her parçanın stili ve sırası o kopyalarla aynıdır (Kasa.App.Core.Tests GorunumEsdegerligiTests eski biçimlendirmeyle
/// karşılaştırır). Parçalar yalnız gerektiğinde görünür:
/// - Hata doluyken hata metni: <see cref="HataKutusu"/> false ise düz LblError etiketi (rapor sayfaları), true ise ErrorBox.
/// - Mesaj doluyken yeşil bilgi iletisi; <see cref="YenileCommand"/> verilmişse son güncelleme satırı ve Yenile düğmesi.
/// Hiçbir parça görünmüyorsa şeridin kendisi de gizlenir: üst yığında boşluk (Spacing) bırakmaz. Bu yüzden sayfa
/// IsVisible'ı bağlamaz. Parçalar arası boşluk Spacing'dir (varsayılan 8).</summary>
public class DurumSeridi : VerticalStackLayout
{
    public static readonly BindableProperty MesgulProperty = Ozellik(nameof(Mesgul), typeof(bool), false);
    public static readonly BindableProperty YukleniyorMetniProperty = Ozellik(nameof(YukleniyorMetni), typeof(string), "Veriler yükleniyor…");
    public static readonly BindableProperty HataProperty = Ozellik(nameof(Hata), typeof(string), null);
    public static readonly BindableProperty HataKutusuProperty = Ozellik(nameof(HataKutusu), typeof(bool), false);
    public static readonly BindableProperty MesajProperty = Ozellik(nameof(Mesaj), typeof(string), null);
    public static readonly BindableProperty SonGuncellemeMetniProperty = Ozellik(nameof(SonGuncellemeMetni), typeof(string), null);
    public static readonly BindableProperty YenileCommandProperty = Ozellik(nameof(YenileCommand), typeof(ICommand), null);

    public bool Mesgul { get => (bool)GetValue(MesgulProperty); set => SetValue(MesgulProperty, value); }
    public string YukleniyorMetni { get => (string)GetValue(YukleniyorMetniProperty); set => SetValue(YukleniyorMetniProperty, value); }
    public string? Hata { get => (string?)GetValue(HataProperty); set => SetValue(HataProperty, value); }
    public bool HataKutusu { get => (bool)GetValue(HataKutusuProperty); set => SetValue(HataKutusuProperty, value); }
    public string? Mesaj { get => (string?)GetValue(MesajProperty); set => SetValue(MesajProperty, value); }
    public string? SonGuncellemeMetni { get => (string?)GetValue(SonGuncellemeMetniProperty); set => SetValue(SonGuncellemeMetniProperty, value); }
    public ICommand? YenileCommand { get => (ICommand?)GetValue(YenileCommandProperty); set => SetValue(YenileCommandProperty, value); }

    private readonly HorizontalStackLayout _yukleniyor;
    private readonly ActivityIndicator _gosterge;
    private readonly Label _yukleniyorMetni;
    private readonly Label _hata;
    private readonly Border _hataKutusu;
    private readonly Label _hataKutusuMetni;
    private readonly Label _mesaj;
    private readonly Grid _yenileSatiri;
    private readonly Label _sonGuncelleme;
    private readonly Button _yenile;

    public DurumSeridi()
    {
        Spacing = 8;
        _gosterge = new ActivityIndicator { WidthRequest = 20, HeightRequest = 20 };
        _yukleniyorMetni = new Label { Style = (Style)Application.Current!.Resources["LblPageSub"], VerticalOptions = LayoutOptions.Center };
        _yukleniyor = new UyumluSatir { Spacing = 8, Children = { _gosterge, _yukleniyorMetni } };
        _hata = new Label { Style = (Style)Application.Current!.Resources["LblError"] };
        _hataKutusuMetni = new Label { Style = (Style)Application.Current!.Resources["LblError"] };
        _hataKutusu = new Border { Style = (Style)Application.Current!.Resources["ErrorBox"], Content = _hataKutusuMetni };
        _mesaj = new Label { Style = (Style)Application.Current!.Resources["LblPageSub"], TextColor = (Color)Application.Current!.Resources["Green"] };
        _sonGuncelleme = new Label { Style = (Style)Application.Current!.Resources["LblPageSub"], VerticalOptions = LayoutOptions.Center };
        _yenile = new Button { Text = "Yenile / tekrar dene", Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        _yenileSatiri = new UyumluIzgara
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
        };
        _yenileSatiri.Add(_sonGuncelleme);
        _yenileSatiri.Add(_yenile, 1);
        Children.Add(_yukleniyor);
        Children.Add(_hata);
        Children.Add(_hataKutusu);
        Children.Add(_mesaj);
        Children.Add(_yenileSatiri);
        Guncelle();
    }

    private static BindableProperty Ozellik(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(DurumSeridi), varsayilan, propertyChanged: (s, _, _) => ((DurumSeridi)s).Guncelle());

    private void Guncelle()
    {
        _yukleniyor.IsVisible = Mesgul;
        _gosterge.IsRunning = Mesgul;
        _yukleniyorMetni.Text = YukleniyorMetni;
        // Görünürlük sayfalardaki DoluIse dönüştürücüsüyle aynı kural: boş ya da yalnız boşluk olan metin gösterilmez.
        var hataVar = !string.IsNullOrWhiteSpace(Hata);
        _hata.Text = Hata;
        _hata.IsVisible = hataVar && !HataKutusu;
        _hataKutusuMetni.Text = Hata;
        _hataKutusu.IsVisible = hataVar && HataKutusu;
        _mesaj.Text = Mesaj;
        _mesaj.IsVisible = !string.IsNullOrWhiteSpace(Mesaj);
        _sonGuncelleme.Text = SonGuncellemeMetni;
        _yenile.Command = YenileCommand;
        _yenile.IsEnabled = !Mesgul;
        _yenileSatiri.IsVisible = YenileCommand is not null;
        IsVisible = _yukleniyor.IsVisible || _hata.IsVisible || _hataKutusu.IsVisible || _mesaj.IsVisible || _yenileSatiri.IsVisible;
    }
}
