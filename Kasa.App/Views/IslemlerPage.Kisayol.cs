#if WINDOWS
using Kasa.App.Core;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using WinHizlandirici = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using WinUIElement = Microsoft.UI.Xaml.UIElement;

namespace Kasa.App.Views;

// Paket C · 26: İşlemler sayfasının Windows klavye kısayolları. Tuş → eylem eşlemesi ve eylemin
// kendisi App.Core'da (KlavyeKisayollari, IslemlerViewModel.KisayolCalistir; testli); burada yalnız
// WinUI olayları bağlanır. Enter form kutularında ReturnCommand ile çalışır (XAML).
public partial class IslemlerPage
{
    private bool _hizlandiricilarEklendi;
    private WinUIElement? _tarihBagli, _gelenTarihBagli;

    partial void KisayollariBagla()
    {
        HandlerChanged += (_, _) =>
        {
            if (Handler?.PlatformView is WinUIElement kok) HizlandiricilariEkle(kok);
        };
        TarihSecici.HandlerChanged += (_, _) => TarihTusuBagla(TarihSecici, ref _tarihBagli, _vm.TarihKisayolu);
        GelenTarihSecici.HandlerChanged += (_, _) => TarihTusuBagla(GelenTarihSecici, ref _gelenTarihBagli, _vm.GelenTarihKisayolu);
    }

    /// <summary>Ctrl+S, Ctrl+N, Esc, Alt+1…4: sayfa kökünde gizli klavye hızlandırıcıları.</summary>
    private void HizlandiricilariEkle(WinUIElement kok)
    {
        if (_hizlandiricilarEklendi) return;
        _hizlandiricilarEklendi = true;
        kok.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        foreach (var k in KlavyeKisayollari.Liste)
        {
            if (k.Tus == "Enter") continue;
            var h = new WinHizlandirici
            {
                Key = Tus(k.Tus),
                Modifiers = (k.Ctrl ? VirtualKeyModifiers.Control : VirtualKeyModifiers.None)
                            | (k.Alt ? VirtualKeyModifiers.Menu : VirtualKeyModifiers.None),
            };
            var eylem = k.Eylem;
            h.Invoked += (_, a) => a.Handled = _vm.KisayolCalistir(eylem);   // işlenmezse tuş normal akar
            kok.KeyboardAccelerators.Add(h);
        }
    }

    private static VirtualKey Tus(string ad)
        => ad.Length == 1 && char.IsAsciiDigit(ad[0])
            ? VirtualKey.Number0 + (ad[0] - '0')
            : Enum.Parse<VirtualKey>(ad);

    /// <summary>Tarih kutusu odaktayken B (bugün) / D (dün); Ctrl/Alt basılıysa dokunmaz.</summary>
    private static void TarihTusuBagla(DatePicker secici, ref WinUIElement? bagli, Func<string?, bool> uygula)
    {
        if (secici.Handler?.PlatformView is not WinUIElement el || ReferenceEquals(el, bagli)) return;
        bagli = el;
        el.PreviewKeyDown += (_, e) =>
        {
            if (Basili(VirtualKey.Control) || Basili(VirtualKey.Menu)) return;
            if (uygula(e.Key.ToString())) e.Handled = true;
        };
    }

    private static bool Basili(VirtualKey tus)
        => InputKeyboardSource.GetKeyStateForCurrentThread(tus).HasFlag(CoreVirtualKeyStates.Down);
}
#endif
