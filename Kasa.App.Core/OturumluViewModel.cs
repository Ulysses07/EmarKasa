using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Oturuma bağlı ekranların tek tabanı (oturum ve rol): rol oturumdan okunur (<see cref="EditorMu"/> hesaplanır, sayfa
/// atamaz); oturum değişince yürütücünün nesli artar (bekleyen işler eskir) ve ekran sıfırlanır (<see cref="OturumTemizle"/>).
/// Takip ekranları, İşlemler, Alışlar ve Ayarlar buradan türer. Yürütme deseni tabandaki <see cref="Yurutucu"/>'dur.</summary>
public abstract partial class OturumluViewModel : TemelViewModel
{
    protected readonly AuthViewModel Auth;
    protected OturumluViewModel(AuthViewModel auth)
    {
        Auth = auth;
        OturumDegisiminiDinle(auth);
    }

    /// <summary>Oturum değişince ekran yeni kurulmuş modelin durumuna döner; rol yeni oturumunkidir.</summary>
    private void OturumuSifirla()
    {
        VeriHazir = false;
        VeriEski = false;
        Mesgul = false;
        Hata = null;
        Mesaj = null;
        SonGuncelleme = null;
        OturumTemizle();
        RolBildir();
    }

    /// <summary>EditorMu ve ona bağlı hesaplanan değerler bildirilir (rol ya da oturum değişince).</summary>
    private void RolBildir()
    {
        OnPropertyChanged(nameof(EditorMu));
        RolDegisti();
    }

    /// <summary>Rol değişince EditorMu'ya bağlı hesaplanan değerleri bildirmek için (ör. Alışlar'da onay ve iade düğmeleri).</summary>
    protected virtual void RolDegisti() { }

    /// <summary>Oturum ve rol değişimini dinler (appcore-10): OturumSurumu değişince bekleyen işler hemen eskir (sonuçları, hataları ve
    /// bitişleri yansımaz) ve ekran sıfırlanır (<see cref="OturumuSifirla"/>); AktifRol oturum sürümü değişmeden de değişirse EditorMu
    /// ve ona bağlı değerler bildirilir. Sıfırlama ve bildirim model kurulurken yakalanan UI bağlamında yapılır. Eskiyen iş göstergeyi
    /// indirmediği için Mesgul'u sıfırlama indirir.</summary>
    private void OturumDegisiminiDinle(AuthViewModel auth)
    {
        var ui = SynchronizationContext.Current;
        void UiBaglaminda(Action eylem)
        {
            if (ui is not null && SynchronizationContext.Current != ui)
                ui.Post(_ => eylem(), null);
            else
                eylem();
        }
        auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.OturumSurumu))
            {
                Yurutucu.GecersizKil();
                UiBaglaminda(OturumuSifirla);
            }
            else if (e.PropertyName == nameof(AuthViewModel.AktifRol))
                UiBaglaminda(RolBildir);
        };
    }

    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Yurutucu.Nesil;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GovdeGorunur))]
    private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    /// <summary>Son başarılı yükleme anı (yerel saat ve farkı); rapor ve işlem listesiyle aynı tür.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni), nameof(GovdeGorunur))]
    private DateTimeOffset? _sonGuncelleme;
    /// <summary>Son yükleme hata verdi; gösterilen veri son başarılı yüklemeden (soluk gösterilir, tasarım §3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private bool _veriEski;

    /// <summary>"Son güncelleme: 02.10.2026 14:05" (veri eskiyse " · güncel olmayabilir" ekiyle); hiç yükleme yoksa "Henüz yüklenmedi.".</summary>
    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son güncelleme: {zaman:dd.MM.yyyy HH:mm}" + (VeriEski ? Bicim.EskiVeriEki : "")
        : Bicim.HenuzYuklenmedi;

    /// <summary>Ekranın gövdesi görünür mü: veri hazır ya da (yükleme hata verse de) son başarılı veri var (tasarım §3).</summary>
    public bool GovdeGorunur => VeriHazir || SonGuncelleme is not null;

    /// <summary>Gerekçe isteyen işlemin tek yolu (iptal, durum değişimi, belge kaldırma, ay kilidi): oturum gerekçe penceresi
    /// açılmadan ÖNCE yakalanır. Pencere açıkken oturum değişirse (çıkış, oturumun sona ermesi, yeni giriş) gerekçe yeni oturumun
    /// formuna yazılmaz ve işlem yapılmaz. Vazgeçilirse (null) işlem yapılmaz; boş gerekçe de yalnız
    /// <paramref name="bosGerekceGecerli"/> ise geçer.</summary>
    /// <param name="sor">Gerekçe penceresi (sayfanın DisplayPromptAsync'i); vazgeçilirse null.</param>
    /// <param name="islem">Gerekçe ve pencereden önce yakalanan oturum nesliyle yapılacak işlem; ardından ikinci bir onay
    /// penceresi açılıyorsa model bu nesli (<see cref="OturumNesli"/>) onun sonrasında da denetler.</param>
    public async Task GerekceyleAsync(Func<Task<string?>> sor, Func<string, int, Task> islem, bool bosGerekceGecerli = false)
    {
        var oturum = OturumNesli;
        var gerekce = await sor();
        if (gerekce is null || (!bosGerekceGecerli && string.IsNullOrWhiteSpace(gerekce)) || !Gecerli(oturum))
            return;
        await islem(gerekce, oturum);
    }

    /// <summary>"Kaydedilmemiş değişiklik var. Bırakılsın mı?" onayı (Bırak: true, Forma dön: false). Sayfa bağlar
    /// (DisplayAlertAsync); bağlı değilse yazılmış form bırakılmaz.</summary>
    public Func<string, Task<bool>>? BirakmaOnayi { get; set; }

    /// <summary>Başka kayda geçiş, Yeni ve sayfadan çıkıştan önce (tasarım §2): form değişmediyse ya da kullanıcı "Bırak" derse
    /// true. "Vazgeç" bunu sormaz (bilerek bırakmaktır, tasarım §2).</summary>
    protected async Task<bool> BirakilabilirAsync(KaydedilmemisDegisiklik form)
        => !form.Var || (BirakmaOnayi is { } sor && await sor(KaydedilmemisDegisiklik.Ileti));

    protected override void IletiyiTemizle() => Mesaj = null;
    protected override bool BaglantiKopuk => Auth.Baglanti.Kopuk;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; VeriEski = false; SonGuncelleme = DateTimeOffset.Now; }

    /// <summary>Ekran yüklemesi (tekil işlem): hata okuma iletisiyle yazılır, bağlantı kopukken bağlantı hatası yazılmaz (kabuk
    /// şeridi söyler); son başarılı veri silinmez, varsa eski işaretlenir. Başarılı yükleme <see cref="Tamamlandi"/>'yı çağırır.</summary>
    protected Task VeriYukleAsync(Func<int, Task> islem) => YurutAsync(islem, hataIsle: hata =>
    {
        Yurutucu.OkumaHatasiniYaz(hata);
        VeriEski = SonGuncelleme is not null;
    });
}
