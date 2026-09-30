using System.Windows.Input;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Kasa.App.Controls;

/// <summary>Kredi kartı kutusu (Kartlar ekranı, tasarım 2026-09-30 §2): banka rengindeki zeminde kart adı, "Kart borcu" ve borç,
/// limit doluluk çubuğu, limit ve ilk açık ekstrenin son ödemesi, en çok iki durum etiketi. İçerik bağlamdaki
/// <see cref="KartTakipSatiri"/>'ndan gelir; satır değişince KartIzgarasi kutuyu yeniden kurar. Tıklama ve klavye yüzeyi alttaki
/// şeffaf düğmedir (SeffafDugme, menü öğesiyle aynı desen): Tab ile odaklanır, UI Otomasyonu'nda "{kart adı} kartı" adlı
/// düğmedir, açık kutu "Ayrıntısı açık" ipucuyla bildirilir; yazılar girdiyi geçirir ve erişilebilirlik ağacında ayrı öğe
/// değildir. Zemin <c>BackgroundColor</c>'dır (Background fırçası yazılmaz).</summary>
public class KartKutusu : Border
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(KartKutusu),
        propertyChanged: (b, _, y) => ((KartKutusu)b)._dugme.Command = (ICommand?)y);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(KartKutusu),
        propertyChanged: (b, _, y) => ((KartKutusu)b)._dugme.CommandParameter = y);

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>Açık kutunun düğmesine yazılan UI Otomasyonu ipucu.</summary>
    public const string SeciliIpucu = "Ayrıntısı açık";

    private readonly Button _dugme;
    private readonly Label _ad, _borcEtiketi, _borc, _limit, _sonOdeme;
    private readonly Grid _cubuk;
    private readonly BoxView _iz, _dolu;
    private readonly HorizontalStackLayout _etiketler;
    private Color? _kenar, _yazi;
    private bool _secili;

    public KartKutusu()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 14 };
        StrokeThickness = 1;
        _dugme = new Button { Style = (Style)Application.Current!.Resources["SeffafDugme"] };
        _ad = Yazi(new Label { FontAttributes = FontAttributes.Bold, FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation });
        _borcEtiketi = Yazi(new Label { Text = "Kart borcu", FontSize = 12 });
        _borc = Yazi(new Label { FontAttributes = FontAttributes.Bold, FontSize = 22 });
        _iz = new BoxView { HeightRequest = 6, CornerRadius = 3 };
        _dolu = new BoxView { HeightRequest = 6, CornerRadius = 3 };
        _cubuk = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, Margin = new Thickness(0, 2) };
        _cubuk.Add(_iz);
        Grid.SetColumnSpan(_iz, 2);
        _cubuk.Add(_dolu);
        _limit = Yazi(new Label { FontSize = 12 });
        _sonOdeme = Yazi(new Label { FontSize = 12, HorizontalTextAlignment = TextAlignment.End });
        var alt = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        alt.Add(_limit);
        alt.Add(_sonOdeme, 1);
        _etiketler = new HorizontalStackLayout { Spacing = 6 };
        var icerik = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14),
            Spacing = 6,
            InputTransparent = true,
            Children = { _ad, _borcEtiketi, _borc, _cubuk, alt, _etiketler },
        };
        var kok = new Grid();
        kok.Add(_dugme);
        kok.Add(icerik);
        Content = kok;
    }

    /// <summary>Kutunun gösterdiği satır (bağlam).</summary>
    public KartTakipSatiri? Satir { get; private set; }

    /// <summary>Açık kartın kutusu: kenar kalınlaşır ve yazı rengini alır.</summary>
    public bool Secili
    {
        get => _secili;
        set
        {
            _secili = value;
            Cerceve();
        }
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is KartTakipSatiri satir)
            Doldur(satir);
    }

    private void Doldur(KartTakipSatiri satir)
    {
        Satir = satir;
        BackgroundColor = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Zemin));
        _kenar = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Kenar));
        _yazi = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Yazi));
        foreach (var etiket in new[] { _ad, _borcEtiketi, _borc, _limit, _sonOdeme })
            etiket.TextColor = _yazi;
        _ad.Text = satir.Veri.Ad;
        _borc.Text = satir.BorcMetni;
        _limit.Text = satir.LimitMetni;
        _sonOdeme.Text = satir.SonOdemeMetni;
        _sonOdeme.IsVisible = satir.SonOdemeMetni is not null;
        var oran = satir.Doluluk ?? 0;
        _cubuk.IsVisible = satir.DolulukVar;
        _cubuk.ColumnDefinitions[0].Width = new GridLength(oran, GridUnitType.Star);
        _cubuk.ColumnDefinitions[1].Width = new GridLength(1 - oran, GridUnitType.Star);
        _iz.Color = _kenar;
        _dolu.Color = _yazi;
        _etiketler.Clear();
        foreach (var etiket in satir.Etiketler)
            _etiketler.Add(Etiket(etiket));
        _etiketler.IsVisible = satir.Etiketler.Count > 0;
        SemanticProperties.SetDescription(_dugme, $"{satir.Veri.Ad} kartı");
        Cerceve();
    }

    private void Cerceve()
    {
        if (_secili)
            SemanticProperties.SetHint(_dugme, SeciliIpucu);
        else
            _dugme.ClearValue(SemanticProperties.HintProperty);
        if (_kenar is null || _yazi is null)
            return;
        Stroke = new SolidColorBrush(_secili ? _yazi : _kenar);
        StrokeThickness = _secili ? 2.5 : 1;
    }

    /// <summary>Durum etiketi (StatusChip): tehlike NegSoft/Neg, uyarı UyariZemin/UyariMetin, nötr ChipBg/Ink (kontrast testli).</summary>
    private static Border Etiket(KartEtiketi etiket)
    {
        var (zemin, yazi) = etiket.Tur switch
        {
            KartEtiketTuru.Tehlike => ((Color)Application.Current!.Resources["NegSoft"], (Color)Application.Current!.Resources["Neg"]),
            KartEtiketTuru.Uyari => ((Color)Application.Current!.Resources["UyariZemin"], (Color)Application.Current!.Resources["UyariMetin"]),
            _ => ((Color)Application.Current!.Resources["ChipBg"], (Color)Application.Current!.Resources["Ink"]),
        };
        var cip = new Border
        {
            Style = (Style)Application.Current!.Resources["StatusChip"],
            BackgroundColor = zemin,
            Content = Yazi(new Label { Text = etiket.Metin, Style = (Style)Application.Current!.Resources["LblStatusChip"], TextColor = yazi }),
        };
        AutomationProperties.SetIsInAccessibleTree(cip, false);
        return cip;
    }

    /// <summary>Kutunun üstündeki yazı: girdiyi alttaki düğmeye geçirir, erişilebilirlik ağacında ayrı öğe değildir.</summary>
    private static Label Yazi(Label etiket)
    {
        etiket.InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(etiket, false);
        return etiket;
    }

    /// <summary>Renk ailesi anahtarı (KartRengi.Anahtar); anahtarların varlığı ve kontrastı Kasa.App.Core.Tests'te sınanır.</summary>
    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];
}
