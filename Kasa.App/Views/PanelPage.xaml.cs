using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage
{
    private readonly PanelViewModel _vm;
    private readonly AuthViewModel _auth;

    public PanelPage(PanelViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Bekleyen giderler kartı yalnız editöre yüklenir.
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
