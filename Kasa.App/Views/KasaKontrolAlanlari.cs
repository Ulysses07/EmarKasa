using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

internal static class KasaKontrolAlanlari
{
    private static View Durum(OturumluViewModel vm, Func<Task> yukle, View govde)
    {
        var panel = new VerticalStackLayout { Spacing = 12, BindingContext = vm };
        var busy = new ActivityIndicator(); busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.Mesgul));
        var hata = Bagli(nameof(vm.Hata)); hata.TextColor = Colors.DarkRed;
        panel.Add(Tikla("Yenile / tekrar dene", yukle)); panel.Add(busy); panel.Add(hata); panel.Add(Bagli(nameof(vm.Mesaj)));
        var tarih = new Label { FontSize = 12 }; tarih.SetBinding(Label.TextProperty, new Binding(nameof(vm.SonGuncelleme), stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}")); panel.Add(tarih);
        govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir)); govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter()); panel.Add(govde); return panel;
    }
    public static View Kontrol(KasaKontrolViewModel vm)
    {
        var body = new VerticalStackLayout { Spacing = 16, Children = {
            Kart("Kanal alt limit uyarıları", Bagli(nameof(vm.EsikUyarilari)), Metin("Alt limitler Ayarlar bölümünden açılır. Uyarılar kanal bakiyesini değiştirmez.")),
            Editor(Kart("Gerçek genel bakiye ile karşılaştır", Metin("Gerçekte saydığınız toplam bakiyeyi girin. Karşılaştırma kaydı tutulur; fark kasaya veya kanallara otomatik işlenmez."),
                Alan("Gerçek toplam bakiye", Girdi(nameof(vm.GercekBakiye), true)), Alan("Açıklama", Girdi(nameof(vm.Not))), Dugme("Farkı göster", nameof(vm.OnizleCommand)), Bagli(nameof(vm.Karsilastirma)), Dugme("Karşılaştırmayı kaydet", nameof(vm.KaydetCommand)))),
            Kart("Bakiye karşılaştırma geçmişi", Liste<KasaKontrolSatiri>(nameof(vm.Gecmis)))
        } };
        return Durum(vm, vm.YukleAsync, body);
    }
    public static View Esikler(KasaEsikViewModel vm)
    {
        var body = Kart("Kanal alt limitleri", Metin("Uyarılar başlangıçta kapalıdır. Bir kanal seçip alt limitini ve uyarıyı açın. Kasalar ekranında limit altına düşen kanallar gösterilir."),
            Liste<KasaEsikSatiri>(nameof(vm.Kanallar)), Alan("Kanal", Secim(nameof(vm.Kanallar), nameof(vm.Secili))), Alan("Alt limit", Girdi(nameof(vm.Tutar), true)), Onay("Bu kanal için alt limit uyarısı açık", nameof(vm.Etkin)), Dugme("Alt limiti kaydet", nameof(vm.KaydetCommand)));
        return Durum(vm, vm.YukleAsync, body);
    }
    public static View Kilit(AyKilidiViewModel vm, AylikViewModel rapor, Page page)
    {
        async Task Degistir(bool kapat)
        {
            var yil = rapor.Yil; var ay = rapor.Ay;
            var oturum = vm.OturumNesli; var surum = vm.DurumSurumu;
            if (surum is null || !vm.VeriHazir) return;
            var mesaj = kapat ? $"{ay:00}.{yil} ayının sonuna kadar bütün geçmiş mali hareketler kilitlenecek. Okumalar ve gelecek planlar devam eder." : $"{ay:00}.{yil} ayı ve sonraki aylar yeniden açılacak. Bu dönemlerin mali hareketleri değiştirilebilir olacak.";
            var gerekce = await page.DisplayPromptAsync(kapat ? "Ayı kapat" : "Ayı ve sonrasını aç", mesaj + "\nDeğişiklik gerekçesini yazın.", "Devam", "Vazgeç", maxLength: 1000);
            if (string.IsNullOrWhiteSpace(gerekce)) return;
            if (oturum != vm.OturumNesli || yil != rapor.Yil || ay != rapor.Ay) return;
            if (await page.DisplayAlertAsync("Ay kilidi değişikliğini onayla", mesaj + "\n\n" + gerekce, "Onayla", "Vazgeç") && yil == rapor.Yil && ay == rapor.Ay)
                await vm.DegistirAsync(kapat, yil, ay, gerekce, oturum, surum.Value);
        }
        var body = Kart("Ay kilidi", Bagli(nameof(vm.DurumMetni)), Metin("Üstte seçili rapor ayı kullanılır. Ayı kapatmak o ayın sonuna kadar geçmişi korur; açmak seçilen ayı ve sonrasını açar."),
            Editor(Tikla("Seçili ayı kapat", () => Degistir(true))), Editor(Tikla("Seçili ayı ve sonrasını aç", () => Degistir(false))), Liste<AyKilidiSatiri>(nameof(vm.Gecmis)));
        return Durum(vm, vm.YukleAsync, body);
    }
}
