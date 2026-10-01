using System.Diagnostics;
using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;
    /// <summary>Menünün içeriği (gruplar, öğeler, seçili öğe): Shell.FlyoutContent kökünün (MenuAlani) bağlamı.</summary>
    private readonly MenuModeli _menuModeli = new();
    /// <summary>Rol bölümü → sayfa öğesi (tek kaynak). Öğeler menüde çizilmez (menüyü MenuModeli çizer) ama rota kaynağıdır;
    /// yetkisi olmayan bölümün öğesi gizli kalır, doğrudan rotayla erişim kuralı önceki menüdekiyle aynıdır.</summary>
    private readonly IReadOnlyDictionary<Bolum, FlyoutItem> _menu;
    private bool _giriseDonuluyor;
    private bool _cikiliyor;
    private readonly BildirimNobetcisi _bildirimNobetcisi;
    private readonly BildirimTiklamalari _bildirimTiklamalari;
    /// <summary>Uygulama açıkken 5 dakikada bir bildirim bakması (BildirimNobetcisi.Aralik); oturum açılınca başlar, girişe dönüşte durur.</summary>
    private readonly IDispatcherTimer _bildirimZamanlayicisi;

    public AppShell(AuthViewModel auth, BildirimNobetcisi bildirimNobetcisi, BildirimTiklamalari bildirimTiklamalari)
    {
        InitializeComponent();
        _auth = auth;
        _bildirimNobetcisi = bildirimNobetcisi;
        _bildirimTiklamalari = bildirimTiklamalari;
        _bildirimZamanlayicisi = Dispatcher.CreateTimer();
        _bildirimZamanlayicisi.Interval = BildirimNobetcisi.Aralik;
        _bildirimZamanlayicisi.IsRepeating = true;
        _menu = new Dictionary<Bolum, FlyoutItem>
        {
            [Bolum.Panel] = PanelItem,
            [Bolum.Haftalik] = HaftalikItem,
            [Bolum.Aylik] = AylikItem,
            [Bolum.Islemler] = IslemlerItem,
            [Bolum.AylikGiderler] = AylikGiderlerItem,
            [Bolum.Ayarlar] = AyarlarItem,
            [Bolum.Alislar] = AlislarItem,
            [Bolum.DisariAktar] = DisariAktarItem,
            [Bolum.Kartlar] = KartlarItem,
            [Bolum.Krediler] = KredilerItem,
            [Bolum.Bildirimler] = BildirimlerItem,
            [Bolum.EkstreAktar] = EkstreAktarItem,
        };
        MenuAlani.BindingContext = _menuModeli;
        // Olay işleyicileri async void'dir: çağırdıkları yardımcılar (GitAsync, CikisAsync, GiriseDonAsync; AcilistaDogrulaAsync
        // kendisi) istisnayı yakalar (küresel işleyici yok, yakalanmayan istisna WinUI sürecini çökertir).
        _menuModeli.GitIstendi += async (_, rota) => await GitAsync(rota);
        _menuModeli.CikisIstendi += async (_, _) => await CikisAsync();
        _auth.OturumSonlandi += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await GiriseDonAsync());
        // Bildirim bakması ve tıklaması da istisnayı yakalayan yardımcılara gider. Tıklama olayı Ekle'yi çağıran iş parçacığında
        // gelir (Windows göstericisi UI iş parçacığına aktarır); gezinme yine de ana iş parçacığında yapılır.
        _bildirimZamanlayicisi.Tick += async (_, _) => await BildirimYoklaAsync();
        _bildirimTiklamalari.Istendi += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await TiklamayiUygulaAsync());
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    /// <summary>Her gezinmede (menü, sayfalar arası bağlantı, girişe dönüş) seçili menü öğesi yeni konumdan belirlenir.</summary>
    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _menuModeli.RotaSecildi(args.Current?.Location?.OriginalString);
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi)
            MenuyuAc();
        else
            await GiriseDonAsync();
    }

    public void MenuyuAc()
    {
        MenuyuGoster(SekmeModeli.Bolumler(_auth.AktifRol));
        _ = AcilisaGitAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }

    /// <summary>Oturum açılınca: ilk sayfa, sonra bekleyen bildirim tıklaması (uygulama tıklamayla başladıysa), sonra bildirim bakması
    /// (editörse görev saati de güncellenir) ve 5 dakikalık zamanlayıcı. İlk sayfaya gidilemese de bakma başlar.</summary>
    private async Task AcilisaGitAsync(string rota)
    {
        try
        {
            await GoToAsync(rota);
        }
        catch (Exception ex) { Debug.WriteLine($"Açılış gezinmesi başarısız: {ex}"); }
        await TiklamayiUygulaAsync();
        try
        {
            _bildirimZamanlayicisi.Start();
            await _bildirimNobetcisi.OturumAcildiAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Açılış bildirim bakması başarısız: {ex}"); }
    }

    /// <summary>5 dakikalık bildirim bakması; hata günlüğe yazılır (async void işleyiciden istisna çıkmaz).</summary>
    private async Task BildirimYoklaAsync()
    {
        try
        {
            await _bildirimNobetcisi.TikAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Bildirim bakması başarısız: {ex}"); }
    }

    /// <summary>Bekleyen bildirim tıklaması: oturum açık değilse beklemede kalır ve girişten sonra uygulanır; editör oturumunda
    /// bildirim okundu işaretlenir ve hedef sayfa açılır. Al() bekleyeni alıp temizlediği için aynı tıklama iki kez uygulanmaz.</summary>
    private async Task TiklamayiUygulaAsync()
    {
        try
        {
            if (!_auth.GirisYapildi || _bildirimTiklamalari.Al() is not { } tiklama)
                return;
            if (await _bildirimNobetcisi.TiklamayiIsleAsync(tiklama) is { } rota)
                await GoToAsync(rota);
        }
        catch (Exception ex) { Debug.WriteLine($"Bildirim tıklaması uygulanamadı: {ex}"); }
    }

    /// <summary>Yalnız verilen bölümlerin sayfa öğeleri erişilebilir ve menüde görünür (girişe dönüşte hiçbiri).</summary>
    private void MenuyuGoster(IReadOnlyCollection<Bolum> bolumler)
    {
        foreach (var (bolum, oge) in _menu)
            oge.IsVisible = bolumler.Contains(bolum);
        _menuModeli.Goster(bolumler);
    }

    /// <summary>Menüden gezinme; başarısız gezinme günlüğe yazılır, kullanıcı bulunduğu sayfada kalır.</summary>
    private async Task GitAsync(string rota)
    {
        try
        { await GoToAsync("//" + rota); }
        catch (Exception ex) { Debug.WriteLine($"Gezinme başarısız ({rota}): {ex}"); }
    }

    /// <summary>Çıkış; çift tıklamada ikinci çıkış başlamaz.</summary>
    private async Task CikisAsync()
    {
        if (_cikiliyor)
            return;
        _cikiliyor = true;
        try
        {
            await _auth.CikisAsync();
            await GiriseDonAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Çıkış başarısız: {ex}"); }
        finally { _cikiliyor = false; }
    }

    private async Task GiriseDonAsync()
    {
        if (_giriseDonuluyor)
            return;
        _giriseDonuluyor = true;
        try
        {
            _bildirimZamanlayicisi.Stop();
            MenuyuGoster([]);
            await GoToAsync("//login");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Girişe dönüş başarısız: {ex}");
        }
        finally { _giriseDonuluyor = false; }
    }
}
