using System.ComponentModel;
using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>
/// Paket B · dönem ayrıntısı listenin üstünde açılır; liste eskiden yeniye olduğundan son haftalardan
/// birine dokunan kullanıcı onu görmezdi. Ayrıntı açılınca sayfa başa kayar, kapanınca listedeki
/// eski konuma dönülür.
/// </summary>
public partial class HaftalikPage
{
    /// <summary>Ayrıntı açılmadan önceki liste konumu (kapatınca oraya dönülür).</summary>
    private double? _listeKonumu;

    private void DetayDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HaftalikViewModel.DetayVar)) return;
        if (_vm.DetayVar)
        {
            _listeKonumu ??= Kaydirma.ScrollY;
            Dispatcher.Dispatch(async () => await Kaydirma.ScrollToAsync(0, 0, true));
        }
        else if (_listeKonumu is { } y)
        {
            _listeKonumu = null;
            // Kart gizlendikten sonra (yerleşim güncellenince) eski konuma dön.
            Dispatcher.Dispatch(async () => await Kaydirma.ScrollToAsync(0, y, false));
        }
    }
}
