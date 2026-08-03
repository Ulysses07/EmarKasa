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
        var bolumler = SekmeModeli.Bolumler(_auth.AktifRol);
        PanelItem.IsVisible = true;
        HaftalikItem.IsVisible = true;
        AylikItem.IsVisible = true;
        CarilerItem.IsVisible = true;
        IslemlerItem.IsVisible = true;
        KartlarItem.IsVisible = true;
        KredilerItem.IsVisible = true;
        AyarlarItem.IsVisible = bolumler.Contains(Bolum.Ayarlar);
        _ = GoToAsync("//panel");
    }

    private async void CikisTiklandi(object? sender, EventArgs e)
    {
        await _auth.CikisAsync();
        PanelItem.IsVisible = false;
        HaftalikItem.IsVisible = false;
        AylikItem.IsVisible = false;
        CarilerItem.IsVisible = false;
        IslemlerItem.IsVisible = false;
        KartlarItem.IsVisible = false;
        KredilerItem.IsVisible = false;
        AyarlarItem.IsVisible = false;
        await GoToAsync("//login");
    }
}
