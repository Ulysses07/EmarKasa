using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>Yatay satır geniş alanda aynı kalır; sığmayan düğme ve metinler dar alanda yeni satıra geçer.</summary>
public class UyumluSatir : HorizontalStackLayout
{
    protected override ILayoutManager CreateLayoutManager() => new Yerlestirici(this);

    private sealed class Yerlestirici(UyumluSatir satir) : LayoutManager(satir)
    {
        private readonly List<(IView Alan, Rect Yer)> _yerler = [];
        private double _olculenGenislik = double.NaN, _sonYerlesimGenisligi = double.NaN;
        private Size _boyut;
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            Olc(widthConstraint);
            return _boyut;
        }

        private void Olc(double genislik)
        {
            _yerler.Clear();
            var ic = Math.Max(0, genislik - satir.Padding.HorizontalThickness);
            double x = 0, y = 0, satirYuksekligi = 0, enGenis = 0;
            foreach (var alan in satir.Where(v => v.Visibility != Visibility.Collapsed))
            {
                var boyut = alan.Measure(double.PositiveInfinity, double.PositiveInfinity);
                if (boyut.Width > ic)
                    boyut = alan.Measure(ic, double.PositiveInfinity);
                if (x > 0 && x + boyut.Width > ic)
                {
                    y += satirYuksekligi + satir.Spacing;
                    x = 0;
                    satirYuksekligi = 0;
                }
                _yerler.Add((alan, new Rect(x, y, boyut.Width, boyut.Height)));
                enGenis = Math.Max(enGenis, x + boyut.Width);
                satirYuksekligi = Math.Max(satirYuksekligi, boyut.Height);
                x += boyut.Width + satir.Spacing;
            }
            _boyut = new Size(enGenis + satir.Padding.HorizontalThickness, y + satirYuksekligi + satir.Padding.VerticalThickness);
            _olculenGenislik = genislik;
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var oncekiYukseklik = _boyut.Height;
            if (_olculenGenislik != bounds.Width)
                Olc(bounds.Width);
            foreach (var (alan, yer) in _yerler)
            {
                var satirYuksekligi = _yerler.Where(p => p.Yer.Y == yer.Y).Max(p => p.Yer.Height);
                alan.Arrange(new Rect(bounds.X + satir.Padding.Left + yer.X, bounds.Y + satir.Padding.Top + yer.Y, yer.Width, satirYuksekligi));
            }
            if (oncekiYukseklik != _boyut.Height && _sonYerlesimGenisligi != bounds.Width)
                satir.InvalidateMeasure();
            _sonYerlesimGenisligi = bounds.Width;
            return new Size(bounds.Width, _boyut.Height);
        }
    }
}
