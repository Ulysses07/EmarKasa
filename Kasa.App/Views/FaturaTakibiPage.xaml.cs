using System.ComponentModel;
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class FaturaTakibiPage : ContentPage
{
    private readonly FaturaTakibiViewModel _vm;
    private readonly AuthViewModel _auth;

    public FaturaTakibiPage(FaturaTakibiViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
        _vm.PropertyChanged += PaneleKaydir;
    }

    /// <summary>"Fatura geldi" formu ya da ek paneli açılınca sayfa ona kayar (satır listenin altında olabilir).</summary>
    private async void PaneleKaydir(object? sender, PropertyChangedEventArgs e)
    {
        View? hedef = e.PropertyName switch
        {
            nameof(FaturaTakibiViewModel.GelenFormuGorunur) when _vm.GelenFormuGorunur => GelenPaneli,
            nameof(FaturaTakibiViewModel.EkPaneliGorunur) when _vm.EkPaneliGorunur => EkPaneli,
            _ => null,
        };
        if (hedef is null) return;
        try
        {
            await Task.Yield();   // panel görünür olup yerleşsin
            await Kaydirici.ScrollToAsync(hedef, ScrollToPosition.Start, animated: true);
        }
        catch (Exception) { /* kaydırma yalnız kolaylık: başarısızsa panel yine görünür */ }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // "Fatura geldi" yalnız editörde
        await _vm.YukleAsync();
    }
}
