using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class KrediKartlariPage : ContentPage
{
    private readonly KrediKartlariViewModel _vm;

    public KrediKartlariPage(KrediKartlariViewModel vm)
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
