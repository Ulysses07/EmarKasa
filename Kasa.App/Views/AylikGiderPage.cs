using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class AylikGiderPage : TakipSayfasi<AylikGiderViewModel>
{
    public AylikGiderPage(AylikGiderViewModel vm) : base(vm, "Aylık Giderler", "Kira, maaş ve düzenli giderleri burada planlayın. Şablonlar kendiliğinden ödeme oluşturmaz; her ay gerçekleşen nakit / havale ödemesini siz kaydedersiniz.", vm.YukleAsync)
    {
        var ay = new HorizontalStackLayout { Spacing = 10, Children = { Tikla("Önceki ay", () => vm.AyDegistirAsync(-1)), Tikla("Sonraki ay", () => vm.AyDegistirAsync(1)) } };
        Govde.Add(Kart("Ayın giderleri", ay, Alan("Gösterilecek ay", Tarih(nameof(vm.AyTarihi))), Tikla("Seçilen ayı göster", vm.YukleAsync),
            Goster(Metin("Ay seçimi değişti. Kayıtları ve ödeme tutarlarını yenilemek için seçilen ayı gösterin."), nameof(vm.AySecimiDegisti)), Bagli(nameof(vm.AyOzeti), 18),
            Liste<AylikGiderSatiri>(nameof(vm.Kayitlar), async s =>
            {
                if (s.Veri.Durum == "Odendi") { var oturum = vm.OturumNesli; var gerekce = await GerekceAsync("Aylık gider ödemesini iptal et"); if (!string.IsNullOrWhiteSpace(gerekce)) await vm.IptalAsync(s, gerekce, oturum); }
                else vm.OdemeSec(s);
            }, "Ödemeyi aç / iptal et", _ => vm.EditorMu)));
        Govde.Add(Editor(Goster(Kart("Bu ayın ödemesini kaydet", Bagli(nameof(vm.OdemeEtkisi)), Alan("Gerçek ödeme tarihi", Tarih(nameof(vm.OdemeTarihi))), Alan("Not / dekont açıklaması", Girdi(nameof(vm.OdemeNotu))),
            Onay("Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.", nameof(vm.OdemeOnay)), Dugme("Nakit / havale ödemesini kaydet", nameof(vm.OdeCommand))), nameof(vm.OdemeSecili))));
        Govde.Add(Kart("Düzenli gider şablonları", Metin("Kaydedilmiş ödemelerin tutarı ve kanal payları sabit kalır. Değişiklikler seçilen geçerlilik ayından itibaren uygulanır."),
            Liste<AylikSablonSatiri>(nameof(vm.Sablonlar), s => { vm.SablonSec(s); return Task.CompletedTask; }, "Şablonu düzenle", _ => vm.EditorMu), Editor(Dugme("Yeni şablon", nameof(vm.YeniCommand)))));
        Govde.Add(Editor(Kart("Şablon bilgileri", Bagli(nameof(vm.SablonBasligi)), Alan("Ad / açıklama", Girdi(nameof(vm.Ad))), Alan("Gider türü", Secim(nameof(vm.Turler), nameof(vm.Tur))), Alan("Aylık tutar", Girdi(nameof(vm.Tutar), true)),
            Alan("Ödeme günü (1–31; kısa ayda son gün)", Girdi(nameof(vm.OdemeGunu), sayi: true)), Alan("Dağılım biçimi — seçin", Secim(nameof(vm.DagilimTurleri), nameof(vm.DagilimTuru))),
            Goster(KanalSecimleri(nameof(vm.KanalSecimleri)), nameof(vm.EsitDagilim)), Goster(Paylar(vm.Paylar, vm.PayEkle), nameof(vm.OzelDagilim)),
            Metin("Yalnız genel kasa seçilirse kanallar değişmez. Eşit dağılım yalnız seçtiğiniz kanalları kullanır; yeni kanallar sonradan otomatik eklenmez."),
            Alan("Geçerlilik ayı (cari ay veya sonrası)", Tarih(nameof(vm.GecerliAy))), Onay("Şablon aktif (kapatırsanız sonraki planlar arşivlenir)", nameof(vm.Aktif)),
            Dugme("Şablonu kaydet — ödeme oluşturmaz", nameof(vm.SablonKaydetCommand)))));
    }
}
