using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PosPage : ContentPage
{
    private readonly PosViewModel _vm;
    private readonly AuthViewModel _auth;

    public PosPage(PosViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // izleyici yalnız görür
        await _vm.YukleAsync();
    }
}
