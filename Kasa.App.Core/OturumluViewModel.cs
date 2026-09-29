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
        OturumDegisiminiDinle(auth, () =>
        {
            VeriHazir = false;
            Mesgul = false;
            Hata = null;
            Mesaj = null;
            SonGuncelleme = null;
            OturumTemizle();
            RolBildir();
        });
        // Rol oturum sürümü değişmeden de değişebilir: EditorMu hesaplanan değer olduğu için ayrıca bildirilir.
        auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.AktifRol))
                RolBildir();
        };
    }

    /// <summary>EditorMu ve ona bağlı hesaplanan değerler bildirilir (rol ya da oturum değişince).</summary>
    private void RolBildir()
    {
        OnPropertyChanged(nameof(EditorMu));
        RolDegisti();
    }

    /// <summary>Rol değişince EditorMu'ya bağlı hesaplanan değerleri bildirmek için (ör. Alışlar'da onay ve iade düğmeleri).</summary>
    protected virtual void RolDegisti() { }

    /// <summary>Oturum değişimini dinler (appcore-10): OturumSurumu değişince bekleyen işler hemen eskir (sonuçları, hataları ve
    /// bitişleri yansımaz), ekran <paramref name="sifirla"/> ile model kurulurken yakalanan UI bağlamında sıfırlanır. Eskiyen iş
    /// göstergeyi indirmediği için Mesgul'u sıfırlama indirir.</summary>
    private void OturumDegisiminiDinle(AuthViewModel auth, Action sifirla)
    {
        var ui = SynchronizationContext.Current;
        auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AuthViewModel.OturumSurumu))
                return;
            Yurutucu.GecersizKil();
            if (ui is not null && SynchronizationContext.Current != ui)
                ui.Post(_ => sifirla(), null);
            else
                sifirla();
        };
    }
    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Yurutucu.Nesil;
    [ObservableProperty] private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    /// <summary>Son başarılı yükleme anı (yerel saat ve farkı); rapor ve işlem listesiyle aynı tür.</summary>
    [ObservableProperty] private DateTimeOffset? _sonGuncelleme;
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

    protected override void IletiyiTemizle() => Mesaj = null;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; SonGuncelleme = DateTimeOffset.Now; }
}
