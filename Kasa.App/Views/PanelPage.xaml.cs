using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage
{
    private readonly PanelViewModel _vm;
    private readonly TakipOzetViewModel _takip;
    private readonly KasaKontrolViewModel _kontrol;

    public PanelPage(PanelViewModel vm, TakipOzetViewModel takip, KasaKontrolViewModel kontrol)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _takip = takip;
        _kontrol = kontrol;
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(vm.VeriVar))
            {
                if (vm.VeriVar) { await takip.YukleAsync(); await kontrol.YukleAsync(); }
                else { takip.VeriHazir = false; kontrol.VeriHazir = false; }
            }
        };
        takip.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(takip.KanalKartBorclari) or nameof(takip.VeriHazir))
                _vm.KartBorclariniYansit(takip.VeriHazir ? takip.KanalKartBorclari : null);
        };
        var secim = new HorizontalStackLayout { Spacing = 10 };
        foreach (var gun in new[] { 7, 30 }) secim.Add(TakipUi.Tikla($"Önümüzdeki {gun} gün", async () => { takip.Gun = gun; await takip.YukleAsync(); }));
        var yukle = new ActivityIndicator(); yukle.SetBinding(ActivityIndicator.IsRunningProperty, nameof(takip.Mesgul));
        var hata = TakipUi.Bagli(nameof(takip.Hata)); hata.TextColor = Colors.DarkRed;
        var icerik = new VerticalStackLayout { Spacing = 12 };
        icerik.Add(TakipUi.Bagli(nameof(takip.Ozet)));
        icerik.Add(TakipUi.Bagli(nameof(takip.BelirsizBorcOzeti)));
        icerik.Add(TakipUi.Liste<TakipOlaySatiri>(nameof(takip.Olaylar)));
        icerik.SetBinding(IsVisibleProperty, nameof(takip.VeriHazir));
        var kart = TakipUi.Kart("Kart ve kredi takibi", TakipUi.Metin("Kart kesimi ve son ödeme günü hatırlatmadır; kartta kasa yalnız kaydedilen ödeme ile değişir. Kredi taksitleri ise vade tarihinde otomatik olarak kasaya işlenir."), secim, yukle, hata, icerik,
            TakipUi.Tikla("Kartlar", () => Shell.Current.GoToAsync("//kartlar")), TakipUi.Tikla("Krediler", () => Shell.Current.GoToAsync("//krediler")));
        kart.BindingContext = takip; PanelAlani.Add(kart);
        PanelAlani.Add(KasaKontrolAlanlari.Kontrol(kontrol));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
