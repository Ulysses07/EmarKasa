using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class AylikGiderPage : TakipSayfasi<AylikGiderViewModel>
{
    public AylikGiderPage(AylikGiderViewModel vm) : base(vm, "Aylık giderler", vm.YukleAsync)
    {
        var ay = new Controls.UyumluSatir { Spacing = 10, Children = { Tikla("Önceki ay", () => vm.AyDegistirAsync(-1)), Tikla("Sonraki ay", () => vm.AyDegistirAsync(1)) } };
        // AG-02: ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" (iptal gerekçe penceresiyle onay ister).
        Govde.Add(Kart("Ayın giderleri", ay, Alan("Gösterilecek ay", Tarih(nameof(vm.AyTarihi))), Tikla("Seçilen ayı göster", vm.YukleAsync),
            Goster(Metin("Listeyi görmek için “Seçilen ayı göster”e basın."), nameof(vm.AySecimiDegisti)), BagliBuyuk(nameof(vm.AyOzeti)),
            Liste<AylikGiderSatiri>(nameof(vm.Kayitlar), async s =>
            {
                if (!vm.ListeGuncelMi())   // soluk (eski) listede iki düğme de aynı iletiyle durur
                    return;
                if (s.OdendiMi)
                    await GerekceyleAsync("Aylık gider ödemesini iptal et", (gerekce, oturum) => vm.IptalAsync(s, gerekce, oturum));
                else
                    vm.OdemeSec(s);
            }, "Öde", _ => vm.EditorMu, AylikGiderViewModel.SatirDugmesi)));
        Govde.Add(Goster(Kart("İptal edilen ödemeler",
            Liste<AylikGiderIptalSatiri>(nameof(vm.Iptaller))), nameof(vm.IptalVar)));

        // Ödeme formu: genel hata formun en üstünde, onay hatası onay kutusunun altında; seçilince görünür yere kaydırılır (AG-02).
        const string o = nameof(vm.OdemeHatalari);
        var odemeHataKutusu = FormHatasi(o + ".Genel");
        var odemeFormu = Kart("Bu ayın ödemesini kaydet", odemeHataKutusu, Bagli(nameof(vm.OdemeEtkisi)),
            Alan("Gerçek ödeme tarihi", Tarih(nameof(vm.OdemeTarihi))), Alan("Not", Girdi(nameof(vm.OdemeNotu))),
            Alan("Ödeme onayı", Onay("Ödeme yapıldı, tutar ve kanal payları doğru.", nameof(vm.OdemeOnay)), o, nameof(vm.OdemeOnay)),
            Birincil(Dugme("Ödemeyi kaydet", nameof(vm.OdeCommand))));
        Govde.Add(Editor(Goster(odemeFormu, nameof(vm.OdemeSecili))));

        Govde.Add(Kart("Düzenli gider şablonları",
            Liste<AylikSablonSatiri>(nameof(vm.Sablonlar), vm.SablonSecAsync, "Şablonu düzenle", _ => vm.EditorMu),
            Editor(Dugme("Yeni şablon", nameof(vm.YeniCommand)))));

        // Şablon formu: başlık modu söyler; genel hata formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1).
        const string h = nameof(vm.SablonHatalari);
        var sablonHataKutusu = FormHatasi(h + ".Genel");
        var sablonFormu = Kart("Şablon bilgileri", Bagli(nameof(vm.SablonBasligi)), sablonHataKutusu,
            Alan("Ad", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)),
            Alan("Gider türü", Secim(nameof(vm.Turler), nameof(vm.Tur)), h, nameof(vm.Tur)),
            Alan("Aylık tutar", Girdi(nameof(vm.Tutar), true), h, nameof(vm.Tutar)),
            Alan("Ödeme günü", Girdi(nameof(vm.OdemeGunu), sayi: true), h, nameof(vm.OdemeGunu)),
            Alan("Dağılım", Secim(nameof(vm.DagilimTurleri), nameof(vm.DagilimTuru)), h, nameof(vm.DagilimTuru)),
            Goster(Alan("Kanallar", KanalSecimleri(nameof(vm.KanalSecimleri)), h, nameof(vm.KanalSecimleri)), nameof(vm.EsitDagilim)),
            Goster(Paylar(vm.Paylar, vm.PayEkle), nameof(vm.OzelDagilim)),
            Yardim("Yalnız genel kasa seçilirse kanallar değişmez. Eşit dağılım yalnız seçtiğiniz kanalları kullanır; sonradan eklenen kanal şablona girmez. "
                + "Kaydedilmiş ödemelerin tutarı ve kanal payları sabit kalır; değişiklik seçilen aydan başlar. Kısa ayda ödeme günü ayın son günüdür."),
            Alan("Geçerli olacağı ay", Tarih(nameof(vm.GecerliAy))), Onay("Şablon etkin", nameof(vm.Aktif)),
            Dugme("Şablonu kaydet", nameof(vm.SablonKaydetCommand)));
        Govde.Add(Editor(sablonFormu));

        vm.OdemeHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(odemeFormu, vm.OdemeHatalari, odemeHataKutusu);
        vm.SablonHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(sablonFormu, vm.SablonHatalari, sablonHataKutusu);
        // Aynı satıra yeniden "Öde" basılınca SeciliOdeme değişmez; bu yüzden PropertyChanged'e güvenmeyip
        // OdemeSec'in kendi olayına bağlanır (görev 14-19 incelemesi).
        vm.OdemeSecIstendi += (_, _) => Gorunur.Yap(odemeFormu, KaydirmaHesabi.FormKaydirmasi);
        // Y-1: kayıt ya da iptalden sonra form kapanır ve sayfa kısalır; başarı iletisi (sayfa başında, ayın güncellenen satırı
        // hemen altında) görünür yere kaydırılır.
        vm.OdemeSonucuGosterIstendi += (_, _) => Gorunur.Yap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
    }
}
