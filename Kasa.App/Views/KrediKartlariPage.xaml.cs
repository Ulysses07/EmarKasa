using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class KrediKartlariPage : ContentPage
{
    private readonly KrediKartlariViewModel _vm;
    private readonly AuthViewModel _auth;

    public KrediKartlariPage(KrediKartlariViewModel vm, AuthViewModel auth)
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
