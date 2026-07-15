using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>Metnin baş harfini (TR büyük harf) avatar rozetinde göstermek için döndürür.</summary>
public sealed class BasHarfConverter : IValueConverter
{
    static readonly CultureInfo Tr = new("tr-TR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && s.Length > 0 ? s.Substring(0, 1).ToUpper(Tr) : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
