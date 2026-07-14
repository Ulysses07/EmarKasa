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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
