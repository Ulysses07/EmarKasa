using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>Entry.Text ↔ decimal iki-yönlü. Görünümde tr-TR ondalık (virgül); girişte
/// virgül/nokta/binlik ayırıcı toleranslı ayrıştırma. Boş = 0.</summary>
public sealed class ParaGirisConverter : IValueConverter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value is decimal m ? m : 0m;
        return d == 0m ? string.Empty : d.ToString(parameter as string ?? "0.##", Tr);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().Replace(" ", "").Replace("₺", "");
        if (s.Length == 0) return 0m;

        var hasComma = s.Contains(',');
        var hasDot = s.Contains('.');
        if (hasComma && hasDot) s = s.Replace(".", "").Replace(',', '.'); // nokta=binlik, virgül=ondalık
        else if (hasComma) s = s.Replace(',', '.');

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
