using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class HaftalikPage : ContentPage
{
    private readonly HaftalikViewModel _vm;

    public HaftalikPage(HaftalikViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += DetayDegisti;   // Paket B: ayrıntı açılınca ona kay (HaftalikPage.B.cs)
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
