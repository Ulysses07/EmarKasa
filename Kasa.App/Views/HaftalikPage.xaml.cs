using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class HaftalikPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

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
