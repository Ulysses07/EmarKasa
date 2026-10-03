using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kartlar ekranında açık form; aynı anda tek form açıktır (Yok: hiçbiri).</summary>
public enum KartFormu { Yok, Odeme, Harcama, Masraf, Ekstre, KartBilgisi, Gecis }

/// <summary>Kart ayrıntısının sekmeleri (varsayılan Ekstreler).</summary>
public enum KartSekmesi { Ekstreler, Harcamalar, Odemeler }

/// <summary>
/// Kartlar ekranının arayüz durumu (tasarım 2026-09-30 §2). Açık kart <see cref="KartTakipViewModel.Secili"/>'dir (kutuya
/// tıklamak açar, aynı kutuya tekrar tıklamak kapatır); Secili null iken <see cref="KartFormu.KartBilgisi"/> yeni kart formudur.
/// Formlar yalnız editörde ve kartın takibine uygunsa açılır; işlemi başlatan formun başarılı kaydı, Vazgeç, kart değişimi ve
/// izleyiciye dönüş formu kapatır, hata formu açık bırakır. Yalnız formun kendi komutunun hatası formun içinde gösterilir
/// (<see cref="FormHatasi"/>); diğerleri sayfa başındadır (<see cref="SayfaHatasi"/>). Hesap, doğrulama ve sunucu mantığı değişmez.
/// </summary>
public partial class KartTakipViewModel
{
    private readonly SecimCipi[] _sekmeler = [new("Ekstreler") { Secili = true }, new("Harcamalar"), new("Ödemeler")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormAcik), nameof(YeniKartFormuAcik), nameof(FormHatasi), nameof(SayfaHatasi))]
    private KartFormu _acikForm;

    [ObservableProperty]
    private KartSekmesi _seciliSekme;

    /// <summary>Sekme çipleri (CipGrubu); sırası <see cref="KartSekmesi"/> ile aynı.</summary>
    public IReadOnlyList<SecimCipi> Sekmeler => _sekmeler;
    public bool FormAcik => AcikForm != KartFormu.Yok;
    /// <summary>Kutusu açık kartın kimliği (KartIzgarasi.AcikKartId); açık kart yoksa null.</summary>
    public int? AcikKartId => Secili?.Id;
    /// <summary>"Yeni kart ekle" kutusunun formu açık (KartIzgarasi.YeniAcik).</summary>
    public bool YeniKartFormuAcik => Secili is null && AcikForm == KartFormu.KartBilgisi;
    /// <summary>Hatanın kaynağı: formun kendi komutu (kaydet, önizle, ayrı kaydet) başlarken o form, formdan bağımsız yollar
    /// (yükleme, iptal, iptal reddi, kart bulunamadı) Yok yazar. Kaynak form kapanınca ya da değişince hatası da kalkar.</summary>
    private KartFormu _hataKaynagi;
    private KartFormu HataKaynagi
    {
        get => _hataKaynagi;
        set
        {
            if (_hataKaynagi == value)
                return;
            _hataKaynagi = value;
            HataYuzeyleriniBildir();
        }
    }
    /// <summary>Yalnız açık formun kendi komutunun hatası formun içinde gösterilir; o sırada sayfa başındaki hata satırı boştur.</summary>
    public string? FormHatasi => FormAcik && HataKaynagi == AcikForm ? Hata : null;
    /// <summary>Formdan bağımsız hata (yükleme, iptal …) ve kaynağı artık açık olmayan formun hatası sayfa başında gösterilir.</summary>
    public string? SayfaHatasi => FormHatasi is null ? Hata : null;

    /// <summary>Kart bilgileri formunun (yeni kart ve "Kartı düzenle") hataları (tasarım 2026-10-02 §1; KR-04). Sunucu kart hatalarını
    /// alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari KartHatalari { get; } = new();
    /// <summary>Kart ödemesi formunun hataları (KR-01): eksik alan önce alanın altında, sunucu iletisi genel hatada.</summary>
    public AlanHatalari OdemeHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KartHatalari, OdemeHatalari];

    /// <summary>Kart bilgileri formunun başlığı: yeni kartta "Yeni kart", düzenlemede "Düzenleniyor: Bonus".</summary>
    public string KartFormuBasligi => Secili is { } kart ? $"Düzenleniyor: {kart.Ad}" : "Yeni kart";
    public string KartKaydetMetni => Secili is null ? "Kartı kaydet" : "Değişikliği kaydet";

    /// <summary>Açık formun açıldığı andaki değerleri (kart bilgileri, ödeme, harcama): kaydedilmemiş değişiklik ölçütü (tasarım §2).
    /// Diğer formlar (masraf, ekstre, geçiş) izlenmez.</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => AcikForm switch
    {
        KartFormu.KartBilgisi => new
        {
            Ad,
            Limit,
            KesimGunu,
            SonOdemeGunu,
            AcilisTarihi,
            AcilisBorc,
            Paylar = AcilisPaylari.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
        },
        KartFormu.Odeme => new { OdemeTarihi, OdemeTutari, Ekstre = OdemeEkstresi?.Veri.Id, OdemeNotu },
        KartFormu.Harcama => (object)new
        {
            HarcamaTarihi,
            HarcamaAciklama,
            HarcamaTutari,
            TaksitSayisi,
            IlkKesimVar,
            IlkKesimTarihi,
            Iade = IadeKaynagi?.Veri.Id,
            Paylar = HarcamaPaylari.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
        },
        _ => null,
    });

    /// <summary>Aynı kartın formları arasında geçerken önceki formda kaydedilmemiş değişiklik vardı: geçiş sorulmaz (aynı kayıt) ama
    /// kirlilik taşınır, başka karta geçişte onay sorulur. Form kapanınca (Vazgeç, kayıt, kart değişimi, Bırak) kalkar.</summary>
    private bool _oncekiFormKirli;

    public bool KaydedilmemisDegisiklikVar => FormIzi.Var || _oncekiFormKirli;

    /// <summary>Yazılmış değişiklikleri bırakır: form kapanır, kartın form alanları kartın kayıtlı değerlerine (yeni kartta boşa) döner.</summary>
    public void DegisiklikleriBirak()
    {
        AcikForm = KartFormu.Yok;
        KartFormlariniTemizle();
        if (Secili is { } kart)
            Sec(new KartTakipSatiri(kart, _zaman));
        else
            Yeni();
    }

    /// <summary>Başka karta geçiş, kartı kapatma ve yeni kart öncesi: yazılmış değişiklik varsa onay sorulur; "Bırak" seçilirse
    /// değişiklikler bırakılır. Aynı kartın formları arasında geçiş sorulmaz (aynı kayıt).</summary>
    private async Task<bool> FormdanCikilabilirAsync()
    {
        if (!KaydedilmemisDegisiklikVar)
            return true;
        if (!await BirakilabilirAsync(true))
            return false;
        DegisiklikleriBirak();
        return true;
    }

    /// <summary>Hatası <paramref name="kaynak"/> formuna ait tekil işlem (<see cref="KartFormu.Yok"/>: sayfaya ait).</summary>
    private Task YurutAsync(KartFormu kaynak, Func<int, Task> islem) => YurutAsync(n =>
    {
        HataKaynagi = kaynak;
        return islem(n);
    });

    /// <summary>Formdan bağımsız, doğrudan yazılan hata (tekil işlem dışında).</summary>
    private void SayfaHatasiYaz(string ileti)
    {
        HataKaynagi = KartFormu.Yok;
        Hata = ileti;
    }

    /// <summary>Başarılı kayıttan sonra yalnız işlemi başlatan form hâlâ açıksa kapanır (kayıt sürerken açılan başka form kalır).</summary>
    private void FormuKapat(KartFormu form)
    {
        if (AcikForm == form)
            AcikForm = KartFormu.Yok;
    }

    [RelayCommand]
    private void FormAc(KartFormu form)
    {
        if (!EditorMu || !Acilabilir(form))
            return;
        AcikForm = form;
    }

    /// <summary>Ödeme, harcama ve masraf yeni takipteki kartta; ekstre bilgisi yeni takipte seçili ekstreyle; geçiş eski takipte.</summary>
    private bool Acilabilir(KartFormu form) => form switch
    {
        KartFormu.Odeme or KartFormu.Harcama or KartFormu.Masraf => YeniTakip,
        KartFormu.Ekstre => YeniTakip && DuzenlenenEkstre is not null,
        KartFormu.Gecis => EskiTakip,
        KartFormu.KartBilgisi => true,
        _ => false,
    };

    /// <summary>Formu kapatır; formun hatası ve benzer kayıt uyarısı da kalkar (<see cref="OnAcikFormChanged"/>). "Vazgeç" bilerek
    /// bırakmaktır: onay sorulmaz (tasarım §2 onayı başka kayda geçiş, Yeni ve sayfadan çıkışta ister).</summary>
    [RelayCommand]
    private void Vazgec() => AcikForm = KartFormu.Yok;

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır. Yazılmış form varsa önce onay sorulur.</summary>
    [RelayCommand]
    private async Task KutuSecAsync(KartTakipSatiri? satir)
    {
        if (satir is null || !await FormdanCikilabilirAsync())
            return;
        if (Secili?.Id == satir.Veri.Id)
            Yeni();
        else
            Sec(satir);
    }

    /// <summary>"Yeni kart ekle" kutusu: boş kart bilgileri formunu açar; form açıkken tekrar tıklamak kapatır. Yazılmış form varsa
    /// önce onay sorulur.</summary>
    [RelayCommand]
    private async Task YeniKartAcAsync()
    {
        // Onaydan önce okunur: "Bırak" formu kapatıp yeni kart alanlarını boşaltır, kutu yine de kapatma tıklamasıdır.
        var kapat = YeniKartFormuAcik;
        if (!EditorMu || !await FormdanCikilabilirAsync())
            return;
        if (kapat)
        {
            AcikForm = KartFormu.Yok;
            return;
        }
        Yeni();
        FormAc(KartFormu.KartBilgisi);
    }

    [RelayCommand]
    private void SekmeSec(SecimCipi sekme)
    {
        var sira = Array.IndexOf(_sekmeler, sekme);
        if (sira >= 0)
            SeciliSekme = (KartSekmesi)sira;
    }

    partial void OnSeciliSekmeChanged(KartSekmesi value)
    {
        for (var i = 0; i < _sekmeler.Length; i++)
            _sekmeler[i].Secili = i == (int)value;
    }

    /// <summary>Formdan çıkılınca (kapanma ya da başka form) o formun hatası kalkar ve hata kaynağı sayfaya döner; ekstre
    /// formundan çıkılınca düzenlenen ekstre bırakılır (ekstre formu yalnız ekstreye tıklanarak açılır); ödeme ve harcama
    /// formundan çıkılınca benzer kayıt uyarısı ve onayı kalkar.</summary>
    /// <summary>Aynı kartta formdan forma geçişte önceki formun kirliliği taşınır (<see cref="_oncekiFormKirli"/>); değer henüz
    /// değişmediği için iz hâlâ önceki formu ölçer. Forma Yok'a kapanması (Vazgeç dahil) bu bayrağı düşürmez: Vazgeç yalnız açık
    /// formun kirliliğini bırakır (<see cref="Vazgec"/>); taşınan bayrak kart değişimine kadar kalır (<see cref="KartTakipViewModel.Sec(KartTakipSatiri, bool)"/>,
    /// <see cref="KartTakipViewModel.Yeni"/>).</summary>
    partial void OnAcikFormChanging(KartFormu oldValue, KartFormu newValue)
    {
        if (oldValue != KartFormu.Yok && newValue != KartFormu.Yok)
            _oncekiFormKirli |= FormIzi.Var;
    }

    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        KartHatalari.Temizle();
        OdemeHatalari.Temizle();
        if (newValue == KartFormu.Yok)
            FormIzi.Kapat();
        else
            FormIzi.Ac();
        if (HataKaynagi == oldValue && oldValue != KartFormu.Yok)
        {
            HataKaynagi = KartFormu.Yok;
            Hata = null;
        }
        if (oldValue == KartFormu.Ekstre)
            DuzenlenenEkstre = null;
        else if (oldValue == KartFormu.Odeme)
            OdemeBenzerlik.Temizle();
        else if (oldValue == KartFormu.Harcama)
            HarcamaBenzerlik.Temizle();
    }

    /// <summary>Düzenlenen ekstre bırakılınca (ör. liste yenilemesinde aynı kartın yeniden seçilmesi) boş ekstre formu açık kalmaz.</summary>
    partial void OnDuzenlenenEkstreChanged(EkstreSatiri? value)
    {
        if (value is null && AcikForm == KartFormu.Ekstre)
            AcikForm = KartFormu.Yok;
    }

    /// <summary>Başka karta geçiş (kutu, yeni kart, yeni kartın kaydı, liste yenilemesinde kaybolan kart) açık formu kapatır ve
    /// Ekstreler sekmesine döner; aynı kartın güncellenmesi (kayıt sonucu) formu ve sekmeyi korur.</summary>
    partial void OnSeciliChanged(KartTakipDto? oldValue, KartTakipDto? newValue)
    {
        if (oldValue?.Id != newValue?.Id)
        {
            AcikForm = KartFormu.Yok;
            SeciliSekme = KartSekmesi.Ekstreler;
        }
        OnPropertyChanged(nameof(AcikKartId));
        OnPropertyChanged(nameof(YeniKartFormuAcik));
        OnPropertyChanged(nameof(KartFormuBasligi));
        OnPropertyChanged(nameof(KartKaydetMetni));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Hata))
            HataYuzeyleriniBildir();
    }

    private void HataYuzeyleriniBildir()
    {
        OnPropertyChanged(nameof(FormHatasi));
        OnPropertyChanged(nameof(SayfaHatasi));
    }

    /// <summary>İzleyiciye dönen oturumda form açık kalmaz.</summary>
    protected override void RolDegisti()
    {
        if (!EditorMu)
            AcikForm = KartFormu.Yok;
    }
}
