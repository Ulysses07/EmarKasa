using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Excel'den toplu yükleme (paket C · 16; yalnız editör). Mantık TopluGirisViewModel'de.</summary>
public partial class TopluGirisPage : ContentPage
{
    private readonly TopluGirisViewModel _vm;
    private readonly AuthViewModel _auth;

    public TopluGirisPage(TopluGirisViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
