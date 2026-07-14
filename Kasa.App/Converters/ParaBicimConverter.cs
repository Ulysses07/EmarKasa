using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>decimal → tr-TR para metni (Bicim.Tl). Görünümde para etiketleri için.</summary>
public sealed class ParaBicimConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? Bicim.Tl(d) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
