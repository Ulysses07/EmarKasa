using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class AylikGiderPage : TakipSayfasi<AylikGiderViewModel>
{
    public AylikGiderPage(AylikGiderViewModel vm) : base(vm, "Aylık Giderler",
        "Kira, maaş ve düzenli giderleri burada planlayın. Şablonlar kendiliğinden ödeme oluşturmaz; her ay gerçekleşen nakit / havale ödemesini siz kaydedersiniz.",
        vm.YukleAsync)
    {
        var ay = new HorizontalStackLayout { Spacing = 10, Children = { Tikla("Önceki ay", () => vm.AyDegistirAsync(-1)), Tikla("Sonraki ay", () => vm.AyDegistirAsync(1)) } };
        // AG-02: ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" (iptal gerekçe penceresiyle onay ister).
        Govde.Add(Kart("Ayın giderleri", ay, Alan("Gösterilecek ay", Tarih(nameof(vm.AyTarihi))), Tikla("Seçilen ayı göster", vm.YukleAsync),
            Goster(Metin("Ay seçimi değişti. Kayıtları ve ödeme tutarlarını yenilemek için seçilen ayı gösterin."), nameof(vm.AySecimiDegisti)), BagliBuyuk(nameof(vm.AyOzeti)),
            Liste<AylikGiderSatiri>(nameof(vm.Kayitlar), async s =>
            {
                if (s.OdendiMi)
                    await GerekceyleAsync("Aylık gider ödemesini iptal et", (gerekce, oturum) => vm.IptalAsync(s, gerekce, oturum));
                else
                    vm.OdemeSec(s);
            }, "Öde", _ => vm.EditorMu, AylikGiderViewModel.SatirDugmesi)));
        Govde.Add(Goster(Kart("İptal edilen ödemeler", Metin("İptal edilen ödeme kasaya yansımaz ve ay toplamlarına girmez; planı yukarıda yeniden ödeme bekler."),
            Liste<AylikGiderIptalSatiri>(nameof(vm.Iptaller))), nameof(vm.IptalVar)));

        // Ödeme formu: genel hata formun en üstünde, onay hatası onay kutusunun altında; seçilince görünür yere kaydırılır (AG-02).
        const string o = nameof(vm.OdemeHatalari);
        var odemeHataKutusu = FormHatasi(o + ".Genel");
        var odemeFormu = Kart("Bu ayın ödemesini kaydet", odemeHataKutusu, Bagli(nameof(vm.OdemeEtkisi)),
            Alan("Gerçek ödeme tarihi", Tarih(nameof(vm.OdemeTarihi))), Alan("Not / dekont açıklaması", Girdi(nameof(vm.OdemeNotu))),
            Alan("Ödeme onayı", Onay("Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.", nameof(vm.OdemeOnay)), o, nameof(vm.OdemeOnay)),
            Dugme("Nakit / havale ödemesini kaydet", nameof(vm.OdeCommand)));
        Govde.Add(Editor(Goster(odemeFormu, nameof(vm.OdemeSecili))));

        Govde.Add(Kart("Düzenli gider şablonları", Metin("Kaydedilmiş ödemelerin tutarı ve kanal payları sabit kalır. Değişiklikler seçilen geçerlilik ayından itibaren uygulanır."),
            Liste<AylikSablonSatiri>(nameof(vm.Sablonlar), vm.SablonSecAsync, "Şablonu düzenle", _ => vm.EditorMu),
            Editor(Dugme("Yeni şablon", nameof(vm.YeniCommand)))));

        // Şablon formu: başlık modu söyler; genel hata formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1).
        const string h = nameof(vm.SablonHatalari);
        var sablonHataKutusu = FormHatasi(h + ".Genel");
        var sablonFormu = Kart("Şablon bilgileri", Bagli(nameof(vm.SablonBasligi)), sablonHataKutusu,
            Alan("Ad / açıklama", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)),
            Alan("Gider türü", Secim(nameof(vm.Turler), nameof(vm.Tur)), h, nameof(vm.Tur)),
            Alan("Aylık tutar", Girdi(nameof(vm.Tutar), true), h, nameof(vm.Tutar)),
            Alan("Ödeme günü (1–31; kısa ayda son gün)", Girdi(nameof(vm.OdemeGunu), sayi: true), h, nameof(vm.OdemeGunu)),
            Alan("Dağılım biçimi — seçin", Secim(nameof(vm.DagilimTurleri), nameof(vm.DagilimTuru)), h, nameof(vm.DagilimTuru)),
            Goster(Alan("Kanallar", KanalSecimleri(nameof(vm.KanalSecimleri)), h, nameof(vm.KanalSecimleri)), nameof(vm.EsitDagilim)),
            Goster(Paylar(vm.Paylar, vm.PayEkle), nameof(vm.OzelDagilim)),
            Metin("Yalnız genel kasa seçilirse kanallar değişmez. Eşit dağılım yalnız seçtiğiniz kanalları kullanır; yeni kanallar sonradan otomatik eklenmez."),
            Alan("Geçerlilik ayı (cari ay veya sonrası)", Tarih(nameof(vm.GecerliAy))), Onay("Şablon aktif (kapatırsanız sonraki planlar arşivlenir)", nameof(vm.Aktif)),
            Dugme("Şablonu kaydet — ödeme oluşturmaz", nameof(vm.SablonKaydetCommand)));
        Govde.Add(Editor(sablonFormu));

        vm.OdemeHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(odemeFormu, vm.OdemeHatalari, odemeHataKutusu);
        vm.SablonHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(sablonFormu, vm.SablonHatalari, sablonHataKutusu);
        // Aynı satıra yeniden "Öde" basılınca SeciliOdeme değişmez; bu yüzden PropertyChanged'e güvenmeyip
        // OdemeSec'in kendi olayına bağlanır (görev 14-19 incelemesi).
        vm.OdemeSecIstendi += (_, _) => Gorunur.Yap(odemeFormu, KaydirmaHesabi.FormKaydirmasi);
    }
}
