using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Başka sayfalara gömülü kasa bölümleri: Ayarlar'daki kanal alt limitleri ve Aylık'taki ay kilidi. Kasa kontrolü kendi
/// sayfasıdır (<see cref="KasaKontrolPage"/>).</summary>
internal static class KasaKontrolAlanlari
{
    /// <summary>Gömülü bölüm: durum satırı (BolumDurumu) ve yalnız veri hazırken görünen gövde.</summary>
    private static View Durum(OturumluViewModel vm, View govde)
    {
        var panel = new VerticalStackLayout { Spacing = 12, BindingContext = vm };
        BolumDurumu(panel, Bagli(nameof(vm.Mesaj)));
        govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir));
        govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        panel.Add(govde);
        return panel;
    }
    public static View Esikler(KasaEsikViewModel vm)
    {
        var body = Kart("Kanal alt limitleri", Metin("Bir kanal seçip alt limitini girin."),
            Liste<KasaEsikSatiri>(nameof(vm.Kanallar)), Alan("Kanal", Secim(nameof(vm.Kanallar), nameof(vm.Secili))),
            Alan("Alt limit", Girdi(nameof(vm.Tutar), true)), Onay("Bu kanal için alt limit uyarısı açık", nameof(vm.Etkin)),
            Dugme("Alt limiti kaydet", nameof(vm.KaydetCommand)));
        return Durum(vm, body);
    }
    public static View Kilit(AyKilidiViewModel vm, AylikViewModel rapor, Page page)
    {
        // Onay metni ve "rapor ayı/oturum değişti mi" koşulu AyKilidiViewModel'dedir (maui-8); burada yalnız diyaloglar gösterilir.
        async Task Degistir(bool kapat)
        {
            var yil = rapor.Yil;
            var ay = rapor.Ay;
            var surum = vm.DurumSurumu;
            if (surum is null || !vm.VeriHazir)
                return;
            var mesaj = vm.OnayMetni(kapat, yil, ay, rapor.Rapor);
            await vm.GerekceyleAsync(() => page.DisplayPromptAsync(kapat ? "Ayı kapat" : "Ayı ve sonrasını aç", mesaj + "\nDeğişiklik gerekçesini yazın.", "Devam", "Vazgeç", maxLength: 1000),
                async (gerekce, oturum) =>
                {
                    if (!vm.IstekHalaGecerli(oturum, yil, ay, rapor.Yil, rapor.Ay))
                        return;
                    if (await page.DisplayAlertAsync("Ay kilidi değişikliğini onayla", mesaj + "\n\n" + gerekce, "Onayla", "Vazgeç") && vm.IstekHalaGecerli(oturum, yil, ay, rapor.Yil, rapor.Ay))
                        await vm.DegistirAsync(kapat, yil, ay, gerekce, oturum, surum.Value);
                });
        }
        var body = Kart("Ay kilidi", Bagli(nameof(vm.DurumMetni)), Yardim("Üstte seçili ay kullanılır. Ayı kapatmak o ayın sonuna kadar geçmişi korur; açmak seçilen ayı ve sonrasını açar."),
            Editor(Tikla("Seçili ayı kapat", () => Degistir(true))), Editor(Tikla("Seçili ayı ve sonrasını aç", () => Degistir(false))), Liste<AyKilidiSatiri>(nameof(vm.Gecmis)));
        return Durum(vm, body);
    }
}
