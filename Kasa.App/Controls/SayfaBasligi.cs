using System.Windows.Input;

namespace Kasa.App.Controls;

/// <summary>Sayfanın tek başlık satırı (docs/specs/2026-11-26-slop-temizligi.md §3): solda başlık; sağda yalnız meşgulken dönen gösterge,
/// yalnız veri eskiyken "Son güncelleme" ve küçük "Yenile"; altında yalnız doluyken hata (düz ya da kutulu) ve ileti. Yenile sayfanın
/// yenilemesidir (<see cref="IYenilenebilir"/>: ana veri ve gömülü bölümler, kabuğun "Yeniden dene"siyle aynı yol); başlık bir sayfada
/// değilse bağlı <see cref="YenileCommand"/> çalışır. Bütün sayfalar (XAML ve kodla yazılmış) bu satırı kullanır; açıklama paragrafı yoktur.</summary>
public class SayfaBasligi : VerticalStackLayout
{
    public static readonly BindableProperty BaslikProperty = Ozellik(nameof(Baslik), typeof(string), "");
    public static readonly BindableProperty MesgulProperty = Ozellik(nameof(Mesgul), typeof(bool), false);
    public static readonly BindableProperty HataProperty = Ozellik(nameof(Hata), typeof(string), null);
    public static readonly BindableProperty HataKutusuProperty = Ozellik(nameof(HataKutusu), typeof(bool), false);
    public static readonly BindableProperty MesajProperty = Ozellik(nameof(Mesaj), typeof(string), null);
    public static readonly BindableProperty SonGuncellemeMetniProperty = Ozellik(nameof(SonGuncellemeMetni), typeof(string), null);
    public static readonly BindableProperty VeriEskiProperty = Ozellik(nameof(VeriEski), typeof(bool), false);
    public static readonly BindableProperty YenileCommandProperty = Ozellik(nameof(YenileCommand), typeof(ICommand), null);
    public static readonly BindableProperty YenileGorunurProperty = Ozellik(nameof(YenileGorunur), typeof(bool), true);
    /// <summary>Sayfanın başlıktaki tek eylemi (Alışlar'da "+ Yeni alış"): Yenile'nin solunda durur.</summary>
    public static readonly BindableProperty EylemProperty = BindableProperty.Create(nameof(Eylem), typeof(View), typeof(SayfaBasligi), null,
        propertyChanged: (s, eski, yeni) => ((SayfaBasligi)s).EylemiYerlestir(eski as View, yeni as View));

    public string Baslik { get => (string)GetValue(BaslikProperty); set => SetValue(BaslikProperty, value); }
    public bool Mesgul { get => (bool)GetValue(MesgulProperty); set => SetValue(MesgulProperty, value); }
    public string? Hata { get => (string?)GetValue(HataProperty); set => SetValue(HataProperty, value); }
    /// <summary>Hata kırmızı kutuda (ErrorBox) gösterilir; değilse düz kırmızı metin.</summary>
    public bool HataKutusu { get => (bool)GetValue(HataKutusuProperty); set => SetValue(HataKutusuProperty, value); }
    public string? Mesaj { get => (string?)GetValue(MesajProperty); set => SetValue(MesajProperty, value); }
    public string? SonGuncellemeMetni { get => (string?)GetValue(SonGuncellemeMetniProperty); set => SetValue(SonGuncellemeMetniProperty, value); }
    /// <summary>Son yükleme hata verdi, ekrandaki veri eski: "Son güncelleme" yalnız bu durumda görünür.</summary>
    public bool VeriEski { get => (bool)GetValue(VeriEskiProperty); set => SetValue(VeriEskiProperty, value); }
    public ICommand? YenileCommand { get => (ICommand?)GetValue(YenileCommandProperty); set => SetValue(YenileCommandProperty, value); }
    public bool YenileGorunur { get => (bool)GetValue(YenileGorunurProperty); set => SetValue(YenileGorunurProperty, value); }
    public View? Eylem { get => (View?)GetValue(EylemProperty); set => SetValue(EylemProperty, value); }

    /// <summary>Hata metni (sayfa onu görünür yere kaydırabilir).</summary>
    public Label HataEtiketi { get; }
    /// <summary>Başarı iletisi (kayıt ya da iptalden sonra görünür yere kaydırılır).</summary>
    public Label MesajEtiketi { get; }
    public Button YenileDugmesi { get; }

    private readonly Label _baslik;
    private readonly ActivityIndicator _gosterge;
    private readonly Label _sonGuncelleme;
    private readonly Border _hataKutusu;
    private readonly ContentView _hataYeri;
    private readonly HorizontalStackLayout _sag;

    public SayfaBasligi()
    {
        Spacing = 8;
        _baslik = new Label { Style = Stil("LblPageTitle"), VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetHeadingLevel(_baslik, SemanticHeadingLevel.Level1);
        _gosterge = new ActivityIndicator { WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center };
        _sonGuncelleme = new Label { Style = Stil("LblPageSub") };
        YenileDugmesi = new Button { Text = "Yenile", Style = Stil("BtnKucuk"), VerticalOptions = LayoutOptions.Center };
        YenileDugmesi.Clicked += async (_, _) => await YenileAsync();
        _sag = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = { _gosterge, YenileDugmesi } };
        // "Son güncelleme … güncel olmayabilir" uzundur: düğmelerin yanında değil, başlığın altındaki satırda (dar ekranda başlığı ezmez).
        var ust = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
        };
        ust.Add(_baslik);
        ust.Add(_sag, 1);
        ust.Add(_sonGuncelleme, 0, 1);
        Grid.SetColumnSpan(_sonGuncelleme, 2);
        HataEtiketi = new Label { Style = Stil("LblError") };
        _hataKutusu = new Border { Style = Stil("ErrorBox") };
        _hataYeri = new ContentView();
        MesajEtiketi = new Label { Style = Stil("LblTakipMesaj") };
        Children.Add(ust);
        Children.Add(_hataYeri);
        Children.Add(MesajEtiketi);
        Guncelle();
    }

    private static Style Stil(string anahtar) => (Style)Application.Current!.Resources[anahtar];

    private void EylemiYerlestir(View? eski, View? yeni)
    {
        if (eski is not null)
            _sag.Remove(eski);
        if (yeni is not null)
            _sag.Insert(_sag.IndexOf(YenileDugmesi), yeni);
    }

    private static BindableProperty Ozellik(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(SayfaBasligi), varsayilan, propertyChanged: (s, _, _) => ((SayfaBasligi)s).Guncelle());

    private async Task YenileAsync()
    {
        for (Element? e = Parent; e is not null; e = e.Parent)
        {
            if (e is IYenilenebilir sayfa)
            {
                await sayfa.YenileAsync();
                return;
            }
        }
        if (YenileCommand?.CanExecute(null) == true)
            YenileCommand.Execute(null);
    }

    private void Guncelle()
    {
        // Ctor sırasında özellik varsayılanları atanırken parçalar henüz yoktur.
        if (_baslik is null)
            return;
        _baslik.Text = Baslik;
        _gosterge.IsRunning = Mesgul;
        _gosterge.IsVisible = Mesgul;
        _sonGuncelleme.Text = SonGuncellemeMetni;
        _sonGuncelleme.IsVisible = VeriEski && !string.IsNullOrWhiteSpace(SonGuncellemeMetni);
        YenileDugmesi.IsVisible = YenileGorunur;
        YenileDugmesi.IsEnabled = !Mesgul;
        // Görünürlük DoluIse kuralıyla aynı: boş ya da yalnız boşluk olan metin gösterilmez.
        HataEtiketi.Text = Hata;
        var hataVar = !string.IsNullOrWhiteSpace(Hata);
        // Etiket önce eski yerinden çıkar (bir öğenin tek ebeveyni olur), sonra kutuya ya da doğrudan yerine konur.
        if (HataKutusu && _hataYeri.Content != _hataKutusu)
        {
            _hataYeri.Content = null;
            _hataKutusu.Content = HataEtiketi;
            _hataYeri.Content = _hataKutusu;
        }
        else if (!HataKutusu && _hataYeri.Content != HataEtiketi)
        {
            _hataKutusu.Content = null;
            _hataYeri.Content = HataEtiketi;
        }
        HataEtiketi.IsVisible = hataVar;
        _hataYeri.IsVisible = hataVar;
        MesajEtiketi.Text = Mesaj;
        MesajEtiketi.IsVisible = !string.IsNullOrWhiteSpace(Mesaj);
    }
}
