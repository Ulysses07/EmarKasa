using System.Globalization;
using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Converters;

/// <summary>DonemDto → "13 Tem – 19 Tem" aralık metni (dönem picker öğeleri için). ConverterParameter="yil" ile
/// yıllı etiket ("13 Tem 2026 – 19 Tem 2026"): gelir formu dönem toplamını yerine koyar, yıllar karışmamalı.</summary>
public sealed class DonemBicimConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DonemDto d
            ? Bicim.Donem(d, yilli: parameter is "yil")
            : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
