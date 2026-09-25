using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>
{
    public KrediTakipPage(KrediTakipViewModel vm) : base(vm, "Krediler", "Taksitler tarihlerinde genel kasa ve sabit kanal paylarından otomatik düşer. Bu, bankaya ödemenin doğrulandığı anlamına gelmez.", vm.YukleAsync)
    {
        var ozet = Kart("Kredi ayrıntısı", Bagli(nameof(vm.KrediOzeti), 18));
        Govde.Add(Kart("Krediler", Liste<KrediTakipSatiri>(nameof(vm.Krediler), async s => { vm.SecCommand.Execute(s); await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true); }), Editor(Dugme("Yeni kredi / mevcut krediyi ekle", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        Govde.Add(Editor(Goster(Kart("Kredi ekle", Alan("Banka / kredi adı", Girdi(nameof(vm.Ad))), Onay("Önceden çekilmiş mevcut kredi: yeni kasa girişi oluşturma", nameof(vm.MevcutKredi)),
            Alan("Çekilen tutar", Girdi(nameof(vm.CekilenTutar), true)), Alan("Çekim tarihi", Tarih(nameof(vm.CekimTarihi))), Alan("İlk / kalan ilk taksit tarihi", Tarih(nameof(vm.IlkTaksitTarihi))),
            Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)), Alan("Aylık taksit tutarı", Girdi(nameof(vm.AylikOdeme), true)),
            Metin("Tek kanal seçerseniz tutarın tamamı o kanala gider. Birden fazla kanalda kredi girişi ve her taksit eşit, kuruşları korunarak bölünür. Seçili kanallar sonradan kendiliğinden değişmez."), KanalSecimleri(nameof(vm.Kanallar)), Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)),
            Metin("Yeni kredi genel kasaya bir kez girer; kanal payları aynı girişin dağılımıdır."), Dugme("Krediyi kaydet", nameof(vm.KaydetCommand))), nameof(vm.YeniKredi))));
        Govde.Add(Editor(Goster(Kart("Eski krediyi yeni takibe al", Metin("Eski kredi çekimi ikinci kez genel kasaya girmez. Geçmiş kanal bakiyeleri sessizce değiştirilmez; ileri taksitler seçtiğiniz kanallara bölünür."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), KanalSecimleri(nameof(vm.Kanallar)), Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))),
            Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)), Onay("Kasa ve kanal farkını inceledim; geçişi onaylıyorum.", nameof(vm.GecisOnay)), Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand))), nameof(vm.EskiTakip))));
        Govde.Add(Goster(Kart("Taksit planı", Metin("Kalan planlı ödeme, kalan anapara değildir. Bankadan anapara/faiz ayrımı girilmediği için anapara tahmini yapılmaz."),
            Liste<TaksitSatiri>(nameof(vm.Taksitler), s => { vm.TaksitSecCommand.Execute(s); return Task.CompletedTask; }, "Planı düzenle / not ekle", _ => vm.EditorMu && vm.YeniTakip)), nameof(vm.KrediSecili)));
        var tarihTutar = new VerticalStackLayout { Spacing = 12, Children = { Alan("Tarih", Tarih(nameof(vm.TaksitTarihi))), Alan("Tutar", Girdi(nameof(vm.TaksitTutari), true)), Onay("Bu taksidi plan değişikliğiyle iptal et", nameof(vm.TaksitIptal)) } };
        tarihTutar.SetBinding(IsEnabledProperty, nameof(vm.TaksitDuzenlenebilir));
        Govde.Add(Editor(Goster(Kart("Taksit bilgisi", Metin("Kasaya işlenmiş taksidin tarih/tutarı değişmez. Not veya dekont referansı eklemek ikinci çıkış oluşturmaz."), tarihTutar,
            Alan("Not / dekont referansı", Girdi(nameof(vm.TaksitNotu))), Alan("Değişiklik açıklaması", Girdi(nameof(vm.Gerekce))), Dugme("Taksiti kaydet", nameof(vm.TaksitKaydetCommand))), nameof(vm.DuzenlenenTaksit), true)));
        Govde.Add(Editor(Goster(Kart("Erken kapama", Alan("Kapama tarihi", Tarih(nameof(vm.KapatmaTarihi))), Alan("Bankanın kapama tutarı", Girdi(nameof(vm.KapatmaTutari), true)),
            Alan("Kapama açıklaması", Girdi(nameof(vm.Gerekce))), Bagli(nameof(vm.KapatmaOzeti)), Onay("Bu çıkışı ve yerine geçen ileri taksitlerin iptalini onaylıyorum.", nameof(vm.KapatmaOnay)), Dugme("Erken kapamayı kaydet", nameof(vm.KapatCommand))), nameof(vm.YeniTakip))));
        Govde.Add(Editor(Goster(Kart("Arşiv", Metin("Arşiv geçmişi ve planın kasa etkisini silmez."), Tikla("Arşiv / aktif durumunu değiştir", async () => { var reason = await GerekceAsync("Kredi arşiv durumunu değiştir"); if (!string.IsNullOrWhiteSpace(reason)) { vm.Gerekce = reason; await vm.DurumDegistirAsync(); } })), nameof(vm.KrediSecili))));
    }
}
