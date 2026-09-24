using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class KasaSayimiPage : ContentPage
{
    private readonly KasaSayimiViewModel _vm;
    private readonly AuthViewModel _auth;

    public KasaSayimiPage(KasaSayimiViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // izleyici yalnız geçmişi görür
        await _vm.YukleAsync();
    }
}
