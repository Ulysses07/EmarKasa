using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Gelen tablosu (paket C · 29). Mantık GelenlerViewModel'de; burada yalnız yukarı kaydırma.</summary>
public partial class GelenlerPage : ContentPage
{
    private readonly GelenlerViewModel _vm;
    private readonly AuthViewModel _auth;

    public GelenlerPage(GelenlerViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
        // "O haftaya git": tablo sayfanın başında; eksik listesi aşağıdayken değişiklik görünsün.
        _vm.YukariKaydirIstendi += (_, _) => Dispatcher.Dispatch(async () => await Kaydirma.ScrollToAsync(0, 0, true));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
