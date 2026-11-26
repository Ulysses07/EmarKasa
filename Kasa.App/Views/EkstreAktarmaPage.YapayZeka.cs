using Kasa.App.Core;
using Kasa.Core.Kodlar;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

// Yapay zekâ (docs/specs/2026-11-25-yapay-zeka-onerileri.md): durum ve ölçüm kartı. Sorma ve gönderilen metin hareket penceresindedir.
public sealed partial class EkstreAktarmaPage
{
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
