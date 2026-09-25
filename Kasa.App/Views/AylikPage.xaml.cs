using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage
{
    private readonly AylikViewModel _vm;
    private readonly AyKilidiViewModel _kilit;

    public AylikPage(AylikViewModel vm, AyKilidiViewModel kilit)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _kilit = kilit;
        AylikAlani.Add(KasaKontrolAlanlari.Kilit(kilit, vm, this));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
        await _kilit.YukleAsync();
    }
}
