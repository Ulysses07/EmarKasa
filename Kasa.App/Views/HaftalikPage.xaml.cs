using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class HaftalikPage : ContentPage
{
    private readonly HaftalikViewModel _vm;

    public HaftalikPage(HaftalikViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }

    /// <summary>Başka ekrana geçince süren rapor isteği iptal edilir (sunucu hesabı da kesilir).</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.EkrandanAyril();
    }
}
