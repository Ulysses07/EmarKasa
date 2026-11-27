using Kasa.App.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Kasa.App.WinUI;

/// <summary>Pencere kökünde F5, Ctrl+N, Ctrl+S, Ctrl+F (docs/specs/2026-11-26-slop-temizligi.md §8). Kök içerik PreviewKeyDown ile
/// dinlenir: metin kutusu odaktayken de çalışır. Hedef en üstteki açık pencere (ekstre hareket penceresi) ya da açık sayfadır.</summary>
internal static class KlavyeKisayollari
{
    public static void Bagla(Microsoft.UI.Xaml.Window pencere)
    {
        // Pencere oluşturulduğunda içerik henüz atanmamıştır; ilk etkinleşmede kök içeriğe bir kez bağlanılır.
        var bagli = false;
        pencere.Activated += (_, _) =>
        {
            if (bagli || pencere.Content is not UIElement kok)
                return;
            bagli = true;
            kok.PreviewKeyDown += TusaBasildi;
        };
    }

    private static void TusaBasildi(object sender, KeyRoutedEventArgs e)
    {
        // Basılı tutulan tuşun tekrarı ikinci kaydı başlatmaz.
        if (e.KeyStatus.WasKeyDown || Coz(e.Key) is not { } kisayol || Shell.Current is not { } kabuk)
            return;
        e.Handled = Kisayollar.Uygula(kabuk.Navigation.ModalStack.LastOrDefault() ?? kabuk.CurrentPage, kisayol);
    }

    private static Kisayol? Coz(VirtualKey tus)
    {
        if (tus == VirtualKey.F5)
            return Kisayol.Yenile;
        if (!InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            return null;
        return tus switch { VirtualKey.N => Kisayol.Yeni, VirtualKey.S => Kisayol.Kaydet, VirtualKey.F => Kisayol.Ara, _ => null };
    }
}
