using System.Diagnostics;
using System.Globalization;
using Kasa.App.Controls;
using Kasa.App.Core;
using Microsoft.Maui.Layouts;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>
/// Kredi kartları (docs/specs/2026-09-30-masaustu-menu-ve-kartlar.md §2): kartlar banka rengindeki kutulardır (KartKutusu);
/// kutuya tıklanınca kartın ayrıntısı kutunun satırının hemen altında tam genişlikte açılır (KartIzgarasi). Ayrıntı yukarıdan
/// aşağıya: özet, düğmeler (editör), tek form alanı (editör) ve Ekstreler / Harcamalar / Ödemeler sekmeleri. Açık kart, açık form
/// ve seçili sekme modeldedir (KartTakipViewModel.AcikKartId, AcikForm, SeciliSekme). Formların alanları, önizlemeleri, benzer
/// kayıt uyarıları, onay kutuları ve iletileri önceki tek sayfalık düzendekiyle aynıdır; yalnız yerleri değişti. Önceki sayfada
/// ayrı kartlar olan "Kullanım durumu" ve "Eski borç devri" Kartı düzenle formundadır. Izgara ve ayrıntı Govde'nin içindedir:
/// model meşgulken (kayıt sürerken) kutular, düğmeler ve form kapalıdır, açık kart değiştirilemez.
/// </summary>
public sealed class KartTakipPage : TakipSayfasi<KartTakipViewModel>, IQueryAttributable
{
    private const string SayfaAciklamasi = "Yeni takipte kartla harcama nakit çıkışı oluşturmaz; kasa kaydettiğiniz ödeme ile azalır. "
        + "Geçiş yapılmamış eski kartlarda önceki ay sonu kuralı korunur.";
    private const string KasaNotu = "Bu tutarlar mevcut kasadan düşülmüş değildir. Kasa, kaydedilen kart ödemesiyle değişir.";
    private const string AcilisNotu = "Açılış borcunun bilinen kanal paylarını girin. Bilinmeyen dağılım tahmin edilmez.";
    private const string DurumNotu = "Kartı pasife almak geçmiş hareketleri silmez.";
    private const string GecisNotu = "Bankanızdaki kalan borcu girin. Kasada önceden sayılan kısım, sistemin eski kuralla kasadan düştüğü/düşeceği borçtur; "
        + "siz değiştirmedikçe alan kalan borç ile sistem kart borcunun küçüğünü izler. Önizleme farklı bir tutar önerirse "
        + "\"Önerilen tutarla yeniden önizle\" ile uygulayabilirsiniz. Önerilenin altı yalnız açılış borcu kasadan ayrıca ödenecekse girilebilir. "
        + "Girdiler değişirse geçiş, yeni önizleme alınmadan onaylanamaz. Bu işlem yeni harcama oluşturmaz.";
    private const string EkstreNotu = "Asgari ödeme ve tarihler bankanın ekstresinden girilir; uygulama oran veya tatil günü tahmini yapmaz.";
    private const string MasrafNotu = "Bankanın bildirdiği tutarı girin. Seçilen kesilmiş ekstre ve önceki ekstrelerin kalan borcuna göre kanallara dağıtılır; "
        + "ileri taksitler ağırlığa katılmaz. Bilinmeyen kanal payı varsa kaydetmeden önce düzeltilmelidir.";
    private const string HarcamaNotu = "Alışlar veya İşlemler'den bu karta bağlanan harcamayı tekrar girmeyin. İade için eksi tutar ve tek taksit kullanın. "
        + "Kalan borca dağıtılacak faiz / masrafı \"Faiz / masraf\" formundan girin.";
    private const string HarcamaPayNotu = "Bilinen kanal paylarını girin. Taksit toplamı borca ikinci kez eklenmez.";
    private const string DevirNotu = "Devrin ödemesi kasada önceden sayılan kısım kadar kasadan ikinci kez düşmez; devre yapılan iadenin önceden sayılmış kısmı "
        + "iade tarihinde kasaya döner. Hatalı devir iptal edilmez: düzeltme etkin devri iptal edip aynı tarihle yeni tutarı yazar, gerekçe denetim izine kaydedilir.";

    private int? _istenenKartId;
    private int? _gosterilenKartId;
    /// <summary>Son kaydırma isteğinin sırası ve kaydırması yapılmış istek: yalnız en son istek, bir kez kaydırır.</summary>
    private int _kaydirmaIstegi, _kaydirilanIstek;
    private readonly View _ayrinti;
    private readonly View _formAlani;

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

    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları", SayfaAciklamasi, vm.YukleAsync, nameof(vm.SayfaHatasi))
    {
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
            Style = (Style)Application.Current!.Resources["CardForm"],
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    Goster(Ozet(vm), nameof(vm.KartSecili)),
                    Editor(Goster(Dugmeler(vm), nameof(vm.KartSecili))),
                    Editor(Goster(_formAlani, nameof(vm.FormAcik))),
                    Goster(Sekmeler(vm), nameof(vm.KartSecili)),
                },
            },
        };
        // Ayrıntının görünürlüğünü ızgara yönetir (açık kutu varken); burada ayrıca bağlanmaz.
        var izgara = new KartIzgarasi { Ayrinti = _ayrinti };
        izgara.SetBinding(KartIzgarasi.ItemsSourceProperty, nameof(vm.Kartlar));
        izgara.SetBinding(KartIzgarasi.SecCommandProperty, nameof(vm.KutuSecCommand));
        izgara.SetBinding(KartIzgarasi.YeniCommandProperty, nameof(vm.YeniKartAcCommand));
        izgara.SetBinding(KartIzgarasi.YeniGorunurProperty, nameof(vm.EditorMu));
        izgara.SetBinding(KartIzgarasi.AcikKartIdProperty, nameof(vm.AcikKartId));
        izgara.SetBinding(KartIzgarasi.YeniAcikProperty, nameof(vm.YeniKartFormuAcik));
        // Önceki "Kartlar" listesinin boş durum iletisi: kart yokken (izleyicide ızgara tamamen boş kalır) gösterilir.
        var bos = Metin("Gösterilecek kayıt yok.");
        bos.SetBinding(IsVisibleProperty, $"{nameof(vm.Kartlar)}.{nameof(vm.Kartlar.Count)}", converter: new SifirIse());
        Govde.Add(bos);
        Govde.Add(izgara);
        // Başka bir kart açılınca (kutu ya da sayfa belirirken IdIleSec) ayrıntısı, form açılınca form, sayfa hatası yazılınca
        // sayfa başındaki hata satırı görünür yere kaydırılır (yalnız görünmüyorsa). Aynı kartın kayıttan sonra güncellenmesi
        // (AcikKartId yine bildirilir) ve kartın kapanması kaydırmaz; yeni kart formu AcikForm üzerinden kaydırılır.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.AcikKartId) && vm.AcikKartId != _gosterilenKartId)
            {
                _gosterilenKartId = vm.AcikKartId;
                if (vm.AcikKartId is not null)
                    GorunurYap(_ayrinti, AyrintiKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.AcikForm) && vm.FormAcik)
            {
                GorunurYap(_formAlani, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.SayfaHatasi) && !string.IsNullOrWhiteSpace(vm.SayfaHatasi))
            {
                GorunurYap(HataSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
        };
    }

    /// <summary>Hedefin (üst, yükseklik) ve kaydırıcının (kaydırma konumu, görünür yükseklik) değerlerinden yeni kaydırma konumu;
    /// null: kaydırılmaz (KaydirmaHesabi).</summary>
    private delegate double? KaydirmaKarari(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik);

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
    private static double? AyrintiKaydirmasi(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik)
        => KaydirmaHesabi.BasaKaydirilmali(ust, kaydirmaY, gorunurYukseklik) ? ust : null;

    /// <summary>
    /// Hedef yerleştikten sonra (konumu okunabilir olunca) <paramref name="karar"/>'ın verdiği konuma kaydırır (KaydirmaHesabi;
    /// hedef görünüyorsa kaydırılmaz). Zamanlama: hedefin bir sonraki SizeChanged'i (yerleşim turunda üst öğeleri de yerleşmiş olur) ya da,
    /// boyutu değişmeden yalnız yeri değişirse (başka satırdaki kart, aynı boyda form), 100 ms aralıklı yoklama; yoklama hedef
    /// yerleşmiş (genişliği olan) bulunca ya da 1 sn sonra biter. Kaydırma her iki yolda da yerleşim turunun bitimine
    /// (Dispatch) bırakılır. Yeni istek eskisini geçersiz kılar; istek bir kez kaydırır.
    /// </summary>
    private void GorunurYap(View hedef, KaydirmaKarari karar)
    {
        var istek = ++_kaydirmaIstegi;
        var deneme = 0;
        void Boyutlandi(object? sender, EventArgs e) => Yerlesti();
        void Yerlesti()
        {
            hedef.SizeChanged -= Boyutlandi;
            Dispatcher.Dispatch(() => Kaydir(hedef, karar, istek));
        }
        void Yokla()
        {
            if (istek != _kaydirmaIstegi || istek == _kaydirilanIstek)
            {
                hedef.SizeChanged -= Boyutlandi;
                return;
            }
            if (hedef.Width > 0)
                Yerlesti();
            else if (++deneme < 10)
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
            else
                hedef.SizeChanged -= Boyutlandi;
        }
        hedef.SizeChanged += Boyutlandi;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
    }

    private async void Kaydir(View hedef, KaydirmaKarari karar, int istek)
    {
        if (istek != _kaydirmaIstegi || istek == _kaydirilanIstek || !hedef.IsVisible)
            return;
        _kaydirilanIstek = istek;
        try
        {
            if (karar(KaydiriciyaGoreY(hedef), hedef.Height, Kaydirici.ScrollY, Kaydirici.Height) is { } y)
                await Kaydirici.ScrollToAsync(Kaydirici.ScrollX, y, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Kartlar ekranı kaydırılamadı: {ex}");
        }
    }

    /// <summary>Hedefin kaydırılan içeriğe göre üstü: ata zinciri boyunca her öğenin üst öğesine göre yeri (Frame.Y) toplanır;
    /// hedef kaydırıcının içinde değilse NaN (kaydırılmaz).</summary>
    private double KaydiriciyaGoreY(VisualElement hedef)
    {
        var y = 0d;
        for (Element? e = hedef; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, Kaydirici))
                return y;
            if (e is VisualElement v)
                y += v.Frame.Y;
        }
        return double.NaN;
    }

    // ---- Özet ----

    private static View Ozet(KartTakipViewModel vm)
    {
        // İlk sürüm geçiş kalıntısı (web'deki notice): tahmini kasa farkı varsa koyu kırmızı, yalnız düşüş tarihi farklıysa bilgi.
        var gecisUyarisi = Bagli(nameof(vm.GecisUyarisi));
        gecisUyarisi.FontAttributes = FontAttributes.Bold;
        gecisUyarisi.Triggers.Add(new DataTrigger(typeof(Label))
        {
            Binding = new Binding(nameof(vm.GecisUyarisiTehlikeli)),
            Value = true,
            Setters = { new Setter { Property = Label.TextColorProperty, Value = (Color)Application.Current!.Resources["KoyuKirmizi"] } }
        });
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Baslik("Kart ayrıntısı"),
                BagliBuyuk(nameof(vm.KartOzeti)),
                Goster(AltBolum("Geçiş uyarısı", gecisUyarisi), nameof(vm.GecisUyarisi), true),
                AltBolum("Kanalların kalan kart borcu", Bagli(nameof(vm.KanalBorcOzeti)), Metin(KasaNotu)),
                Goster(AltBolum("Eski karttan geçiş", Bagli(nameof(vm.GecisKaydi))), nameof(vm.GecisKaydi), true),
            },
        };
    }

    // ---- Düğmeler ----

    private static View Dugmeler(KartTakipViewModel vm) => new VerticalStackLayout
    {
        Children =
        {
            Goster(DugmeSirasi(FormDugmesi("Ödeme kaydet", KartFormu.Odeme, birincil: true), FormDugmesi("Harcama ekle", KartFormu.Harcama),
                FormDugmesi("Faiz / masraf", KartFormu.Masraf), FormDugmesi("Kartı düzenle", KartFormu.KartBilgisi)), nameof(vm.YeniTakip)),
            Goster(DugmeSirasi(FormDugmesi("Yeni takibe al", KartFormu.Gecis, birincil: true), FormDugmesi("Kartı düzenle", KartFormu.KartBilgisi)),
                nameof(vm.EskiTakip)),
        },
    };

    /// <summary>Formu açan düğme (TakipUi.FormDugmesi): açık formun düğmesi vurgulanır, genişliği değişmez.</summary>
    private static Button FormDugmesi(string metin, KartFormu form, bool birincil = false)
        => TakipUi.FormDugmesi(metin, nameof(KartTakipViewModel.FormAcCommand), form, nameof(KartTakipViewModel.AcikForm), birincil);

    private static FlexLayout DugmeSirasi(params Button[] dugmeler)
    {
        var sira = new FlexLayout { Wrap = FlexWrap.Wrap };
        foreach (var dugme in dugmeler)
        {
            dugme.Margin = new Thickness(0, 0, 10, 10);
            sira.Add(dugme);
        }
        return sira;
    }

    // ---- Form alanı: aynı anda tek form (AcikForm); hata formun içinde (FormHatasi) ----

    private View FormAlani(KartTakipViewModel vm)
    {
        var vazgec = Dugme("Vazgeç", nameof(vm.VazgecCommand));
        vazgec.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        return new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                Goster(BagliHata(nameof(vm.FormHatasi)), nameof(vm.FormHatasi), true),
                Durumda(KartBilgileri(vm), nameof(vm.AcikForm), KartFormu.KartBilgisi),
                Durumda(Odeme(vm), nameof(vm.AcikForm), KartFormu.Odeme),
                Durumda(Harcama(vm), nameof(vm.AcikForm), KartFormu.Harcama),
                Durumda(Masraf(vm), nameof(vm.AcikForm), KartFormu.Masraf),
                Durumda(Ekstre(vm), nameof(vm.AcikForm), KartFormu.Ekstre),
                Durumda(Gecis(vm), nameof(vm.AcikForm), KartFormu.Gecis),
                vazgec,
            },
        };
    }

    private View KartBilgileri(KartTakipViewModel vm)
    {
        var acilis = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true)),
                Metin(AcilisNotu), Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)),
            },
        };
        var durum = AltBolum("Kullanım durumu", Metin(DurumNotu), Tikla("Aktif / pasif durumunu değiştir",
            () => GerekceyleAsync("Kartın kullanım durumunu değiştir", (gerekce, _) =>
            {
                vm.Gerekce = gerekce;
                return vm.DurumDegistirAsync();
            })));
        return Form("Kart bilgileri",
            Alan("Kart / banka adı", Girdi(nameof(vm.Ad))), Alan("Limit", Girdi(nameof(vm.Limit), true)),
            Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true)), Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true)),
            Goster(acilis, nameof(vm.YeniKart)), Dugme("Kartı kaydet", nameof(vm.KaydetCommand)),
            Goster(durum, nameof(vm.KartSecili)), Goster(Devir(vm), nameof(vm.GecisKaydi), true));
    }

    private static View Devir(KartTakipViewModel vm)
    {
        // Geçişli kartın eski borç devri (web'deki "Eski borç devri" bölümü): okunur, engeli yoksa gerekçeyle düzeltilir.
        var devirFormu = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Doğru kalan borç", Girdi(nameof(vm.DevirKalanBorc), true)),
                Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.DevirOncedenSayilan), true)),
                Paylar(vm.DevirPaylari, () => vm.PayEkle(vm.DevirPaylari)), Alan("Düzeltme gerekçesi", Girdi(nameof(vm.DevirAciklama))),
                Dugme("Devri düzelt", nameof(vm.DevirDuzeltCommand)),
            },
        };
        return AltBolum("Eski borç devri", Metin(DevirNotu), Dugme("Devir bilgisini göster", nameof(vm.DevirYukleCommand)),
            Bagli(nameof(vm.DevirOzeti)), Goster(devirFormu, nameof(vm.DevirDuzeltilebilir)));
    }

    private static View Odeme(KartTakipViewModel vm) => Form("Kart ödemesi kaydet",
        Alan("Tarih", Tarih(nameof(vm.OdemeTarihi))), Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true)),
        Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
        Tikla("En eski açık ekstrelere dağıt", () =>
        {
            vm.OdemeEkstresi = null;
            return Task.CompletedTask;
        }),
        Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu))),
        Dugme("Ödeme ve kanal paylarını göster", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)),
        Dugme("Ödemeyi kaydet", nameof(vm.OdemeKaydetCommand)), Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));

    private static View Harcama(KartTakipViewModel vm) => Form("Bağımsız kart hareketi",
        Metin(HarcamaNotu),
        Alan("Tarih", Tarih(nameof(vm.HarcamaTarihi))), Alan("Açıklama", Girdi(nameof(vm.HarcamaAciklama))),
        Alan("Tutar (iade için eksi)", Girdi(nameof(vm.HarcamaTutari), true)), Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)),
        Onay("İlk hesap kesim tarihini belirle", nameof(vm.IlkKesimVar)), Goster(Alan("İlk kesim tarihi", Tarih(nameof(vm.IlkKesimTarihi))), nameof(vm.IlkKesimVar)),
        Goster(Alan("İade edilen harcama (kanal payları kaynaktan alınır)", Secim(nameof(vm.IadeKaynaklari), nameof(vm.IadeKaynagi), "Baslik")), nameof(vm.IadeGirisi)),
        Goster(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Metin(HarcamaPayNotu), Paylar(vm.HarcamaPaylari, () => vm.PayEkle(vm.HarcamaPaylari)) }
        }, nameof(vm.HarcamaGirisi)),
        Dugme("Kart hareketini kaydet", nameof(vm.HarcamaKaydetCommand)),
        Benzerlik(nameof(vm.HarcamaBenzerlik), nameof(vm.HarcamayiAyriKaydetCommand)));

    private static View Masraf(KartTakipViewModel vm) => Form("Faiz / masrafı kalan borca dağıt",
        Metin(MasrafNotu),
        Alan("Kesilmiş açık ekstre", Secim(nameof(vm.MasrafEkstreleri), nameof(vm.MasrafEkstresi))),
        Alan("Tarih", Tarih(nameof(vm.MasrafTarihi))), Alan("Bankanın bildirdiği faiz / masraf", Girdi(nameof(vm.MasrafTutari), true)),
        Alan("Açıklama", Girdi(nameof(vm.MasrafAciklama))),
        Dugme("Kanal dağılımını göster", nameof(vm.MasrafOnizleCommand)), Bagli(nameof(vm.MasrafOnizleme)),
        Dugme("Gösterilen masrafı kaydet", nameof(vm.MasrafKaydetCommand)));

    private static View Ekstre(KartTakipViewModel vm) => Form("Ekstre bilgisi",
        Metin(EkstreNotu),
        Alan("Son ödeme tarihi", Tarih(nameof(vm.EkstreSonOdeme))),
        Onay("Banka asgari ödeme tutarı girildi", nameof(vm.AsgariVar)), Alan("Asgari ödeme", Girdi(nameof(vm.AsgariTutar), true)),
        Alan("Açıklama", Girdi(nameof(vm.Gerekce))), Dugme("Ekstreyi kaydet", nameof(vm.EkstreKaydetCommand)));

    private static View Gecis(KartTakipViewModel vm)
    {
        // Kabul edilemez önizlemede (KabulEdilebilir=false) onay kutusu ve düğme kapalıdır; neden kırmızı yazılır.
        var gecisEngeli = BagliHata(nameof(vm.GecisEngeli));
        var gecisOnayi = Onay("Gösterilen kasa ve kanal etkisini inceledim; geçişi onaylıyorum.", nameof(vm.GecisOnay));
        gecisOnayi.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.GecisOnaylanabilir));
        // Sunucu farklı bir tutar önerirse alana kendiliğinden yazılmaz; web'deki gibi açık eylemle uygulanıp yeniden önizlenir.
        var gecisOnerisi = new Button { HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        gecisOnerisi.SetBinding(Button.TextProperty, nameof(vm.GecisOneriMetni));
        gecisOnerisi.SetBinding(Button.CommandProperty, nameof(vm.OnerilenleGecisOnizleCommand));
        return Form("Eski kartı yeni takibe al",
            Metin(GecisNotu),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), Alan("Kalan kart borcu", Girdi(nameof(vm.GecisKalanBorc), true)),
            Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.OncedenSayilan), true)),
            Paylar(vm.GecisPaylari, () => vm.PayEkle(vm.GecisPaylari)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))),
            Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)),
            Goster(gecisEngeli, nameof(vm.GecisEngeli), true), Goster(gecisOnerisi, nameof(vm.GecisOneriVar)), gecisOnayi,
            Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand)));
    }

    // ---- Sekmeler ----

    private View Sekmeler(KartTakipViewModel vm)
    {
        var sekmeler = new CipGrubu();
        sekmeler.SetBinding(BindableLayout.ItemsSourceProperty, nameof(vm.Sekmeler));
        sekmeler.SetBinding(CipGrubu.SecCommandProperty, nameof(vm.SekmeSecCommand));
        var ekstreler = Liste<EkstreSatiri>(nameof(vm.Ekstreler), s =>
        {
            vm.EkstreSecCommand.Execute(s);
            return Task.CompletedTask;
        }, "Tarih / asgari ödeme", _ => vm.EditorMu && vm.YeniTakip);
        // Eski borç devri iptal edilmez ("Eski borç devri" bölümünden düzeltilir); kilitli avans dağıtımı da ayrıca iptal edilmez.
        var harcamalar = Liste<HarcamaSatiri>(nameof(vm.Harcamalar), s => HarcamayiAcAsync(vm, s), "Hareketi iptal et",
            s => vm.EditorMu && vm.YeniTakip && !vm.DevirSatiri(s) && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
            s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Hareketi iptal et");
        var odemeler = Liste<KartOdemeSatiri>(nameof(vm.Odemeler), s => OdemeyiAcAsync(vm, s), "Ödemeyi iptal et",
            s => vm.EditorMu && vm.YeniTakip && !s.AvansDagitimi && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
            s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Ödemeyi iptal et");
        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                sekmeler,
                Durumda(ekstreler, nameof(vm.SeciliSekme), KartSekmesi.Ekstreler),
                Durumda(harcamalar, nameof(vm.SeciliSekme), KartSekmesi.Harcamalar),
                Durumda(odemeler, nameof(vm.SeciliSekme), KartSekmesi.Odemeler),
            },
        };
    }

    private async Task HarcamayiAcAsync(KartTakipViewModel vm, HarcamaSatiri s)
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
    }

    private async Task OdemeyiAcAsync(KartTakipViewModel vm, KartOdemeSatiri s)
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
    }

    // ---- Yapı taşları ----

    private static Label Baslik(string metin) => new() { Text = metin, Style = (Style)Application.Current!.Resources["LblTakipKartBaslik"] };

    /// <summary>Form: kart başlığı stilinde başlık ve alanlar (önceki sayfadaki kartın içeriği).</summary>
    private static VerticalStackLayout Form(string baslik, params View[] icerik)
    {
        var form = new VerticalStackLayout { Spacing = 12 };
        form.Add(Baslik(baslik));
        foreach (var v in icerik)
            form.Add(v);
        return form;
    }

    /// <summary>Özet ya da form içindeki alt bölüm: kalın küçük başlık ve içerik.</summary>
    private static VerticalStackLayout AltBolum(string baslik, params View[] icerik)
    {
        var bolum = new VerticalStackLayout { Spacing = 8 };
        bolum.Add(new Label { Text = baslik, FontAttributes = FontAttributes.Bold, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] });
        foreach (var v in icerik)
            bolum.Add(v);
        return bolum;
    }

    /// <summary>Görünüm yalnız bağlı durum <paramref name="deger"/> iken görünür (açık form, seçili sekme).</summary>
    private static View Durumda<TDurum>(View gorunum, string yol, TDurum deger) where TDurum : struct, Enum
    {
        gorunum.SetBinding(IsVisibleProperty, yol, converter: new DurumdaIse<TDurum>(deger));
        return gorunum;
    }

    private sealed class DurumdaIse<TDurum>(TDurum deger) : IValueConverter where TDurum : struct, Enum
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is TDurum durum && durum.Equals(deger);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Sayı 0 ise true (boş liste iletisi).</summary>
    private sealed class SifirIse : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is 0;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
