using Kasa.App.Core;
using Kasa.Core.Kodlar;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): sor düğmesi, gönderilecek metin, durum ve ölçüm.
public sealed partial class EkstreAktarmaPage
{
    private View YapayZekaBolumu(EkstreAktarmaViewModel vm)
    {
        var sor = Tikla("Yanıtsız satırları yeniden sor", YanitsizlariSorAsync);
        var goster = Ikincil(Dugme("", nameof(vm.GonderilecekAcKapatCommand)));
        goster.SetBinding(Button.TextProperty, nameof(vm.GonderilecekDugmesi));
        var satirlar = new VerticalStackLayout { Spacing = 4 };
        BindableLayout.SetItemsSource(satirlar, vm.GonderilecekSatirlar);
        BindableLayout.SetItemTemplate(satirlar, new DataTemplate(() =>
        {
            var etiket = Etiket("LblTakipKucuk");
            etiket.SetBinding(Label.TextProperty, ".");
            return etiket;
        }));
        var bolum = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Metin("Kural ve hafızanın tanımadığı satırlar yapay zekâya sorulabilir. IBAN, kart ve bütün sayılar, kişi, firma ve yer adları gizlenir; belge gönderilmez. "
                    + "Öneri satırı doldurur, işaretlemez ve kaydetmez."),
                Eylemler(sor, goster), Goster(satirlar, nameof(vm.GonderilecekAcik)),
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Goster(bolum, nameof(vm.YanitsizSorulabilir)), Dolu(Etiket("LblTakipMesaj"), nameof(vm.YapayZekaMesaji)), FormHatasi(nameof(vm.YapayZekaHatasi))
            }
        };
    }

    private async Task YanitsizlariSorAsync()
    {
        if (await Vm.YapayZekaOnizleAsync() is { } o && await DisplayAlertAsync("Yapay zekâya sor", Vm.YapayZekaOnayi(o), "Gönder", "Vazgeç"))
            await Vm.YanitsizlariSorAsync();
    }

    private async Task YapayZekaOlcAsync(string kaynak)
    {
        if (await DisplayAlertAsync("Ölçüm", "Geçmiş kararlarınızın yarısı gizlenip maskeli olarak sorulur ve sizin kararınızla karşılaştırılır. "
            + "Hiçbir kayıt değişmez; bir sentten az tutar.", "Ölç", "Vazgeç"))
            await Vm.YapayZekaOlcAsync(kaynak);
    }

    private void YapayZekaKartiniKur()
    {
        var olcum = Eylemler(Ikincil(Tikla("Banka kararlarıyla ölç", () => YapayZekaOlcAsync(EkstreKaynaklari.Banka))),
            Ikincil(Tikla("Kart kararlarıyla ölç", () => YapayZekaOlcAsync(EkstreKaynaklari.Kart))));
        var kart = Kart("Yapay zekâ", Bagli(nameof(Vm.YapayZekaMetni)), Goster(olcum, nameof(Vm.YapayZekaAcik)), FormHatasi(nameof(Vm.OlcumHatasi)));
        Govde.Add(Editor(Goster(kart, nameof(Vm.YapayZekaGorunur))));
    }
}
