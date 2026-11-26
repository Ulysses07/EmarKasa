using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

// İşlem hafızası (docs/specs/2026-11-25-islem-hafizasi.md): hazır öneriler kutusu, yok sayma önerisi, hafıza listesi ve unutma.
public sealed partial class EkstreAktarmaPage
{
    private View HazirBolumu(EkstreAktarmaViewModel vm)
    {
        var ozet = Bagli(nameof(vm.HazirOzeti));
        ozet.FontAttributes = FontAttributes.Bold;
        var yokSay = Ikincil(Tikla("", YokSayOnerileriniUygulaAsync));
        yokSay.SetBinding(Button.TextProperty, nameof(vm.YokSayOneriDugmesi));
        var hazir = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                ozet,
                Bagli(nameof(vm.HazirAciklamasi)),
            }
        };
        return new VerticalStackLayout { Spacing = 10, Children = { Goster(hazir, nameof(vm.HazirVar)), Goster(yokSay, nameof(vm.YokSayOneriVar)) } };
    }

    private async Task YokSayOnerileriniUygulaAsync()
    {
        if (await DisplayAlertAsync("Satırları yok say", $"Daha önce yok saydığınız hareketlerle aynı {Vm.YokSayOneriSayisi} satır kaydedilmeden kapanacak; kasa değişmez. "
            + "Kapanan sekmesinden geri alabilirsiniz.", "Yok say", "Vazgeç"))
            await Vm.OnerilenleriYokSayAsync();
    }

    private Task SatirHafizasiniUnutAsync(EkstreSatirEditor s) => Vm.Belge is { } belge && s.Oneri?.Hafiza is { } hafiza
        ? HafizaUnutAsync(belge.Kaynak, hafiza.Anahtar, s.Kaynak.Yon) : Task.CompletedTask;

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
            Metin("Kaydettiğiniz ve yok saydığınız satırlar kendiliğinden öğrenilir; aynı açıklama ve yöndeki yeni satır öyle doldurulur. "
                + "Hafıza kayıt oluşturmaz. Kişisel kural eşleşirse kural önceliklidir."),
            dugme, Goster(liste, nameof(Vm.HafizaAcik)))));
    }
}
