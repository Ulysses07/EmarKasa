using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage
{
    private readonly IslemlerViewModel _vm;
    private readonly AuthViewModel _auth;

    public IslemlerPage(IslemlerViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
        _vm.OdakIstendi += OdakIstendi;   // paket C · 26: Ctrl+N, kopyala, seri giriş sonrası odak
        // Paket C · 26: Enter/Ctrl+S odak kutudan çıkmadan kaydeder; VM ekrandaki metni denetlesin.
        TutarKutusu.TextChanged += (_, e) => _vm.TutarKutusuMetni = e.NewTextValue;
        GelenTutarKutusu.TextChanged += (_, e) => _vm.GelenTutarKutusuMetni = e.NewTextValue;
        KisayollariBagla();               // paket C · 26: Windows klavye kısayolları (IslemlerPage.Kisayol.cs)
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }

    /// <summary>VM'in istediği form alanına odaklanır.</summary>
    private void OdakIstendi(object? sender, string alan)
    {
        View hedef = alan switch
        {
            IslemlerViewModel.OdakTarih => TarihSecici,
            IslemlerViewModel.OdakTutar => TutarKutusu,
            _ => CariKutusu,
        };
        Dispatcher.Dispatch(() => hedef.Focus());
    }

    /// <summary>Gelen formunun altındaki "Gelen tablosu" bağlantısı.</summary>
    private async void GelenTablosunaGit(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("//gelenler"); }
        catch (Exception) { /* gezinme sürerken: menüden gidilebilir */ }
    }

    partial void KisayollariBagla();
}
