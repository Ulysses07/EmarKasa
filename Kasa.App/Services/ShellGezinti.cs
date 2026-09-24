using Kasa.App.Core;

namespace Kasa.App.Services;

/// <summary>Görünüm modellerinin gezinme isteğini Shell'e iletir (ana iş parçacığında).</summary>
public sealed class ShellGezinti : IGezinti
{
    public Task GitAsync(string rota)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is { } shell) await shell.GoToAsync(rota);
        });
}
