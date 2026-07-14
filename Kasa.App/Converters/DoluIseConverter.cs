using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>string null/boş değilse true (hata etiketini yalnız doluyken göster).</summary>
public sealed class DoluIseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
