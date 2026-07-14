using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class CarilerPage : ContentPage
{
    private readonly CarilerViewModel _vm;

    public CarilerPage(CarilerViewModel vm)
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
