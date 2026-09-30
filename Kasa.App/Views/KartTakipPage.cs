using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed class KartTakipPage : TakipSayfasi<KartTakipViewModel>, IQueryAttributable
{
    private int? _istenenKartId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KartId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0)
            _istenenKartId = id;
    }
    protected override async void OnAppearing()
    {
        if (_istenenKartId is { } id)
        {
            await Vm.YukleAsync();
            if (Vm.VeriHazir && Vm.Hata is null && Vm.IdIleSec(id))
                _istenenKartId = null;
        }
        base.OnAppearing();
    }
    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları",
        "Yeni takipte kartla harcama nakit çıkışı oluşturmaz; kasa kaydettiğiniz ödeme ile azalır. Geçiş yapılmamış eski kartlarda önceki ay sonu kuralı korunur.",
        vm.YukleAsync)
    {
        var ozet = Kart("Kart ayrıntısı", BagliBuyuk(nameof(vm.KartOzeti)));
        Govde.Add(Kart("Kartlar",
            Liste<KartTakipSatiri>(nameof(vm.Kartlar), async s => { vm.SecCommand.Execute(s); await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true); }),
            Editor(Dugme("Yeni kart", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        // İlk sürüm geçiş kalıntısı (web'deki notice): tahmini kasa farkı varsa koyu kırmızı, yalnız düşüş tarihi farklıysa bilgi.
        var gecisUyarisi = Bagli(nameof(vm.GecisUyarisi));
        gecisUyarisi.FontAttributes = FontAttributes.Bold;
        gecisUyarisi.Triggers.Add(new DataTrigger(typeof(Label))
        {
            Binding = new Binding(nameof(vm.GecisUyarisiTehlikeli)),
            Value = true,
            Setters = { new Setter { Property = Label.TextColorProperty, Value = (Color)Application.Current!.Resources["KoyuKirmizi"] } }
        });
        Govde.Add(Goster(Kart("Geçiş uyarısı", gecisUyarisi), nameof(vm.GecisUyarisi), true));
        Govde.Add(Goster(Kart("Kanalların kalan kart borcu", Bagli(nameof(vm.KanalBorcOzeti)), Metin("Bu tutarlar mevcut kasadan düşülmüş değildir. Kasa, kaydedilen kart ödemesiyle değişir.")),
            nameof(vm.KartSecili)));
        Govde.Add(Editor(Kart("Kart bilgileri", Alan("Kart / banka adı", Girdi(nameof(vm.Ad))), Alan("Limit", Girdi(nameof(vm.Limit), true)),
            Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true)), Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true)),
            Goster(new VerticalStackLayout
            {
                Spacing = 12,
                Children = { Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true)),
            Metin("Açılış borcunun bilinen kanal paylarını girin. Bilinmeyen dağılım tahmin edilmez."),
            Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)) }
            },
            nameof(vm.YeniKart)),
            Dugme("Kartı kaydet", nameof(vm.KaydetCommand)))));
        // Kabul edilemez önizlemede (KabulEdilebilir=false) onay kutusu ve düğme kapalıdır; neden kırmızı yazılır.
        var gecisEngeli = BagliHata(nameof(vm.GecisEngeli));
        var gecisOnayi = Onay("Gösterilen kasa ve kanal etkisini inceledim; geçişi onaylıyorum.", nameof(vm.GecisOnay));
        gecisOnayi.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.GecisOnaylanabilir));
        // Sunucu farklı bir tutar önerirse alana kendiliğinden yazılmaz; web'deki gibi açık eylemle uygulanıp yeniden önizlenir.
        var gecisOnerisi = new Button { HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        gecisOnerisi.SetBinding(Button.TextProperty, nameof(vm.GecisOneriMetni));
        gecisOnerisi.SetBinding(Button.CommandProperty, nameof(vm.OnerilenleGecisOnizleCommand));
        var gecis = Kart("Eski kartı yeni takibe al",
            Metin("Bankanızdaki kalan borcu girin. Kasada önceden sayılan kısım, sistemin eski kuralla kasadan düştüğü/düşeceği borçtur; siz değiştirmedikçe alan kalan borç ile sistem kart borcunun küçüğünü izler. Önizleme farklı bir tutar önerirse \"Önerilen tutarla yeniden önizle\" ile uygulayabilirsiniz. Önerilenin altı yalnız açılış borcu kasadan ayrıca ödenecekse girilebilir. Girdiler değişirse geçiş, yeni önizleme alınmadan onaylanamaz. Bu işlem yeni harcama oluşturmaz."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), Alan("Kalan kart borcu", Girdi(nameof(vm.GecisKalanBorc), true)),
            Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.OncedenSayilan), true)),
            Paylar(vm.GecisPaylari, () => vm.PayEkle(vm.GecisPaylari)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))),
            Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)),
            Goster(gecisEngeli, nameof(vm.GecisEngeli), true), Goster(gecisOnerisi, nameof(vm.GecisOneriVar)), gecisOnayi, Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand)));
        Govde.Add(Editor(Goster(gecis, nameof(vm.EskiTakip))));
        Govde.Add(Goster(Kart("Ekstreler",
            Liste<EkstreSatiri>(nameof(vm.Ekstreler), s => { vm.EkstreSecCommand.Execute(s); return Task.CompletedTask; }, "Tarih / asgari ödeme", _ => vm.EditorMu && vm.YeniTakip)),
            nameof(vm.KartSecili)));
        Govde.Add(Editor(Goster(Kart("Ekstre bilgisi",
            Metin("Asgari ödeme ve tarihler bankanın ekstresinden girilir; uygulama oran veya tatil günü tahmini yapmaz."),
            Alan("Son ödeme tarihi", Tarih(nameof(vm.EkstreSonOdeme))),
            Onay("Banka asgari ödeme tutarı girildi", nameof(vm.AsgariVar)), Alan("Asgari ödeme", Girdi(nameof(vm.AsgariTutar), true)),
            Alan("Açıklama", Girdi(nameof(vm.Gerekce))), Dugme("Ekstreyi kaydet", nameof(vm.EkstreKaydetCommand))),
            nameof(vm.DuzenlenenEkstre), true)));
        var odeme = Kart("Kart ödemesi kaydet", Alan("Tarih", Tarih(nameof(vm.OdemeTarihi))), Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true)),
            Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
            Tikla("En eski açık ekstrelere dağıt", () => { vm.OdemeEkstresi = null; return Task.CompletedTask; }), Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu))),
            Dugme("Ödeme ve kanal paylarını göster", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)),
            Dugme("Ödemeyi kaydet", nameof(vm.OdemeKaydetCommand)), Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));
        Govde.Add(Editor(Goster(odeme, nameof(vm.YeniTakip))));
        Govde.Add(Editor(Goster(Kart("Faiz / masrafı kalan borca dağıt",
            Metin("Bankanın bildirdiği tutarı girin. Seçilen kesilmiş ekstre ve önceki ekstrelerin kalan borcuna göre kanallara dağıtılır; ileri taksitler ağırlığa katılmaz. Bilinmeyen kanal payı varsa kaydetmeden önce düzeltilmelidir."),
            Alan("Kesilmiş açık ekstre", Secim(nameof(vm.MasrafEkstreleri), nameof(vm.MasrafEkstresi))),
            Alan("Tarih", Tarih(nameof(vm.MasrafTarihi))), Alan("Bankanın bildirdiği faiz / masraf", Girdi(nameof(vm.MasrafTutari), true)),
            Alan("Açıklama", Girdi(nameof(vm.MasrafAciklama))),
            Dugme("Kanal dağılımını göster", nameof(vm.MasrafOnizleCommand)), Bagli(nameof(vm.MasrafOnizleme)), Dugme("Gösterilen masrafı kaydet", nameof(vm.MasrafKaydetCommand))),
            nameof(vm.YeniTakip))));
        var harcama = Kart("Bağımsız kart hareketi",
            Metin("Alışlar veya İşlemler'den bu karta bağlanan harcamayı tekrar girmeyin. İade için eksi tutar ve tek taksit kullanın. Kalan borca dağıtılacak faiz / masrafı yukarıdaki ayrı bölümden girin."),
            Alan("Tarih", Tarih(nameof(vm.HarcamaTarihi))), Alan("Açıklama", Girdi(nameof(vm.HarcamaAciklama))),
            Alan("Tutar (iade için eksi)", Girdi(nameof(vm.HarcamaTutari), true)), Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)),
            Onay("İlk hesap kesim tarihini belirle", nameof(vm.IlkKesimVar)), Goster(Alan("İlk kesim tarihi", Tarih(nameof(vm.IlkKesimTarihi))), nameof(vm.IlkKesimVar)),
            Goster(Alan("İade edilen harcama (kanal payları kaynaktan alınır)", Secim(nameof(vm.IadeKaynaklari), nameof(vm.IadeKaynagi), "Baslik")), nameof(vm.IadeGirisi)),
            Goster(new VerticalStackLayout
            {
                Spacing = 10,
                Children = { Metin("Bilinen kanal paylarını girin. Taksit toplamı borca ikinci kez eklenmez."), Paylar(vm.HarcamaPaylari, () => vm.PayEkle(vm.HarcamaPaylari)) }
            },
            nameof(vm.HarcamaGirisi)),
            Dugme("Kart hareketini kaydet", nameof(vm.HarcamaKaydetCommand)),
            Benzerlik(nameof(vm.HarcamaBenzerlik), nameof(vm.HarcamayiAyriKaydetCommand)));
        Govde.Add(Editor(Goster(harcama, nameof(vm.YeniTakip))));
        // Eski borç devri iptal edilmez ("Eski borç devri" bölümünden düzeltilir); kilitli avans dağıtımı da ayrıca iptal edilmez.
        Govde.Add(Goster(Kart("Harcamalar ve iadeler", Liste<HarcamaSatiri>(nameof(vm.Harcamalar), async s =>
            {
                if (!vm.EditorMu)
                    return;
                if (s.Veri.EkstreKayitId is { } id)
                {
                    await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
                    return;
                }
                await GerekceyleAsync("Kart hareketini iptal et", (gerekce, _) =>
                {
                    vm.Gerekce = gerekce;
                    return vm.HarcamaIptalAsync(s);
                });
            }, "Hareketi iptal et", s => vm.EditorMu && vm.YeniTakip && !vm.DevirSatiri(s) && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
                s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Hareketi iptal et")),
                nameof(vm.KartSecili)));
        Govde.Add(Goster(Kart("Kaydedilen ödemeler", Liste<KartOdemeSatiri>(nameof(vm.Odemeler), async s =>
            {
                if (!vm.EditorMu)
                    return;
                if (s.Veri.EkstreKayitId is { } id)
                {
                    await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
                    return;
                }
                await GerekceyleAsync("Kart ödemesini iptal et", (gerekce, _) =>
                {
                    vm.Gerekce = gerekce;
                    return vm.OdemeIptalAsync(s);
                });
            }, "Ödemeyi iptal et", s => vm.EditorMu && vm.YeniTakip && !s.AvansDagitimi && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
                s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Ödemeyi iptal et")),
                nameof(vm.KartSecili)));
        Govde.Add(Goster(Kart("Eski karttan geçiş", Bagli(nameof(vm.GecisKaydi))), nameof(vm.GecisKaydi), true));
        // Geçişli kartın eski borç devri (web'deki "Eski borç devri" bölümü): okunur, engeli yoksa gerekçeyle düzeltilir.
        var devirFormu = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { Alan("Doğru kalan borç", Girdi(nameof(vm.DevirKalanBorc), true)), Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.DevirOncedenSayilan), true)),
            Paylar(vm.DevirPaylari, () => vm.PayEkle(vm.DevirPaylari)), Alan("Düzeltme gerekçesi", Girdi(nameof(vm.DevirAciklama))), Dugme("Devri düzelt", nameof(vm.DevirDuzeltCommand)) }
        };
        Govde.Add(Editor(Goster(Kart("Eski borç devri",
            Metin("Devrin ödemesi kasada önceden sayılan kısım kadar kasadan ikinci kez düşmez; devre yapılan iadenin önceden sayılmış kısmı iade tarihinde kasaya döner. Hatalı devir iptal edilmez: düzeltme etkin devri iptal edip aynı tarihle yeni tutarı yazar, gerekçe denetim izine kaydedilir."),
            Dugme("Devir bilgisini göster", nameof(vm.DevirYukleCommand)), Bagli(nameof(vm.DevirOzeti)), Goster(devirFormu, nameof(vm.DevirDuzeltilebilir))), nameof(vm.GecisKaydi), true)));
        Govde.Add(Editor(Goster(Kart("Kullanım durumu", Metin("Kartı pasife almak geçmiş hareketleri silmez."),
            Tikla("Aktif / pasif durumunu değiştir", () => GerekceyleAsync("Kartın kullanım durumunu değiştir", (gerekce, _) => { vm.Gerekce = gerekce; return vm.DurumDegistirAsync(); }))),
            nameof(vm.KartSecili))));
    }
}
