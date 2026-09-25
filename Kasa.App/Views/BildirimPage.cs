using Kasa.App.Core;

namespace Kasa.App.Views;

public sealed class BildirimPage : TakipSayfasi<BildirimViewModel>
{
    public BildirimPage(BildirimViewModel vm) : base(vm, "Bildirimler", "Kart kesimi, son ödeme ve kredi taksiti hatırlatmaları", vm.YukleAsync)
    {
        Govde.Add(TakipUi.Kart("Masaüstü ve telefon bildirimleri",
            TakipUi.Metin("Bildirimleri tarayıcıda bir kez açın. Windows'ta Edge veya Chrome kullanın; izin verilmiş tarayıcının arka planda çalışmasına ve Windows bildirimlerine izin verin. Emar Kasa uygulaması kapalıyken de hatırlatma alabilirsiniz. Telefonunuzda aynı sayfadan o cihaza izin verin."),
            TakipUi.Metin("Düğme Kasa'nın bildirim sayfasını açar. Tarayıcıda gerekirse yeniden giriş yapın, bu cihazın bildirimlerini açın ve deneme gönderin. Ödeme kaydedilince ilgili bekleyen hatırlatma kaldırılır."),
            TakipUi.Bagli(nameof(vm.CihazDurumu)),
            TakipUi.Tikla("Masaüstü bildirimlerini aç / test et", async () =>
            {
                try { await Browser.Default.OpenAsync(BildirimViewModel.KurulumAdresi(Environment.GetEnvironmentVariable("KASA_API_URL")), BrowserLaunchMode.External); }
                catch { await DisplayAlertAsync("Tarayıcı açılamadı", "Kasa'nın web sitesindeki Bildirimler sayfasını tarayıcıda açın.", "Tamam"); }
            })));
        Govde.Add(TakipUi.Kart("Hatırlatma saati",
            TakipUi.Onay("Hatırlatmalar açık", nameof(vm.Etkin)),
            TakipUi.Alan("Saat (0–23, Türkiye saati)", TakipUi.Girdi(nameof(vm.Saat), sayi: true)),
            TakipUi.Alan("Dakika (0–59)", TakipUi.Girdi(nameof(vm.Dakika), sayi: true)),
            TakipUi.Dugme("Ayarları kaydet", nameof(vm.KaydetCommand))));
        Govde.Add(TakipUi.Kart("Hatırlatmalar", TakipUi.Liste<BildirimSatiri>(nameof(vm.Bildirimler), vm.OkunduAsync, "Okundu işaretle", x => !x.Veri.Okundu),
            TakipUi.Tikla("Kartları aç", () => Shell.Current.GoToAsync("//kartlar")), TakipUi.Tikla("Kredileri aç", () => Shell.Current.GoToAsync("//krediler"))));
        Govde.Add(TakipUi.Kart("İzin verilmiş cihazlar", TakipUi.Liste<BildirimCihaziSatiri>(nameof(vm.Cihazlar), async c =>
        {
            if (await DisplayAlertAsync("Cihaz bildirimleri", $"{c.Veri.CihazAdi} için bildirimler kapatılsın mı?", "Kapat", "Vazgeç")) await vm.CihaziKaldirAsync(c);
        }, "Bu cihazı kapat", x => x.Veri.Etkin)));
    }
}
