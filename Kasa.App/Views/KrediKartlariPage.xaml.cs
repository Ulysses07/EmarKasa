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

    /// <summary>Paket D: kartın ekstre mutabakatı sayfasına gider.</summary>
    private async void MutabakatTiklandi(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: KrediKartiGorunum k }) return;
        try { await Shell.Current.GoToAsync($"{KartMutabakatPage.Rota}?kartId={k.Id}"); }
        catch (Exception) { /* gezinme sürerken ikinci basış */ }
    }
}
