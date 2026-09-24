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
        _vm.GitIstendi += Git;   // Paket A: durum kartı / yapılacak satırı dokunuşu
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Bekleyen giderler kartı yalnız editöre yüklenir.
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }

    /// <summary>Panel içi hedef (bekleyen giderler kartı) kaydırılır; diğerleri Shell rotasıdır.</summary>
    private async void Git(object? sender, string hedef)
    {
        try
        {
            if (hedef == YapilacakListesi.BekleyenKarti)
                await Kaydirici.ScrollToAsync(BekleyenKart, ScrollToPosition.Start, true);
            else
                await Shell.Current.GoToAsync(hedef);
        }
        catch (Exception) { /* gezinme sürerken / geçersiz rota: menüden gidilebilir */ }
    }
}
