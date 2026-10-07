using System.ComponentModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>Geniş ekranda yan yana, dar ekranda sekmelerle aynı form ve listeyi gösterir. Sekme değişimi taslağı etkilemez.</summary>
public class UyumluBolmeler : Grid
{
    public static readonly BindableProperty IlkProperty = Bolme(nameof(Ilk));
    public static readonly BindableProperty IkinciProperty = Bolme(nameof(Ikinci));
    public static readonly BindableProperty IlkBaslikProperty = Durum(nameof(IlkBaslik), typeof(string), "Liste");
    public static readonly BindableProperty IkinciBaslikProperty = Durum(nameof(IkinciBaslik), typeof(string), "Ayrıntı");
    public static readonly BindableProperty IlkKullanilabilirProperty = Durum(nameof(IlkKullanilabilir), typeof(bool), true);
    public static readonly BindableProperty IkinciKullanilabilirProperty = Durum(nameof(IkinciKullanilabilir), typeof(bool), true);
    public static readonly BindableProperty DarSeciliBolmeProperty = Durum(nameof(DarSeciliBolme), typeof(int), 0);
    public View? Ilk { get => (View?)GetValue(IlkProperty); set => SetValue(IlkProperty, value); }
    public View? Ikinci { get => (View?)GetValue(IkinciProperty); set => SetValue(IkinciProperty, value); }
    public string IlkBaslik { get => (string)GetValue(IlkBaslikProperty); set => SetValue(IlkBaslikProperty, value); }
    public string IkinciBaslik { get => (string)GetValue(IkinciBaslikProperty); set => SetValue(IkinciBaslikProperty, value); }
    public bool IlkKullanilabilir { get => (bool)GetValue(IlkKullanilabilirProperty); set => SetValue(IlkKullanilabilirProperty, value); }
    public bool IkinciKullanilabilir { get => (bool)GetValue(IkinciKullanilabilirProperty); set => SetValue(IkinciKullanilabilirProperty, value); }
    public int DarSeciliBolme { get => (int)GetValue(DarSeciliBolmeProperty); set => SetValue(DarSeciliBolmeProperty, value); }
    [TypeConverter(typeof(ColumnDefinitionCollectionTypeConverter))]
    public ColumnDefinitionCollection GenisSutunlar { get; set; } = [new(GridLength.Star), new(GridLength.Star)];
    public double DarEsik { get; set; } = 760;
    public void IlkiAc() { if (IlkKullanilabilir) DarSeciliBolme = 0; }
    public void IkinciyiAc() { if (IkinciKullanilabilir) DarSeciliBolme = 1; }

    private readonly Grid _sekmeler;
    private readonly Button _ilkDugme, _ikinciDugme;
    private readonly ColumnDefinitionCollection _darSutunlar = [new(GridLength.Star)];
    private bool _dar;
    public UyumluBolmeler()
    {
        RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)];
        RowSpacing = 12;
        _ilkDugme = new Button { MinimumHeightRequest = 44 };
        _ikinciDugme = new Button { MinimumHeightRequest = 44 };
        _ilkDugme.SetDynamicResource(StyleProperty, "BtnSecondary");
        _ikinciDugme.SetDynamicResource(StyleProperty, "BtnSecondary");
        _ilkDugme.Clicked += (_, _) => IlkiAc();
        _ikinciDugme.Clicked += (_, _) => IkinciyiAc();
        _sekmeler = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8, IsVisible = false };
        _sekmeler.Add(_ilkDugme);
        _sekmeler.Add(_ikinciDugme, 1);
        Add(_sekmeler);
    }
    private static BindableProperty Bolme(string ad) => BindableProperty.Create(ad, typeof(View), typeof(UyumluBolmeler),
        propertyChanged: (b, e, y) => ((UyumluBolmeler)b).BolmeDegisti((View?)e, (View?)y));
    private static BindableProperty Durum(string ad, Type tur, object varsayilan) => BindableProperty.Create(ad, tur, typeof(UyumluBolmeler), varsayilan,
        propertyChanged: (b, _, _) => ((UyumluBolmeler)b).Guncelle());
    private void BolmeDegisti(View? eski, View? yeni)
    {
        if (eski is not null)
            Remove(eski);
        if (yeni is not null)
            Add(yeni);
        Guncelle();
    }
    private void Guncelle()
    {
        if (_sekmeler is null)
            return;
        _ilkDugme.Text = IlkBaslik;
        _ikinciDugme.Text = IkinciBaslik;
        _sekmeler.IsVisible = _dar && IlkKullanilabilir && IkinciKullanilabilir;
        var tek = _dar || !IlkKullanilabilir || !IkinciKullanilabilir;
        ColumnDefinitions = tek ? _darSutunlar : GenisSutunlar;
        RowSpacing = _sekmeler.IsVisible ? 12 : 0;
        SetColumnSpan((BindableObject)_sekmeler, tek ? 1 : 2);
        var secili = DarSeciliBolme == 0 && IlkKullanilabilir || !IkinciKullanilabilir ? 0 : 1;
        _ilkDugme.FontAttributes = secili == 0 ? FontAttributes.Bold : FontAttributes.None;
        _ikinciDugme.FontAttributes = secili == 1 ? FontAttributes.Bold : FontAttributes.None;
        SemanticProperties.SetDescription(_ilkDugme, IlkBaslik + (secili == 0 ? " (açık)" : ""));
        SemanticProperties.SetDescription(_ikinciDugme, IkinciBaslik + (secili == 1 ? " (açık)" : ""));
        if (Ilk is { } ilk)
        {
            SetRow((BindableObject)ilk, 1);
            SetColumn((BindableObject)ilk, 0);
            ilk.IsVisible = IlkKullanilabilir && (!_dar || secili == 0);
        }
        if (Ikinci is { } ikinci)
        {
            SetRow((BindableObject)ikinci, 1);
            SetColumn((BindableObject)ikinci, tek ? 0 : 1);
            ikinci.IsVisible = IkinciKullanilabilir && (!_dar || secili == 1);
        }
    }
    private bool Uyarla(double genislik)
    {
        if (!double.IsFinite(genislik))
            return false;
        var dar = genislik - Padding.HorizontalThickness < DarEsik;
        var sutunlar = dar || !IlkKullanilabilir || !IkinciKullanilabilir ? _darSutunlar : GenisSutunlar;
        if (_dar == dar && ColumnDefinitions == sutunlar)
            return false;
        _dar = dar;
        ColumnDefinitions = sutunlar;
        Guncelle();
        return true;
    }
    protected override ILayoutManager CreateLayoutManager() => new Yerlestirici(this);
    private sealed class Yerlestirici(UyumluBolmeler bolmeler) : GridLayoutManager(bolmeler)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            bolmeler.Uyarla(widthConstraint);
            return base.Measure(widthConstraint, heightConstraint);
        }
        public override Size ArrangeChildren(Rect bounds)
        {
            if (bolmeler.Uyarla(bounds.Width))
                base.Measure(bounds.Width, bounds.Height);
            return base.ArrangeChildren(bounds);
        }
    }
}
