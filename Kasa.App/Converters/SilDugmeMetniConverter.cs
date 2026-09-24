using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>
/// Satırdaki Sil düğmesinin metni (paket C · 32). Değerler: [satırın kaydı, Silme.Bekleyen,
/// Silme.OnayDugmesi]; bu satır ikinci basışı bekliyorsa "Emin misiniz?", değilse "Sil"
/// (ya da parametre). Karar <see cref="SilmeOnayi.DugmeMetni"/>'nde (testli).
/// </summary>
public sealed class SilDugmeMetniConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var varsayilan = parameter as string ?? "Sil";
        if (values is not { Length: >= 3 }) return varsayilan;
        return SilmeOnayi.DugmeMetni(values[0], values[1], values[2] as string, varsayilan);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
