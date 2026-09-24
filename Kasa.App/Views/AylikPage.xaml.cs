using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage
{
    private readonly AylikViewModel _vm;
    private readonly AuthViewModel _auth;

    public AylikPage(AylikViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // ay kilidi / yayını yalnız editörde
        await _vm.YukleAsync();
    }
}
