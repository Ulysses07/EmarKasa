using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Sorular sayfası (her iki rol). Satırdan "Soru sor" ile gelindiyse form o kayıtla açılır.</summary>
public partial class SorularPage : ContentPage
{
    private readonly SorularViewModel _vm;
    private readonly AuthViewModel _auth;

    public SorularPage(SorularViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Cevap / kapat / sil düğmeleri yalnız editörde.
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
