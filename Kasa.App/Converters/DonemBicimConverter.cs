using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Converters;

/// <summary>DonemDto → "13 Tem – 19 Tem" aralık metni (dönem picker öğeleri için).</summary>
public sealed class DonemBicimConverter : IValueConverter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DonemDto d
            ? $"{d.Start.ToString("dd MMM", Tr)} – {d.End.ToString("dd MMM", Tr)}"
            : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
