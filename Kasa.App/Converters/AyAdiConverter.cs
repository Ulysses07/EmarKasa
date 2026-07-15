using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>Ay numarasını (1–12) Türkçe ay adına çevirir (örn. 7 → "Temmuz").</summary>
public sealed class AyAdiConverter : IValueConverter
{
    static readonly CultureInfo Tr = new("tr-TR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ay = System.Convert.ToInt32(value ?? 0);
        if (ay < 1 || ay > 12) return "";
        var ad = Tr.DateTimeFormat.GetMonthName(ay);
        return Tr.TextInfo.ToTitleCase(ad);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
