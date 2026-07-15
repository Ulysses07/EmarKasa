using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>Bool'u tersler (Mesgul iken Giriş butonunu kilitlemek için).</summary>
public sealed class TersIseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(value is bool b && b);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(value is bool b && b);
}
