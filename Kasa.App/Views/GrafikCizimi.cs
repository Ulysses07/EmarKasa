using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>
/// 04 · Çubuk grafik: her ay için geçen yıl (açık) ve bu yıl (koyu) yan yana. Eksi değer sıfır
/// çizgisinin altına iner. Kur/endeksi olmayan ay çizilmez, yerine "kur yok" yazılır; takip öncesi
/// ay boş kalır. Ölçek ve veriler GrafiklerViewModel'den (GrafikVerisi) gelir.
/// </summary>
public sealed class GrafikCizimi : IDrawable
{
    private static readonly Color BuYil = Color.FromArgb("#1E5F46");
    private static readonly Color BuYilEksi = Color.FromArgb("#C13A2E");
    private static readonly Color GecenYil = Color.FromArgb("#B9C7BD");
    private static readonly Color Cizgi = Color.FromArgb("#E3E0D6");
    private static readonly Color Yazi = Color.FromArgb("#6F7566");
    private static readonly Color Soluk = Color.FromArgb("#9AA08F");
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private const float Sol = 84f, Sag = 8f, Ust = 12f, Alt = 26f;

    private readonly GrafiklerViewModel _vm;

    public GrafikCizimi(GrafiklerViewModel vm) => _vm = vm;

    public void Draw(ICanvas canvas, RectF alan)
    {
        var cubuklar = _vm.Cubuklar;
        float genislik = alan.Width - Sol - Sag, yukseklik = alan.Height - Ust - Alt;
        if (cubuklar.Count == 0 || genislik <= 0 || yukseklik <= 0) return;

        decimal enKucuk = _vm.EnKucuk, enBuyuk = _vm.EnBuyuk;
        float Y(decimal d) => alan.Top + Ust + yukseklik * (1f - (float)GrafikVerisi.Oran(d, enKucuk, enBuyuk));
        float sifir = Y(0m);

        // Eksen: en büyük, sıfır, en küçük.
        canvas.FontSize = 10;
        canvas.FontColor = Yazi;
        canvas.StrokeColor = Cizgi;
        canvas.StrokeSize = 1;
        foreach (var d in new[] { enBuyuk, 0m, enKucuk }.Distinct())
        {
            var y = Y(d);
            canvas.DrawLine(alan.Left + Sol, y, alan.Right - Sag, y);
            canvas.DrawString(d.ToString("#,##0", Tr), alan.Left, y - 7, Sol - 8, 14,
                Microsoft.Maui.Graphics.HorizontalAlignment.Right, Microsoft.Maui.Graphics.VerticalAlignment.Center);
        }

        float grup = genislik / cubuklar.Count;
        float cubuk = Math.Max(2f, grup * 0.32f);
        for (int i = 0; i < cubuklar.Count; i++)
        {
            var c = cubuklar[i];
            float x = alan.Left + Sol + i * grup;
            float orta = x + grup / 2f;

            Cubuk(canvas, c.GecenYil, orta - cubuk - 1f, cubuk, GecenYil, GecenYil);
            Cubuk(canvas, c.Deger, orta + 1f, cubuk, BuYil, BuYilEksi);

            if (c.KurYok || c.GecenYilKurYok)
            {
                canvas.FontSize = 9;
                canvas.FontColor = Soluk;
                canvas.DrawString(GrafikVerisi.KurYokMetni, x, sifir - 14, grup, 12,
                    Microsoft.Maui.Graphics.HorizontalAlignment.Center, Microsoft.Maui.Graphics.VerticalAlignment.Center);
            }

            canvas.FontSize = 10;
            canvas.FontColor = Yazi;
            canvas.DrawString(c.Etiket, x, alan.Bottom - Alt + 6, grup, 14,
                Microsoft.Maui.Graphics.HorizontalAlignment.Center, Microsoft.Maui.Graphics.VerticalAlignment.Center);
        }

        void Cubuk(ICanvas k, decimal? deger, float sol, float en, Color arti, Color eksi)
        {
            if (deger is not { } d) return;
            var y = Y(d);
            k.FillColor = d < 0 ? eksi : arti;
            k.FillRectangle(sol, Math.Min(y, sifir), en, Math.Max(1f, Math.Abs(sifir - y)));
        }
    }
}
