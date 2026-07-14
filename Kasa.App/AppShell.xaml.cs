using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;

    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi) MenuyuAc();
        else await GoToAsync("//login");
    }

    public void MenuyuAc()
    {
        AnaMenu.IsVisible = true;
        _ = GoToAsync("//panel");
    }
}
