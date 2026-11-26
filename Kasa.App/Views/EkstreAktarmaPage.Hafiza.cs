using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

// İşlem hafızası (docs/specs/2026-11-25-islem-hafizasi.md): hafıza listesi ve unutma. Satırların hazır önerileri hareket penceresindedir.
public sealed partial class EkstreAktarmaPage
{
    private async Task HafizaUnutAsync(string kaynak, string anahtar, string yon)
    {
        if (await DisplayAlertAsync("Hafızayı unut", $"{anahtar} · {EkstreSatirEditor.YonAdi(yon)}\nBu hareket bundan sonra kendiliğinden doldurulmaz. Kayıtlar ve raporlar değişmez; "
            + "yeniden kaydettiğinizde yeniden öğrenilir.", "Unut", "Vazgeç"))
            await Vm.HafizaUnutAsync(kaynak, anahtar, yon);
    }

    private void HafizaBolumunuKur()
    {
        var dugme = Ikincil(Dugme("", nameof(Vm.HafizaAcKapatCommand)));
        dugme.SetBinding(Button.TextProperty, nameof(Vm.HafizaDugmesi));
        var liste = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Bagli(nameof(Vm.HafizaDurumu)),
                Liste<EkstreHafizaSatiri>(nameof(Vm.Hafiza), s => HafizaUnutAsync(s.Veri.Kaynak, s.Veri.Anahtar, s.Veri.Yon), "Unut", bosMetin: false,
                    aciklama: s => $"Hafızadan unut: {s.Baslik}"),
            }
        };
        Govde.Add(Editor(Kart("İşlem hafızası",
            Yardim("Kaydettiğiniz ve yok saydığınız satırlar öğrenilir; aynı açıklama ve yöndeki yeni satır öyle doldurulur. Kişisel kural "
                + "eşleşirse kural önceliklidir."),
            dugme, Goster(liste, nameof(Vm.HafizaAcik)))));
    }
}
