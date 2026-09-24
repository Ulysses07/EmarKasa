using Kasa.App.Core;

namespace Kasa.App.Services;

/// <summary>Sistem panosu (MAUI Clipboard); pano UI iş parçacığından yazılır.</summary>
public sealed class MauiPano : IPano
{
    public Task YazAsync(string metin)
        => MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(metin));
}
