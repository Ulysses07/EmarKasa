using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage
{
    private readonly AyarlarViewModel _vm;

    public AyarlarPage(AyarlarViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
