using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>Negatif tutar kırmızı, aksi halde pozitif yeşil (para etiketleri için).</summary>
public sealed class ParaRenkConverter : IValueConverter
{
    static readonly Color Pos = Color.FromArgb("#1B7A4E");
    static readonly Color Neg = Color.FromArgb("#C13A2E");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Convert.ToDecimal(value ?? 0m) < 0 ? Neg : Pos;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
