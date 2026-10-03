using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>İki bağlı değer eşit ve boş değilse true (düzenlenen satırın listede vurgusu: satırın Id'si ile formun DuzenId'si).</summary>
public sealed class EsitIseConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => values is [{ } ilk, { } ikinci] && Equals(ilk, ikinci);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
