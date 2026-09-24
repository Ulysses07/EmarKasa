using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage
{
    private readonly PanelViewModel _vm;
    private readonly AuthViewModel _auth;
    private readonly AcikSorularViewModel _sorular;
    private readonly RiskKartiViewModel _risk;

    public PanelPage(PanelViewModel vm, AuthViewModel auth, AcikSorularViewModel sorular, RiskKartiViewModel risk)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
        _vm.GitIstendi += Git;   // Paket A: durum kartı / yapılacak satırı dokunuşu
        SorularKartiAlani.BindingContext = _sorular = sorular;
        RiskKartiAlani.BindingContext = _risk = risk;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Bekleyen giderler ve risk kartı yalnız editöre yüklenir.
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        _sorular.EditorMu = _risk.EditorMu = _vm.EditorMu;
        await Task.WhenAll(_vm.YukleAsync(), _sorular.YukleAsync(), _risk.YukleAsync());
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
