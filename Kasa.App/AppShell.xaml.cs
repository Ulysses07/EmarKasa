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
        var bolumler = SekmeModeli.Bolumler(_auth.AktifRol);
        AyarlarSekme.IsVisible = bolumler.Contains(Bolum.Ayarlar);
        _ = GoToAsync("//panel");
    }

    private async void CikisTiklandi(object? sender, EventArgs e)
    {
        await _auth.CikisAsync();
        AnaMenu.IsVisible = false;
        await GoToAsync("//login");
    }
}
