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
    private readonly BaglantiDurumu _baglanti;
    private bool _yenileniyor, _birakmaSoruluyor;
    /// <summary>Onay penceresi açıkken (erteleme beklerken) oturum düşerse <see cref="GiriseDonAsync"/> beklemeye alınır (Ö-3):
    /// GoToAsync erteleme beklerken InvalidOperationException verir. Pencere kapanınca (<see cref="OnNavigating"/>'in finally'si)
    /// bu bayrak işlenip girişe dönüş tekrar denenir.</summary>
    private bool _girisDonusuBekliyor;
    /// <summary>Bağlantı geldiğinde otomatik yenileme kararı (K-1 alt sınır, sondaki kenarda tek tetik; Ö-1 kaydedilmemiş
    /// değişiklikte hiç tetiklenmez, form kullanıcı isteği olmadan ezilmez). "Yeniden dene" bunu kullanmaz.</summary>
    private readonly OtomatikYenilemeKarari _otomatikYenileme = new OtomatikYenilemeKarari(TimeProvider.System, TimeSpan.FromSeconds(3));

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
        // devralınır). "Yeniden dene" ve bağlantının geri gelmesi açık sayfayı yeniler; bağlantının geri gelmesi
        // OtomatikYenileAsync'in alt sınırına ve kaydedilmemiş değişiklik denetimine bağlıdır (K-1, Ö-1).
        _baglanti = baglanti;
        _baglantiSeridi = new BaglantiSeridi { BindingContext = baglanti };
        _baglantiSeridi.YenidenDeneIstendi += async (_, _) => await ElleYenileAsync();
        baglanti.BaglantiGeldi += async (_, _) => await OtomatikYenileAsync();
        SetTitleView(this, _baglantiSeridi);
    }

    /// <summary>Bağlantı geldiğinde otomatik yenileme (K-1: alt sınır, sondaki kenarda tek tetik, TimeProvider ile; Ö-1:
    /// kaydedilmemiş değişiklikte hiç yenilenmez, form kullanıcı isteği olmadan ezilmez — şerit kalkar, veri soluk kalır; Ö-2: bir
    /// önceki otomatik yenileme bağlantıyı yeniden kopardıysa bu geçişte yenilenmez).
    /// Sınır içinde kalınırsa <see cref="Dispatcher"/> ile sınırın sonunda yeniden denenir.</summary>
    private async Task OtomatikYenileAsync()
    {
        var kirli = CurrentPage?.BindingContext is IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true };
        if (_otomatikYenileme.Sor(kirli, out var bekle))
        {
            // Ö-2: yenileme bağlantıyı yeniden kopardıysa (ağır istek zaman aşımı) sonraki geçişte yenilenmez, yalnız şerit kalkar.
            var kopma = _baglanti.KopmaSayisi;
            await AcikSayfayiYenileAsync();
            _otomatikYenileme.YenilemeBitti(kopusla: _baglanti.KopmaSayisi != kopma);
        }
        else if (bekle > TimeSpan.Zero)
            Dispatcher.DispatchDelayed(bekle, () => _ = OtomatikYenileAsync());
    }

    /// <summary>"Yeniden dene": yenilenemeyen sayfada bağlantı hemen yoklanır; kaydedilmemiş değişiklikte
    /// <see cref="KaydedilmemisDegisiklik"/> onayı sorulur ("Bırak" derse değişiklikler bırakılıp yenilenir, "Forma dön" derse yenileme yapılmaz); otomatik yenileme sınırına (K-1) bağlı değildir,
    /// her zaman hemen çalışır.</summary>
    private async Task ElleYenileAsync()
    {
        // Küçük-3: yenilenemeyen sayfada (giriş, ayrıntı sayfaları) yoklama hemen yapılır; şerit aralığı beklemeden kalkar.
        if (CurrentPage is not IYenilenebilir)
        {
            await _baglanti.HemenYoklaAsync();
            return;
        }
        if (CurrentPage?.BindingContext is IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true } form)
        {
            if (!await DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, KaydedilmemisDegisiklik.Ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon))
                return;
            form.DegisiklikleriBirak();
        }
        await AcikSayfayiYenileAsync();
    }

    /// <summary>Açık sayfayı yeniler (IYenilenebilir). Yenileme sürerken gelen ikinci istek (ör. yenilemenin ilk yanıtı
    /// bağlantıyı geri getirdi) yok sayılır; hata günlüğe yazılır (async void işleyiciden istisna çıkmaz).</summary>
    private async Task AcikSayfayiYenileAsync()
    {
        if (_yenileniyor || CurrentPage is not IYenilenebilir sayfa)
            return;
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
            // Onay penceresi açıkken oturum düştüyse (Ö-3) girişe dönüş burada tekrar denenir (erteleme artık tamamlandı).
            if (_girisDonusuBekliyor)
            {
                _girisDonusuBekliyor = false;
                _ = GiriseDonAsync();
            }
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

    /// <summary>Girişe dönüş; onay penceresi açıkken (erteleme beklerken, Ö-3) çağrılırsa GoToAsync InvalidOperationException
    /// vereceği için hemen denenmez, beklemeye alınır (<see cref="_girisDonusuBekliyor"/>); <see cref="OnNavigating"/>'in
    /// finally'si pencere kapanınca bunu tekrar dener.</summary>
    private async Task GiriseDonAsync()
    {
        if (_giriseDonuluyor)
            return;
        if (_birakmaSoruluyor)
        {
            _girisDonusuBekliyor = true;
            return;
        }
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
