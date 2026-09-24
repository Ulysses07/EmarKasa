using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>Paket F: işlem listesinde belge özeti ("e-Fatura · F-12 · fatura bekleniyor"; boşsa "").</summary>
public sealed class BelgeOzetiConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BelgeBilgisi b ? BelgeMetin.Ozet(b) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
