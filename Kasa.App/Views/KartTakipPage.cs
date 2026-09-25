using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class KartTakipPage : TakipSayfasi<KartTakipViewModel>, IQueryAttributable
{
    private int? _istenenKartId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KartId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0) _istenenKartId = id;
    }
    protected override async void OnAppearing()
    {
        if (_istenenKartId is { } id)
        {
            await Vm.YukleAsync();
            if (Vm.VeriHazir && Vm.Hata is null && Vm.IdIleSec(id)) _istenenKartId = null;
        }
        base.OnAppearing();
    }
    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları", "Yeni takipte kartla harcama nakit çıkışı oluşturmaz; kasa kaydettiğiniz ödeme ile azalır. Geçiş yapılmamış eski kartlarda önceki ay sonu kuralı korunur.", vm.YukleAsync)
    {
        var ozet = Kart("Kart ayrıntısı", Bagli(nameof(vm.KartOzeti), 18));
        Govde.Add(Kart("Kartlar", Liste<KartTakipSatiri>(nameof(vm.Kartlar), async s => { vm.SecCommand.Execute(s); await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true); }), Editor(Dugme("Yeni kart", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        Govde.Add(Goster(Kart("Kanalların kalan kart borcu", Bagli(nameof(vm.KanalBorcOzeti)), Metin("Bu tutarlar mevcut kasadan düşülmüş değildir. Kasa, kaydedilen kart ödemesiyle değişir.")), nameof(vm.KartSecili)));
        Govde.Add(Editor(Kart("Kart bilgileri", Alan("Kart / banka adı", Girdi(nameof(vm.Ad))), Alan("Limit", Girdi(nameof(vm.Limit), true)),
            Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true)), Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true)),
            Goster(new VerticalStackLayout { Spacing = 12, Children = { Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true)), Metin("Açılış borcunun bilinen kanal paylarını girin. Bilinmeyen dağılım tahmin edilmez."), Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)) } }, nameof(vm.YeniKart)),
            Dugme("Kartı kaydet", nameof(vm.KaydetCommand)))));
        var gecis = Kart("Eski kartı yeni takibe al", Metin("Geçmiş ay sonu çıkışları yeniden kasadan düşmemelidir. Kalan borcu ve kasada daha önce sayılmış kısmı kontrol ederek önizlemeyi alın."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), Alan("Kalan kart borcu", Girdi(nameof(vm.GecisKalanBorc), true)), Alan("Kasada önceden sayılan tutar", Girdi(nameof(vm.OncedenSayilan), true)),
            Paylar(vm.GecisPaylari, () => vm.PayEkle(vm.GecisPaylari)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))), Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)),
            Onay("Gösterilen kasa ve kanal etkisini inceledim; geçişi onaylıyorum.", nameof(vm.GecisOnay)), Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand)));
        Govde.Add(Editor(Goster(gecis, nameof(vm.EskiTakip))));
        Govde.Add(Goster(Kart("Ekstreler", Liste<EkstreSatiri>(nameof(vm.Ekstreler), s => { vm.EkstreSecCommand.Execute(s); return Task.CompletedTask; }, "Tarih / asgari ödeme", _ => vm.EditorMu && vm.YeniTakip)), nameof(vm.KartSecili)));
        Govde.Add(Editor(Goster(Kart("Ekstre bilgisi", Metin("Asgari ödeme ve tarihler bankanın ekstresinden girilir; uygulama oran veya tatil günü tahmini yapmaz."), Alan("Son ödeme tarihi", Tarih(nameof(vm.EkstreSonOdeme))),
            Onay("Banka asgari ödeme tutarı girildi", nameof(vm.AsgariVar)), Alan("Asgari ödeme", Girdi(nameof(vm.AsgariTutar), true)), Alan("Açıklama", Girdi(nameof(vm.Gerekce))), Dugme("Ekstreyi kaydet", nameof(vm.EkstreKaydetCommand))), nameof(vm.DuzenlenenEkstre), true)));
        var odeme = Kart("Kart ödemesi kaydet", Alan("Tarih", Tarih(nameof(vm.OdemeTarihi))), Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true)), Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
            Tikla("En eski açık ekstrelere dağıt", () => { vm.OdemeEkstresi = null; return Task.CompletedTask; }), Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu))),
            Dugme("Ödeme ve kanal paylarını göster", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)), Dugme("Ödemeyi kaydet", nameof(vm.OdemeKaydetCommand)), Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));
        Govde.Add(Editor(Goster(odeme, nameof(vm.YeniTakip))));
        Govde.Add(Editor(Goster(Kart("Faiz / masrafı kalan borca dağıt", Metin("Bankanın bildirdiği tutarı girin. Seçilen kesilmiş ekstre ve önceki ekstrelerin kalan borcuna göre kanallara dağıtılır; ileri taksitler ağırlığa katılmaz. Bilinmeyen kanal payı varsa kaydetmeden önce düzeltilmelidir."),
            Alan("Kesilmiş açık ekstre", Secim(nameof(vm.MasrafEkstreleri), nameof(vm.MasrafEkstresi))), Alan("Tarih", Tarih(nameof(vm.MasrafTarihi))), Alan("Bankanın bildirdiği faiz / masraf", Girdi(nameof(vm.MasrafTutari), true)), Alan("Açıklama", Girdi(nameof(vm.MasrafAciklama))),
            Dugme("Kanal dağılımını göster", nameof(vm.MasrafOnizleCommand)), Bagli(nameof(vm.MasrafOnizleme)), Dugme("Gösterilen masrafı kaydet", nameof(vm.MasrafKaydetCommand))), nameof(vm.YeniTakip))));
        var harcama = Kart("Bağımsız kart hareketi", Metin("Alışlar veya İşlemler'den bu karta bağlanan harcamayı tekrar girmeyin. İade için eksi tutar ve tek taksit kullanın. Kalan borca dağıtılacak faiz / masrafı yukarıdaki ayrı bölümden girin."),
            Alan("Tarih", Tarih(nameof(vm.HarcamaTarihi))), Alan("Açıklama", Girdi(nameof(vm.HarcamaAciklama))), Alan("Tutar (iade için eksi)", Girdi(nameof(vm.HarcamaTutari), true)), Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)),
            Onay("İlk hesap kesim tarihini belirle", nameof(vm.IlkKesimVar)), Goster(Alan("İlk kesim tarihi", Tarih(nameof(vm.IlkKesimTarihi))), nameof(vm.IlkKesimVar)),
            Goster(Alan("İade edilen harcama (kanal payları kaynaktan alınır)", Secim(nameof(vm.IadeKaynaklari), nameof(vm.IadeKaynagi), "Baslik")), nameof(vm.IadeGirisi)),
            Goster(new VerticalStackLayout { Spacing = 10, Children = { Metin("Bilinen kanal paylarını girin. Taksit toplamı borca ikinci kez eklenmez."), Paylar(vm.HarcamaPaylari, () => vm.PayEkle(vm.HarcamaPaylari)) } }, nameof(vm.HarcamaGirisi)), Dugme("Kart hareketini kaydet", nameof(vm.HarcamaKaydetCommand)), Benzerlik(nameof(vm.HarcamaBenzerlik), nameof(vm.HarcamayiAyriKaydetCommand)));
        Govde.Add(Editor(Goster(harcama, nameof(vm.YeniTakip))));
        Govde.Add(Goster(Kart("Harcamalar ve iadeler", Liste<HarcamaSatiri>(nameof(vm.Harcamalar), async s => { if (!vm.EditorMu) return; if (s.Veri.EkstreKayitId is { } id) { await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}"); return; } var reason = await GerekceAsync("Kart hareketini iptal et"); if (!string.IsNullOrWhiteSpace(reason)) { vm.Gerekce = reason; await vm.HarcamaIptalAsync(s); } }, "Hareketi iptal et", s => vm.EditorMu && vm.YeniTakip && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null), s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Hareketi iptal et")), nameof(vm.KartSecili)));
        Govde.Add(Goster(Kart("Kaydedilen ödemeler", Liste<KartOdemeSatiri>(nameof(vm.Odemeler), async s => { if (!vm.EditorMu) return; if (s.Veri.EkstreKayitId is { } id) { await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}"); return; } var reason = await GerekceAsync("Kart ödemesini iptal et"); if (!string.IsNullOrWhiteSpace(reason)) { vm.Gerekce = reason; await vm.OdemeIptalAsync(s); } }, "Ödemeyi iptal et", s => vm.EditorMu && vm.YeniTakip && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null), s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Ödemeyi iptal et")), nameof(vm.KartSecili)));
        Govde.Add(Editor(Goster(Kart("Kullanım durumu", Metin("Kartı pasife almak geçmiş hareketleri silmez."), Tikla("Aktif / pasif durumunu değiştir", async () => { var reason = await GerekceAsync("Kartın kullanım durumunu değiştir"); if (!string.IsNullOrWhiteSpace(reason)) { vm.Gerekce = reason; await vm.DurumDegistirAsync(); } })), nameof(vm.KartSecili))));
    }
}
