using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class CariOzetiPage : ContentPage
{
    private readonly CariOzetiViewModel _vm;

    public CariOzetiPage(CariOzetiViewModel vm)
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
