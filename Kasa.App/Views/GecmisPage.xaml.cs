using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class GecmisPage : ContentPage
{
    private readonly GecmisViewModel _vm;
    private readonly AuthViewModel _auth;

    public GecmisPage(GecmisViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // "Geri al" yalnız editörde
        await _vm.YukleAsync();
    }
}
