using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage
{
    private readonly AyarlarViewModel _vm;
    private readonly GuvenlikViewModel _guvenlik;
    private readonly KasaEsikViewModel _esik;

    public AyarlarPage(AyarlarViewModel vm, GuvenlikViewModel guvenlik, KasaEsikViewModel esik)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _guvenlik = guvenlik;
        _esik = esik;
        AyarlarAlani.Children.Add(KasaKontrolAlanlari.Esikler(esik));
        AyarlarAlani.Children.Add(new GuvenlikAlani(guvenlik, this));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
        await _guvenlik.YukleAsync();
        await _esik.YukleAsync();
    }

    protected override void OnDisappearing() { _guvenlik.EkrandanAyril(); base.OnDisappearing(); }

    private async void KanalSilTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: KanalDto kanal } dugme)
            await SilmeOnayi.GosterAsync(this, dugme, $"Kanal: {kanal.Ad}", () => _vm.KanalSilCommand.ExecuteAsync(kanal));
    }
}
