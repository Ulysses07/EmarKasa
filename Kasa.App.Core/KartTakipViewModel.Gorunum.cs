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
/// Formlar yalnız editörde ve kartın takibine uygunsa açılır; başarılı kayıt, Vazgeç, kart değişimi ve izleyiciye dönüş formu
/// kapatır, hata formu açık bırakır (hata formun içinde gösterilir: <see cref="FormHatasi"/>). Hesap, doğrulama ve sunucu
/// mantığı değişmez.
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
    /// <summary>Form açıkken hata formun içinde gösterilir; sayfa başındaki hata satırı o sırada boştur.</summary>
    public string? FormHatasi => FormAcik ? Hata : null;
    public string? SayfaHatasi => FormAcik ? null : Hata;

    [RelayCommand]
    private void FormAc(KartFormu form)
    {
        if (!EditorMu || !Acilabilir(form))
            return;
        Hata = null;
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

    [RelayCommand]
    private void Vazgec()
    {
        AcikForm = KartFormu.Yok;
        Hata = null;
    }

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır.</summary>
    [RelayCommand]
    private void KutuSec(KartTakipSatiri satir)
    {
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

    /// <summary>Ekstre formundan çıkılınca düzenlenen ekstre bırakılır (ekstre formu yalnız ekstreye tıklanarak açılır).</summary>
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        if (oldValue == KartFormu.Ekstre && newValue != KartFormu.Ekstre)
            DuzenlenenEkstre = null;
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
        {
            OnPropertyChanged(nameof(FormHatasi));
            OnPropertyChanged(nameof(SayfaHatasi));
        }
    }

    /// <summary>İzleyiciye dönen oturumda form açık kalmaz.</summary>
    protected override void RolDegisti()
    {
        if (!EditorMu)
            AcikForm = KartFormu.Yok;
    }
}
