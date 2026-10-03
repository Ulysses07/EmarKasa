using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage, Controls.IYenilenebilir
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
        await YenileAsync();
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3): rapor ve ay kilidi.</summary>
    public async Task YenileAsync()
    {
        await _vm.YukleAsync();
        await _kilit.YukleAsync();
    }
}
