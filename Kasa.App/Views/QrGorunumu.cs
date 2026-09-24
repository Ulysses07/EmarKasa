using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>
/// QR matrisini çizer: beyaz zemin, 4 modüllük sessiz alan, koyu modüller satır parçaları hâlinde.
/// Modül boyu tam sayıya yuvarlanır ve kenar yumuşatma kapatılır ki telefon kamerası keskin okusun.
/// </summary>
public sealed class QrGorunumu : GraphicsView
{
    public static readonly BindableProperty MatrisProperty = BindableProperty.Create(
        nameof(Matris), typeof(QrMatris), typeof(QrGorunumu), null,
        propertyChanged: (b, _, _) => ((QrGorunumu)b).Invalidate());

    public QrGorunumu()
    {
        Drawable = new Cizici(this);
        BackgroundColor = Colors.White;
    }

    public QrMatris? Matris
    {
        get => (QrMatris?)GetValue(MatrisProperty);
        set => SetValue(MatrisProperty, value);
    }

    private sealed class Cizici(QrGorunumu sahip) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF alan)
        {
            canvas.FillColor = Colors.White;
            canvas.FillRectangle(alan);
            if (sahip.Matris is not { } m) return;

            var toplam = m.Boyut + 2 * QrKodu.SessizAlan;
            var kisa = Math.Min(alan.Width, alan.Height);
            var modul = MathF.Floor(kisa / toplam);
            if (modul < 1) modul = kisa / toplam;
            var kenar = modul * toplam;
            var x0 = alan.X + (alan.Width - kenar) / 2 + QrKodu.SessizAlan * modul;
            var y0 = alan.Y + (alan.Height - kenar) / 2 + QrKodu.SessizAlan * modul;

            canvas.Antialias = false;
            canvas.FillColor = Colors.Black;
            for (var y = 0; y < m.Boyut; y++)
            {
                var x = 0;
                while (x < m.Boyut)
                {
                    if (!m[x, y]) { x++; continue; }
                    var bas = x;
                    while (x < m.Boyut && m[x, y]) x++;
                    canvas.FillRectangle(x0 + bas * modul, y0 + y * modul, (x - bas) * modul, modul);
                }
            }
        }
    }
}
