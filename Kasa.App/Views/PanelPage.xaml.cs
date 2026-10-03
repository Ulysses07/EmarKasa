using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly PanelViewModel _vm;
    private readonly TakipOzetViewModel _takip;
    private readonly KasaKontrolViewModel _kontrol;

    public PanelPage(PanelViewModel vm, TakipOzetViewModel takip, KasaKontrolViewModel kontrol, CekOzetViewModel cekler)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _takip = takip;
        _kontrol = kontrol;
        // Takip özeti ve kanal eşikleri panelle aynı ana sayfa yanıtından gelir (bakiye, uyarı ve kart borcu aynı andan); eski
        // sunucuda null'dır ve eski uçlardan ayrıca yüklenir. Her başarılı panel yüklemesinden sonra yenilenir; panelin hatası alt
        // bölümlerin son verisini silmez (tasarım 2026-10-02 §3).
        _vm.Yuklendi += async (_, _) =>
        {
            await takip.PaneldenYukleAsync(vm.TakipOzeti, vm.TakipOzetiGunu);
            await kontrol.YukleAsync(vm.KasaEsikleri);
            await cekler.YukleAsync();
        };
        takip.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(takip.KanalKartBorclari) or nameof(takip.VeriHazir))
                _vm.KartBorclariniYansit(takip.VeriHazir ? takip.KanalKartBorclari : null);
        };
        var secim = new HorizontalStackLayout { Spacing = 10 };
        foreach (var gun in new[] { 7, 30 })
            secim.Add(TakipUi.Tikla($"Önümüzdeki {gun} gün", async () => { takip.Gun = gun; _vm.TakipGunu = gun; await takip.YukleAsync(); }));
        // KS-03: gösterge yalnız meşgulken, hata yalnız doluyken görünür; ikisi de boşken yer kaplamaz (87 px boşluk).
        var (yukle, hata) = TakipUi.MesgulVeHata(nameof(takip.Mesgul), nameof(takip.Hata));
        var icerik = new VerticalStackLayout { Spacing = 12 };
        icerik.Add(TakipUi.Bagli(nameof(takip.Ozet)));
        icerik.Add(TakipUi.Bagli(nameof(takip.BelirsizBorcOzeti)));
        icerik.Add(TakipUi.Liste<TakipOlaySatiri>(nameof(takip.Olaylar)));
        icerik.SetBinding(IsVisibleProperty, nameof(takip.VeriHazir));
        var kart = TakipUi.Kart("Kart ve kredi takibi",
            TakipUi.Metin("Kart kesimi ve son ödeme günü hatırlatmadır; kartta kasa yalnız kaydedilen ödeme ile değişir. Kredi taksitleri ise vade tarihinde otomatik olarak kasaya işlenir."),
            secim, yukle, hata, icerik,
            TakipUi.Tikla("Kartlar", () => Shell.Current.GoToAsync("//kartlar")), TakipUi.Tikla("Krediler", () => Shell.Current.GoToAsync("//krediler")));
        kart.BindingContext = takip;
        PanelAlani.Add(kart);
        PanelAlani.Add(CekKutusu(cekler));
        PanelAlani.Add(KasaKontrolAlanlari.Kontrol(kontrol));
    }

    /// <summary>Çekler kutusu (docs/specs/2026-10-01-cekler.md "Panel"): dört satır; satıra tıklayınca Çekler sayfası o süzgeçle açılır.</summary>
    private static View CekKutusu(CekOzetViewModel cekler)
    {
        var satirlar = new VerticalStackLayout { Spacing = 8 };
        foreach (var (yol, suzgec) in new[]
        {
            (nameof(cekler.Alinan30Metni), CekHazirSuzgec.Alinan30), (nameof(cekler.Verilen30Metni), CekHazirSuzgec.Verilen30),
            (nameof(cekler.GecmisMetni), CekHazirSuzgec.VadesiGecmis), (nameof(cekler.VerilenGecmisMetni), CekHazirSuzgec.VerilenVadesiGecmis),
        })
        {
            var satir = new Button { HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
            satir.SetBinding(Button.TextProperty, yol);
            satir.Clicked += async (_, _) => await Shell.Current.GoToAsync($"//cekler?Suzgec={suzgec}");
            satirlar.Add(satir);
        }
        satirlar.SetBinding(IsVisibleProperty, nameof(cekler.VeriHazir));
        var kutu = TakipUi.Kart("Çekler", TakipUi.Metin("Teminat çekleri bu toplamlara girmez."), TakipUi.BagliHata(nameof(cekler.Hata)), satirlar,
            TakipUi.Tikla("Çekler", () => Shell.Current.GoToAsync("//cekler")));
        kutu.BindingContext = cekler;
        return kutu;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }

    /// <summary>Başka ekrana geçince süren ana sayfa isteği iptal edilir (sunucu hesabı da kesilir).</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.EkrandanAyril();
    }
}
