using System.ComponentModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>Dar alanda alanları satırlara taşır; geniş alanda özgün sütunları ve denetimleri korur.</summary>
public class UyumluIzgara : Grid
{
    public static readonly BindableProperty DarSatirProperty = BindableProperty.CreateAttached("DarSatir", typeof(int), typeof(UyumluIzgara), -1);
    public static readonly BindableProperty DarSutunProperty = BindableProperty.CreateAttached("DarSutun", typeof(int), typeof(UyumluIzgara), 0);
    public static readonly BindableProperty DarSutunAraligiProperty = BindableProperty.CreateAttached("DarSutunAraligi", typeof(int), typeof(UyumluIzgara), 1);
    [TypeConverter(typeof(ColumnDefinitionCollectionTypeConverter))]
    public ColumnDefinitionCollection DarSutunlar { get; set; } = [new(GridLength.Star)];
    public double DarEsik { get; set; } = 600;
    public static int GetDarSatir(BindableObject view) => (int)view.GetValue(DarSatirProperty);
    public static void SetDarSatir(BindableObject view, int value) => view.SetValue(DarSatirProperty, value);
    public static int GetDarSutun(BindableObject view) => (int)view.GetValue(DarSutunProperty);
    public static void SetDarSutun(BindableObject view, int value) => view.SetValue(DarSutunProperty, value);
    public static int GetDarSutunAraligi(BindableObject view) => (int)view.GetValue(DarSutunAraligiProperty);
    public static void SetDarSutunAraligi(BindableObject view, int value) => view.SetValue(DarSutunAraligiProperty, value);

    private ColumnDefinitionCollection? _genisSutunlar;
    private RowDefinitionCollection? _genisSatirlar;
    private readonly Dictionary<BindableObject, (int Satir, int Sutun, int SatirAraligi, int SutunAraligi)> _genisYerler = [];
    private bool? _dar;

    private bool Uyarla(double genislik)
    {
        if (!double.IsFinite(genislik))
            return false;
        var dar = genislik - Padding.HorizontalThickness < DarEsik;
        if (_dar == dar && Children.All(c => c is BindableObject b && _genisYerler.ContainsKey(b)))
            return false;
        _genisSutunlar ??= ColumnDefinitions;
        _genisSatirlar ??= RowDefinitions;
        foreach (var alan in Children.OfType<BindableObject>())
            _genisYerler.TryAdd(alan, (GetRow(alan), GetColumn(alan), GetRowSpan(alan), GetColumnSpan(alan)));
        if (dar)
        {
            ColumnDefinitions = DarSutunlar;
            var satirSayisi = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                if (Children[i] is not BindableObject alan)
                    continue;
                var satir = GetDarSatir(alan) is var belirtilen && belirtilen >= 0 ? belirtilen : i;
                SetRow(alan, satir);
                SetColumn(alan, GetDarSutun(alan));
                SetRowSpan(alan, 1);
                SetColumnSpan(alan, GetDarSutunAraligi(alan));
                satirSayisi = Math.Max(satirSayisi, satir + 1);
            }
            RowDefinitions = new RowDefinitionCollection();
            for (var i = 0; i < satirSayisi; i++)
                RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        else
        {
            ColumnDefinitions = _genisSutunlar;
            RowDefinitions = _genisSatirlar;
            foreach (var (alan, yer) in _genisYerler)
            {
                SetRow(alan, yer.Satir);
                SetColumn(alan, yer.Sutun);
                SetRowSpan(alan, yer.SatirAraligi);
                SetColumnSpan(alan, yer.SutunAraligi);
            }
        }
        _dar = dar;
        return true;
    }

    protected override ILayoutManager CreateLayoutManager() => new Yerlestirici(this);
    private sealed class Yerlestirici(UyumluIzgara izgara) : GridLayoutManager(izgara)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            izgara.Uyarla(widthConstraint);
            return base.Measure(widthConstraint, heightConstraint);
        }
        public override Size ArrangeChildren(Rect bounds)
        {
            if (izgara.Uyarla(bounds.Width))
                base.Measure(bounds.Width, bounds.Height);
            return base.ArrangeChildren(bounds);
        }
    }
}
