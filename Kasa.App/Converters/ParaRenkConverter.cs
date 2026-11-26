using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>Para rengine tek kural (docs/specs/2026-11-26-slop-temizligi.md §4): artı yeşil (Pos), eksi kırmızı (Neg), sıfır gri (Sub);
/// renkler paletten gelir. Parametre <see cref="ParaBicimConverter.Gider"/> ise tutar gider sayılır: pozitif gider kırmızı, iade (negatif
/// gider) yeşil.</summary>
public sealed class ParaRenkConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tutar = System.Convert.ToDecimal(value ?? 0m);
        if (parameter as string == ParaBicimConverter.Gider)
            tutar = -tutar;
        return (Color)Application.Current!.Resources[tutar switch { < 0 => "Neg", > 0 => "Pos", _ => "Sub" }];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
