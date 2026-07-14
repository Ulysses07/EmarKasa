using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage
{
    private readonly AylikViewModel _vm;

    public AylikPage(AylikViewModel vm)
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
