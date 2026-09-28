using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>Belge satırının yükleyen ve kaldırma bilgisi (<see cref="AlislarViewModel.BelgeAciklamasi"/>).</summary>
public sealed class BelgeAciklamasiConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BelgeDto belge ? AlislarViewModel.BelgeAciklamasi(belge) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
