using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Kasa kontrolü (docs/specs/2026-11-26-slop-temizligi.md §8; Kasalar'dan ayrıldı): kanal alt limit uyarıları, karşılaştırma
/// geçmişi, istenince açılan gerçek bakiye karşılaştırması, seçilen kontrolden beri değişenler ve kasa hareket dökümü. Alt limitler
/// Ayarlar'da düzenlenir. Karşılaştırma ve açıklama yalnız editörde görünür; geçmiş, uyarılar ve döküm izleyiciye de açıktır.</summary>
public sealed class KasaKontrolPage : TakipSayfasi<KasaKontrolViewModel>, Controls.IKisayolHedefi
{
    /// <summary>Ctrl+N "Yeni karşılaştırma" (yalnız editör), Ctrl+S yalnız karşılaştırma formu açıkken kaydeder.</summary>
    public System.Windows.Input.ICommand? KisayolKomutu(Controls.Kisayol kisayol) => kisayol switch
    {
        Controls.Kisayol.Yeni when Vm.EditorMu => Vm.FormAcCommand,
        Controls.Kisayol.Kaydet when Vm.FormAcik => Vm.KaydetCommand,
        _ => null,
    };

    public KasaKontrolPage(KasaKontrolViewModel vm) : base(vm, "Kasa kontrolü", vm.YukleAsync)
    {
        // Eşikler yüklenemezse hata bu kartta kalır, geçmiş yine görünür. Limitleri düzenleme Ayarlar'da (yalnız editör).
        Govde.Add(Kart("Alt limit uyarıları", Bagli(nameof(vm.EsikUyarilari)), Goster(BagliHata(nameof(vm.EsikHatasi)), nameof(vm.EsikHatasi), true),
            Editor(Tikla("Alt limitleri düzenle", () => Shell.Current.GoToAsync("//" + MenuModeli.Rota(Bolum.Ayarlar))))));

        // Önce fark gösterilir, sonra kaydedilir; 0 bakiye ikinci basışta kaydedilir (KayitUyarisi).
        var form = Kart("Gerçek bakiye karşılaştırması", Metin("Saydığınız toplamı girin, fark varsa açıklayın."),
            Alan("Gerçek toplam bakiye", Girdi(nameof(vm.GercekBakiye), true)), Alan("Açıklama", Girdi(nameof(vm.Not))),
            Dugme("Farkı göster", nameof(vm.OnizleCommand)), Goster(Bagli(nameof(vm.Karsilastirma)), nameof(vm.Karsilastirma), true),
            Goster(BagliHata(nameof(vm.KayitUyarisi)), nameof(vm.KayitUyarisi), true),
            DugmeSatiri(Birincil(Dugme("Karşılaştırmayı kaydet", nameof(vm.KaydetCommand))), Dugme("Vazgeç", nameof(vm.FormKapatCommand))));

        // Geçmiş satırındaki "Değişenleri göster" (yalnız editör) açar; açıklama kaydı kayıtlı bakiyeleri ve farkı değiştirmez.
        var degisenler = Kart("Bu kontrolden beri değişenler", Bagli(nameof(vm.SonrasiOzeti)),
            Metin("Kontrolden sonra yapılan değişiklikler"), Liste<KasaKontrolDegisiklikSatiri>(nameof(vm.Degisiklikler)),
            Metin("Kontrolden sonra kasaya işleyen hareketler"), Liste<KasaHareketiSatiri>(nameof(vm.SonrasiHareketler)),
            Editor(Alan("Fark açıklaması", Girdi(nameof(vm.FarkAciklamasi)))),
            DugmeSatiri(Editor(Dugme("Açıklamayı kaydet", nameof(vm.AciklaCommand))), Dugme("Kapat", nameof(vm.IncelemeKapatCommand))));

        // Kayıtlı değerler değişmez; kayıt gününe ya da öncesine sonradan dokunan değişiklik "sonradan değişti" diye işaretlenir.
        Govde.Add(Kart("Karşılaştırma geçmişi",
            Yardim("Kayıt gününe ya da öncesine sonradan girilen, silinen ya da düzeltilen kayıtlar güncel durumu değiştirir. "
                + "Karşılaştırmanın kayıtlı değerleri aynı kalır. Fark kasaya ya da kanallara işlenmez."),
            Liste<KasaKontrolSatiri>(nameof(vm.Gecmis), async s =>
            {
                await vm.IncelemeAsync(s);
                if (vm.Secili?.Veri.Id == s.Veri.Id)
                    Gorunur.Yap(degisenler, KaydirmaHesabi.FormKaydirmasi);
            }, "Değişenleri göster", _ => vm.EditorMu, aciklama: s => $"Değişenleri göster: {s.Baslik}"),
            Editor(Dugme("Yeni karşılaştırma", nameof(vm.FormAcCommand)))));
        Govde.Add(Editor(form, nameof(vm.FormAcik)));
        Govde.Add(Goster(degisenler, nameof(vm.Secili), true));

        // Döküm istenince gelir: sorgudan önce boş liste metni yazılmaz, sonuçtaki hareket sayısı özetin içindedir.
        var aralik = new Controls.UyumluIzgara { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        aralik.Add(Alan("Başlangıç", Tarih(nameof(vm.DokumBaslangic))));
        aralik.Add(Alan("Bitiş", Tarih(nameof(vm.DokumBitis))), 1);
        aralik.Add(Alan("Kasa", Secim(nameof(vm.DokumKasalari), nameof(vm.DokumKasa))), 2);
        Govde.Add(Kart("Kasa hareket dökümü", aralik, Dugme("Dökümü göster", nameof(vm.DokumGetirCommand)),
            Goster(Bagli(nameof(vm.DokumOzeti)), nameof(vm.DokumOzeti), true), Liste<KasaHareketiSatiri>(nameof(vm.DokumSatirlari), bosMetin: false)));

        // Açılan form, kayıt hatası ve başarı iletisi görünür yere kaydırılır (hata ve ileti sayfa başlığındadır).
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.FormAcik) && vm.FormAcik)
                Gorunur.Yap(form, KaydirmaHesabi.FormKaydirmasi);
            else if (e.PropertyName == nameof(vm.Hata) && !string.IsNullOrWhiteSpace(vm.Hata))
                Gorunur.Yap(HataSatiri, KaydirmaHesabi.FormKaydirmasi);
            else if (e.PropertyName == nameof(vm.Mesaj) && !string.IsNullOrWhiteSpace(vm.Mesaj))
                Gorunur.Yap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
        };
    }

    /// <summary>Sayfa her göründüğünde yenilenir (ilk açılışta TakipSayfasi yükler): alt limit uyarıları ve geçmişin güncel durumu başka
    /// ekranlardaki kayıtlarla değişir. Kasalar'daki eski bölüm de her açılışta yenileniyordu.</summary>
    protected override async void OnAppearing()
    {
        var hazir = Vm.VeriHazir;
        base.OnAppearing();
        if (hazir)
            await YenileAsync();
    }
}
