using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage, Controls.IYenilenebilir
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
        var gorunur = new Controls.GorunurYapici(AyarlarKaydirici);
        vm.KanalHatalari.GosterIstendi += (_, _) => gorunur.HatayaGit(KanalFormu, vm.KanalHatalari, KanalHataKutusu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await YenileAsync();
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3): ayarlar, güvenlik ve eşikler.</summary>
    public async Task YenileAsync()
    {
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
