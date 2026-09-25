using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;
    private bool _giriseDonuluyor;

    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
        _auth.OturumSonlandi += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await GiriseDonAsync());
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi) MenuyuAc();
        else await GiriseDonAsync();
    }

    public void MenuyuAc()
    {
        var bolumler = SekmeModeli.Bolumler(_auth.AktifRol);
        PanelItem.IsVisible = bolumler.Contains(Bolum.Panel);
        HaftalikItem.IsVisible = bolumler.Contains(Bolum.Haftalik);
        AylikItem.IsVisible = bolumler.Contains(Bolum.Aylik);
        IslemlerItem.IsVisible = bolumler.Contains(Bolum.Islemler);
        AylikGiderlerItem.IsVisible = bolumler.Contains(Bolum.AylikGiderler);
        AyarlarItem.IsVisible = bolumler.Contains(Bolum.Ayarlar);
        AlislarItem.IsVisible = bolumler.Contains(Bolum.Alislar);
        DisariAktarItem.IsVisible = bolumler.Contains(Bolum.DisariAktar);
        KartlarItem.IsVisible = bolumler.Contains(Bolum.Kartlar);
        KredilerItem.IsVisible = bolumler.Contains(Bolum.Krediler);
        BildirimlerItem.IsVisible = bolumler.Contains(Bolum.Bildirimler);
        EkstreAktarItem.IsVisible = bolumler.Contains(Bolum.EkstreAktar);
        _ = GoToAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }

    private async void CikisTiklandi(object? sender, EventArgs e)
    {
        await _auth.CikisAsync();
        await GiriseDonAsync();
    }

    private async Task GiriseDonAsync()
    {
        if (_giriseDonuluyor) return;
        _giriseDonuluyor = true;
        try
        {
            PanelItem.IsVisible = false;
            HaftalikItem.IsVisible = false;
            AylikItem.IsVisible = false;
            IslemlerItem.IsVisible = false;
            AylikGiderlerItem.IsVisible = false;
            AyarlarItem.IsVisible = false;
            AlislarItem.IsVisible = false;
            DisariAktarItem.IsVisible = false;
            KartlarItem.IsVisible = false;
            KredilerItem.IsVisible = false;
            BildirimlerItem.IsVisible = false;
            EkstreAktarItem.IsVisible = false;
            await GoToAsync("//login");
        }
        finally { _giriseDonuluyor = false; }
    }
}
