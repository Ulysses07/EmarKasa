using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage
{
    private readonly AyarlarViewModel _vm;
    private readonly GuvenlikViewModel _guvenlik;

    public AyarlarPage(AyarlarViewModel vm, GuvenlikViewModel guvenlik)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        GuvenlikAlani.BindingContext = _guvenlik = guvenlik;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.WhenAll(_vm.YukleAsync(), _guvenlik.YukleAsync());
        await _vm.KurlariYukleAsync();   // kur/endeks tablosu (AyarlarViewModel.B)
    }
}
