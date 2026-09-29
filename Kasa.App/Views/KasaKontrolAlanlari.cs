using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

internal static class KasaKontrolAlanlari
{
    private static View Durum(OturumluViewModel vm, Func<Task> yukle, View govde)
    {
        var panel = new VerticalStackLayout { Spacing = 12, BindingContext = vm };
        var busy = new ActivityIndicator();
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.Mesgul));
        var hata = Bagli(nameof(vm.Hata));
        hata.TextColor = Colors.DarkRed;
        panel.Add(Tikla("Yenile / tekrar dene", yukle));
        panel.Add(busy);
        panel.Add(hata);
        panel.Add(Bagli(nameof(vm.Mesaj)));
        var tarih = new Label { FontSize = 12 };
        tarih.SetBinding(Label.TextProperty, new Binding(nameof(vm.SonGuncelleme), stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
        panel.Add(tarih);
        govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir));
        govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        panel.Add(govde);
        return panel;
    }
    public static View Kontrol(KasaKontrolViewModel vm)
    {
        var sifirOnayi = Bagli(nameof(vm.KayitUyarisi));
        sifirOnayi.TextColor = Colors.DarkRed;   // 0 bakiye: ikinci basışta kaydedilir
        var esikHatasi = Bagli(nameof(vm.EsikHatasi));
        esikHatasi.TextColor = Colors.DarkRed;     // eşikler yüklenemedi; geçmiş yine görünür
        var body = new VerticalStackLayout
        {
            Spacing = 16,
            Children = {
            Kart("Kanal alt limit uyarıları", Bagli(nameof(vm.EsikUyarilari)), Goster(esikHatasi, nameof(vm.EsikHatasi), true), Metin("Alt limitler Ayarlar bölümünden açılır. Uyarılar kanal bakiyesini değiştirmez.")),
            Editor(Kart("Gerçek genel bakiye ile karşılaştır", Metin("Gerçekte saydığınız toplam bakiyeyi girin. Karşılaştırma kaydı tutulur; fark kasaya veya kanallara otomatik işlenmez. Fark varsa açıklama zorunludur; farkı gördükten sonra yazabilirsiniz."),
                Alan("Gerçek toplam bakiye", Girdi(nameof(vm.GercekBakiye), true)), Alan("Açıklama (fark varsa zorunlu)", Girdi(nameof(vm.Not))), Dugme("Farkı göster", nameof(vm.OnizleCommand)), Bagli(nameof(vm.Karsilastirma)), Dugme("Karşılaştırmayı kaydet", nameof(vm.KaydetCommand)),
                Goster(sifirOnayi, nameof(vm.KayitUyarisi), true))),
            // Geçmiş: kayıtlı değerler değişmez; kayıt gününe ya da öncesine sonradan dokunan değişiklik "sonradan değişti" diye işaretlenir.
            Kart("Bakiye karşılaştırma geçmişi", Metin("Kayıt gününe ya da öncesine sonradan girilen, silinen veya düzeltilen kayıtlar güncel durumu değiştirir; kayıtlı değerler değişmez."),
                Liste<KasaKontrolSatiri>(nameof(vm.Gecmis), vm.IncelemeAsync, "Değişenleri göster", _ => vm.EditorMu)),
            Goster(Kart("Bu kontrolden beri değişenler", Bagli(nameof(vm.SonrasiOzeti)),
                Metin("Kontrolden sonra yapılan değişiklikler"), Liste<KasaKontrolDegisiklikSatiri>(nameof(vm.Degisiklikler)),
                Metin("Kontrol gününe sonradan girilen giderler ve kontrol gününden bugüne kasaya işleyen hareketler"), Liste<KasaHareketiSatiri>(nameof(vm.SonrasiHareketler)),
                Editor(Alan("Fark açıklaması", Girdi(nameof(vm.FarkAciklamasi)))), Editor(Dugme("Açıklamayı kaydet", nameof(vm.AciklaCommand)))), nameof(vm.Secili), true),
            Kart("Kasa hareket dökümü", Metin("Genel kasayı ya da seçilen kanalın kasasını oluşturan bütün hareketler kaynağıyla. Kredi taksitleri ve eski kartın ay sonu düşümü tarihinde kendiliğinden işler."),
                Alan("Başlangıç", Tarih(nameof(vm.DokumBaslangic))), Alan("Bitiş", Tarih(nameof(vm.DokumBitis))), Alan("Kasa", Secim(nameof(vm.DokumKasalari), nameof(vm.DokumKasa))),
                Dugme("Dökümü göster", nameof(vm.DokumGetirCommand)), Bagli(nameof(vm.DokumOzeti)), Liste<KasaHareketiSatiri>(nameof(vm.DokumSatirlari)))
        }
        };
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
        // Onay metni ve "rapor ayı/oturum değişti mi" koşulu AyKilidiViewModel'dedir (maui-8); burada yalnız diyaloglar gösterilir.
        async Task Degistir(bool kapat)
        {
            var yil = rapor.Yil;
            var ay = rapor.Ay;
            var oturum = vm.OturumNesli;
            var surum = vm.DurumSurumu;
            if (surum is null || !vm.VeriHazir)
                return;
            var mesaj = vm.OnayMetni(kapat, yil, ay, rapor.Rapor);
            var gerekce = await page.DisplayPromptAsync(kapat ? "Ayı kapat" : "Ayı ve sonrasını aç", mesaj + "\nDeğişiklik gerekçesini yazın.", "Devam", "Vazgeç", maxLength: 1000);
            if (string.IsNullOrWhiteSpace(gerekce) || !vm.IstekHalaGecerli(oturum, yil, ay, rapor.Yil, rapor.Ay))
                return;
            if (await page.DisplayAlertAsync("Ay kilidi değişikliğini onayla", mesaj + "\n\n" + gerekce, "Onayla", "Vazgeç") && vm.IstekHalaGecerli(oturum, yil, ay, rapor.Yil, rapor.Ay))
                await vm.DegistirAsync(kapat, yil, ay, gerekce, oturum, surum.Value);
        }
        var body = Kart("Ay kilidi", Bagli(nameof(vm.DurumMetni)), Metin("Üstte seçili rapor ayı kullanılır. Ayı kapatmak o ayın sonuna kadar geçmişi korur; açmak seçilen ayı ve sonrasını açar."),
            Editor(Tikla("Seçili ayı kapat", () => Degistir(true))), Editor(Tikla("Seçili ayı ve sonrasını aç", () => Degistir(false))), Liste<AyKilidiSatiri>(nameof(vm.Gecmis)));
        return Durum(vm, vm.YukleAsync, body);
    }
}
