using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Platformdaki bildirim olanakları, sunucu hatırlatma saati/listesi ve izin verilmiş web cihazları.</summary>
public sealed class BildirimPage : TakipSayfasi<BildirimViewModel>
{
    public BildirimPage(BildirimViewModel vm) : base(vm, "Bildirimler", vm.YukleAsync)
    {
        var telefon = TakipUi.Tikla("Telefon kurulumunu aç", TelefonKurulumunuAcAsync);
        telefon.Style = (Style)Application.Current!.Resources["BtnSecondary"];
#if WINDOWS
        Govde.Add(TakipUi.Kart("Bu bilgisayarda Windows bildirimleri",
            TakipUi.Onay("Windows bildirimleri açık", nameof(vm.WindowsBildirimleri)),
            TakipUi.BagliMetin(nameof(vm.WindowsDurumu)),
            TakipUi.Goster(WindowsAyariUyarisi(), nameof(vm.WindowsAyarindaKapali)),
            TakipUi.Dugme("Deneme bildirimi göster", nameof(vm.DenemeGosterCommand)),
            TakipUi.Bagli(nameof(vm.CihazDurumu)),
            TakipUi.Yardim("Uygulama kapalıyken her gün hatırlatma saatinden 5 dakika sonra ve Windows oturumu açılınca kontrol edilir. Bildirime "
                + "tıklayınca ilgili kart ya da kredi açılır ve bildirim okundu işaretlenir; yalnız görmek okundu yapmaz."),
            telefon));
#else
        Govde.Add(TakipUi.Kart("Bu uygulamada hatırlatmalar",
            TakipUi.Metin("Hatırlatmalar aşağıdaki listede görünür."),
            TakipUi.Yardim("Bu iOS sürümü uygulama kapalıyken sistem bildirimi göndermez. Telefon bildirimi için web kurulumundaki destek ve "
                + "izin adımlarını izleyin; web bildirim kaydı bu uygulamadan ayrıdır."),
            telefon));
#endif
        Govde.Add(TakipUi.Kart("Hatırlatma saati",
            TakipUi.Onay("Hatırlatmalar açık", nameof(vm.Etkin)),
            TakipUi.Alan("Saat", TakipUi.Girdi(nameof(vm.Saat), sayi: true)),
            TakipUi.Alan("Dakika", TakipUi.Girdi(nameof(vm.Dakika), sayi: true)),
            TakipUi.Birincil(TakipUi.Dugme("Ayarları kaydet", nameof(vm.KaydetCommand)))));
        Govde.Add(TakipUi.Kart("Hatırlatmalar", TakipUi.Liste<BildirimSatiri>(nameof(vm.Bildirimler), vm.OkunduAsync, "Okundu işaretle", x => !x.Veri.Okundu),
            TakipUi.DugmeSatiri(TakipUi.Tikla("Kartları aç", () => Shell.Current.GoToAsync("//kartlar")), TakipUi.Tikla("Kredileri aç", () => Shell.Current.GoToAsync("//krediler")))));
        Govde.Add(TakipUi.Kart("İzin verilmiş cihazlar",
#if WINDOWS
            TakipUi.Metin("Tarayıcı da kayıtlıysa bildirim iki kez gelir; tarayıcı kaydını buradan kapatın."),
#else
            TakipUi.Metin("Web bildirim kayıtlarını buradan kapatabilirsiniz."),
#endif
            TakipUi.Liste<BildirimCihaziSatiri>(nameof(vm.Cihazlar), async c =>
            {
                if (await DisplayAlertAsync("Cihaz bildirimleri", $"{c.Veri.CihazAdi} için bildirimler kapatılsın mı?", "Kapat", "Vazgeç"))
                    await vm.CihaziKaldirAsync(c);
            }, "Bu cihazı kapat", x => x.Veri.Etkin)));
    }

#if WINDOWS
    /// <summary>Sayfa her göründüğünde anahtar, durum satırı ve Windows ayarı yeniden okunur.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Vm.WindowsDurumunuYenile();
    }

    private static View WindowsAyariUyarisi()
    {
        var uyari = new VerticalStackLayout { Spacing = 8 };
        uyari.Add(new Label
        {
            Text = "Windows ayarlarında bu uygulamanın bildirimleri kapalı. Bildirim görmek için Windows bildirim ayarlarından açın.",
            Style = (Style)Application.Current!.Resources["LblTakipHata"],
        });
        uyari.Add(TakipUi.Tikla("Windows bildirim ayarlarını aç", async () =>
        {
            try
            {
                await Launcher.Default.OpenAsync(new Uri("ms-settings:notifications"));
            }
            catch (Exception)
            {
                // Ayarlar açılamazsa uyarı metni yolu gösterir.
            }
        }));
        return uyari;
    }
#endif

    private async Task TelefonKurulumunuAcAsync()
    {
        try
        {
            var adres = BildirimViewModel.KurulumAdresi(Environment.GetEnvironmentVariable("KASA_API_URL"));
            await Browser.Default.OpenAsync(adres, BrowserLaunchMode.External);
        }
        catch
        {
            await DisplayAlertAsync("Tarayıcı açılamadı", "Kasa'nın web sitesindeki Bildirimler sayfasını tarayıcıda açın.", "Tamam");
        }
    }
}
