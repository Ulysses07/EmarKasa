using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

internal static class KasaKontrolAlanlari
{
    /// <param name="sonVeri">Yükleme hata verse de son veri görünür ve soluk (Kasalar'daki kasa kontrolü, panelle tutarlı; tasarım
    /// 2026-10-02 §3); değilse gövde yalnız veri hazırken görünür.</param>
    private static View Durum(OturumluViewModel vm, View govde, bool sonVeri = false)
    {
        var panel = new VerticalStackLayout { Spacing = 12, BindingContext = vm };
        BolumDurumu(panel, Bagli(nameof(vm.Mesaj)));
        if (sonVeri)
            SonVeriGovdesi(govde);
        else
            govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir));
        govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        panel.Add(govde);
        return panel;
    }
    public static View Kontrol(KasaKontrolViewModel vm)
    {
        var sifirOnayi = BagliHata(nameof(vm.KayitUyarisi));   // 0 bakiye: ikinci basışta kaydedilir
        var esikHatasi = BagliHata(nameof(vm.EsikHatasi));     // eşikler yüklenemedi; geçmiş yine görünür
        var body = new VerticalStackLayout
        {
            Spacing = 16,
            Children = {
            Kart("Kanal alt limit uyarıları", Bagli(nameof(vm.EsikUyarilari)), Goster(esikHatasi, nameof(vm.EsikHatasi), true),
                Metin("Alt limitler Ayarlar'dan açılır.")),
            Editor(Kart("Gerçek genel bakiye ile karşılaştır",
            Metin("Saydığınız toplam bakiyeyi girin; fark varsa açıklayın."),
                Alan("Gerçek toplam bakiye", Girdi(nameof(vm.GercekBakiye), true)), Alan("Açıklama", Girdi(nameof(vm.Not))),
                Dugme("Farkı göster", nameof(vm.OnizleCommand)), Bagli(nameof(vm.Karsilastirma)),
                Dugme("Karşılaştırmayı kaydet", nameof(vm.KaydetCommand)),
                Goster(sifirOnayi, nameof(vm.KayitUyarisi), true))),
            // Geçmiş: kayıtlı değerler değişmez; kayıt gününe ya da öncesine sonradan dokunan değişiklik "sonradan değişti" diye işaretlenir.
            Kart("Bakiye karşılaştırma geçmişi", Yardim("Kayıt gününe ya da öncesine sonradan girilen, silinen ya da düzeltilen kayıtlar güncel durumu "
                + "değiştirir; karşılaştırmanın kayıtlı değerleri aynı kalır. Fark kasaya ya da kanallara işlenmez."),
                Liste<KasaKontrolSatiri>(nameof(vm.Gecmis), vm.IncelemeAsync, "Değişenleri göster", _ => vm.EditorMu)),
            Goster(Kart("Bu kontrolden beri değişenler", Bagli(nameof(vm.SonrasiOzeti)),
                Metin("Kontrolden sonra yapılan değişiklikler"), Liste<KasaKontrolDegisiklikSatiri>(nameof(vm.Degisiklikler)),
                Metin("Kontrolden sonra kasaya işleyen hareketler"), Liste<KasaHareketiSatiri>(nameof(vm.SonrasiHareketler)),
                Editor(Alan("Fark açıklaması", Girdi(nameof(vm.FarkAciklamasi)))), Editor(Dugme("Açıklamayı kaydet", nameof(vm.AciklaCommand)))), nameof(vm.Secili), true),
            Kart("Kasa hareket dökümü",
                Alan("Başlangıç", Tarih(nameof(vm.DokumBaslangic))), Alan("Bitiş", Tarih(nameof(vm.DokumBitis))), Alan("Kasa", Secim(nameof(vm.DokumKasalari), nameof(vm.DokumKasa))),
                Dugme("Dökümü göster", nameof(vm.DokumGetirCommand)), Bagli(nameof(vm.DokumOzeti)), Liste<KasaHareketiSatiri>(nameof(vm.DokumSatirlari)))
        }
        };
        return Durum(vm, body, sonVeri: true);
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
