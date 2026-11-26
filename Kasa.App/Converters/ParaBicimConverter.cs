using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>decimal → tr-TR para metni (Bicim.Tl). Görünümde para etiketleri için. Parametre <see cref="Gider"/> ise tutar yönüyle
/// yazılır: gider "−1.334,44", iade (negatif gider) "+50,00", sıfır işaretsiz (ekstre penceresiyle aynı eksi işareti).</summary>
public sealed class ParaBicimConverter : IValueConverter
{
    public const string Gider = nameof(Gider);
    /// <summary>İşaretli tutar (liste satırının tutar sütunu): artı "+1.500,00", eksi "−553,15", sıfır işaretsiz.</summary>
    public const string Isaretli = nameof(Isaretli);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal d)
            return value?.ToString() ?? string.Empty;
        if (parameter as string == Isaretli)
            d = -d;
        else if (parameter as string != Gider)
            return Bicim.Tl(d);
        return d switch { > 0 => "−" + Bicim.Tl(d), < 0 => "+" + Bicim.Tl(-d), _ => Bicim.Tl(0) };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
