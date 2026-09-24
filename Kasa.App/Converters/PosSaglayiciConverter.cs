using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>Paket F: POS sağlayıcısını Türkçe adına çevirir (BankaPosu → "Banka POS'u").</summary>
public sealed class PosSaglayiciConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is PosSaglayici s ? PosViewModel.SaglayiciAdi(s) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
