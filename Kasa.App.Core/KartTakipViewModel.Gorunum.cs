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

    /// <summary>Formu kapatır; formun hatası ve benzer kayıt uyarısı da kalkar (<see cref="OnAcikFormChanged"/>).</summary>
    [RelayCommand]
    private void Vazgec() => AcikForm = KartFormu.Yok;

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır.</summary>
    [RelayCommand]
    private void KutuSec(KartTakipSatiri? satir)
    {
        if (satir is null)
            return;
        if (Secili?.Id == satir.Veri.Id)
            Yeni();
        else
            Sec(satir);
    }

    /// <summary>"Yeni kart ekle" kutusu: boş kart bilgileri formunu açar; form açıkken tekrar tıklamak kapatır.</summary>
    [RelayCommand]
    private void YeniKartAc()
    {
        if (!EditorMu)
            return;
        if (YeniKartFormuAcik)
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
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
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
