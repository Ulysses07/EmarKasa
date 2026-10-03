using System.Diagnostics;
using Kasa.App.Controls;
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
    /// <summary>Menünün açıldığı oturum (AuthViewModel.OturumSurumu); bkz. <see cref="MenuyuAc"/>.</summary>
    private int? _menuOturumu;
    private bool _cikiliyor;
    private readonly BildirimNobetcisi _bildirimNobetcisi;
    private readonly BildirimTiklamalari _bildirimTiklamalari;
    /// <summary>Uygulama açıkken 5 dakikada bir bildirim bakması (BildirimNobetcisi.Aralik); oturum açılınca başlar, girişe dönüşte durur.</summary>
    private readonly IDispatcherTimer _bildirimZamanlayicisi;
    /// <summary>Gezinme çubuğundaki bağlantı şeridi (Shell.TitleView; yalnız kopukken görünür).</summary>
    private readonly BaglantiSeridi _baglantiSeridi;
    private bool _yenileniyor, _birakmaSoruluyor;
    /// <summary>Son otomatik (bağlantı geldiğinde) yenilemenin anı: <see cref="OtomatikYenilemeAraligi"/>'ndan sık tetiklenmez
    /// (bağlantı şeridi gidip gelirken yenileme fırtınası olmasın, ürün sahibi kararı 2026-10-03). "Yeniden dene" bu sınıra
    /// bağlı değildir.</summary>
    private DateTimeOffset? _sonOtomatikYenileme;
    private static readonly TimeSpan OtomatikYenilemeAraligi = TimeSpan.FromSeconds(3);

    public AppShell(AuthViewModel auth, BildirimNobetcisi bildirimNobetcisi, BildirimTiklamalari bildirimTiklamalari, BaglantiDurumu baglanti)
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
            [Bolum.Cekler] = CeklerItem,
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
        // Menü rozeti: okunmamış bildirim sayısı (5 dakikalık bakma, Bildirimler ekranı, tıklama); çıkışta 0 olur ve gizlenir.
        _bildirimNobetcisi.Yoklayici.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BildirimYoklayici.Okunmamis))
                MainThread.BeginInvokeOnMainThread(() => _menuModeli.RozetAyarla(Bolum.Bildirimler, _bildirimNobetcisi.Yoklayici.Okunmamis));
        };
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
        // Bağlantı şeridi (tasarım 2026-10-02 §3): kabukta tek şerit, bütün sayfaların gezinme çubuğunda (TitleView kabuktan
        // devralınır). "Yeniden dene" ve bağlantının geri gelmesi açık sayfayı bir kez yeniler; bağlantının geri gelmesi
        // OtomatikYenilemeAraligi ile sınırlıdır (K-10: yenileme fırtınası).
        _baglantiSeridi = new BaglantiSeridi { BindingContext = baglanti };
        _baglantiSeridi.YenidenDeneIstendi += async (_, _) => await AcikSayfayiYenileAsync();
        baglanti.BaglantiGeldi += async (_, _) => await AcikSayfayiYenileAsync(otomatik: true);
        SetTitleView(this, _baglantiSeridi);
    }

    /// <summary>Açık sayfayı yeniler (IYenilenebilir). <paramref name="otomatik"/> true ise (bağlantı geldi) art arda gelen
    /// yenilemeler arasında en az <see cref="OtomatikYenilemeAraligi"/> beklenir (şerit gidip gelirken yenileme fırtınası
    /// olmaz); "Yeniden dene" (otomatik=false) her zaman çalışır. Yenileme sürerken gelen ikinci istek (ör. yenilemenin ilk
    /// yanıtı bağlantıyı geri getirdi) yok sayılır; hata günlüğe yazılır (async void işleyiciden istisna çıkmaz).</summary>
    private async Task AcikSayfayiYenileAsync(bool otomatik = false)
    {
        if (_yenileniyor || CurrentPage is not IYenilenebilir sayfa)
            return;
        if (otomatik && _sonOtomatikYenileme is { } once && DateTimeOffset.UtcNow - once < OtomatikYenilemeAraligi)
            return;
        if (otomatik)
            _sonOtomatikYenileme = DateTimeOffset.UtcNow;
        _yenileniyor = true;
        _baglantiSeridi.Yenileniyor = true;
        try
        { await sayfa.YenileAsync(); }
        catch (Exception ex) { Debug.WriteLine($"Sayfa yenilenemedi: {ex}"); }
        finally
        {
            _yenileniyor = false;
            _baglantiSeridi.Yenileniyor = false;
        }
    }

    /// <summary>Sayfadan çıkış onayı (tasarım §2; MAUI Shell gezinme ertelemesi): açık sayfanın formunda kaydedilmemiş değişiklik
    /// varsa "Kaydedilmemiş değişiklik var. Bırakılsın mı?" sorulur. "Forma dön" gezinmeyi iptal eder; "Bırak" değişiklikleri bırakıp
    /// devam eder. Girişe dönüş (çıkış, oturumun sona ermesi) sorulmaz.</summary>
    protected override async void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        if (_birakmaSoruluyor || !args.CanCancel || args.Target?.Location?.OriginalString.Contains("login", StringComparison.Ordinal) == true
            || CurrentPage?.BindingContext is not IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true } form)
            return;
        var erteleme = args.GetDeferral();
        _birakmaSoruluyor = true;
        try
        {
            if (await DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, KaydedilmemisDegisiklik.Ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon))
                form.DegisiklikleriBirak();
            else
                args.Cancel();
        }
        catch (Exception ex) { Debug.WriteLine($"Sayfadan çıkış onayı gösterilemedi: {ex}"); }
        finally
        {
            _birakmaSoruluyor = false;
            erteleme.Complete();
        }
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

    /// <summary>Oturum açıldığında menü bir kez açılır. Açılışta saklı oturum doğrulanınca iki çağrı gelir: LoginPage (GirisYapildi
    /// değişimi, ilk Shell öğesi olduğu için sayfa açılışta oluşur) ve <see cref="AcilistaYonlendirAsync"/>. Aynı oturumdaki
    /// (<see cref="AuthViewModel.OturumSurumu"/>) ikinci çağrı yok sayılır: yoksa ikinci açılış gezinmesi, uygulama bildirim
    /// tıklamasıyla başladığında birincinin açtığı hedef sayfanın üstüne ilk sayfayı açıyordu.</summary>
    public void MenuyuAc()
    {
        if (_menuOturumu == _auth.OturumSurumu)
            return;
        _menuOturumu = _auth.OturumSurumu;
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
    /// hedef sayfa hemen açılır, okundu işareti arkada gönderilir. Al() bekleyeni alıp temizlediği için aynı tıklama iki kez uygulanmaz.</summary>
    private async Task TiklamayiUygulaAsync()
    {
        try
        {
            if (!_auth.GirisYapildi || _bildirimTiklamalari.Al() is not { } tiklama)
                return;
            if (_bildirimNobetcisi.TiklamayiIsle(tiklama) is { } rota)
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
