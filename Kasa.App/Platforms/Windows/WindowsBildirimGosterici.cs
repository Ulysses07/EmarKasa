using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Kasa.ApiClient;
using Kasa.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.WinUI;

/// <summary>
/// Windows App SDK uygulama bildirimleri (AppNotificationManager; paketsiz uygulama, WindowsPackageType=None; tasarım 2026-09-30
/// masaüstü bildirimleri §1 Gösterim). Paketsiz uygulamada Register çağıran süreci COM sunucusu olarak kaydeder; uygulama adı ve
/// simgesi kayıtta verilir (<see cref="GorunenAd"/>, <see cref="SimgeDosyasi"/>; verilmezse ad exe'den türetilir). Pencereli süreç
/// <see cref="Baslat"/> ile önce NotificationInvoked işleyicisini bağlar, sonra kaydolur: uygulama açıkken tıklama bu süreçte olay
/// olarak gelir. Uygulama kapalıyken tıklama yeni süreç başlatır; tıklama etkinleştirme bağımsız değişkenlerinden
/// (AppInstance.GetActivatedEventArgs, ExtendedActivationKind.AppNotification) ya da kayıttan sonra aynı olayla gelir; ikisi de
/// <see cref="Tiklamalar"/>'a yazılır (son değer geçerli). Pencere açmadan çalışan görev işleyici bağlamadan kaydolur (ilk
/// gösterimde): o bildirime tıklanırsa Windows yeni (pencereli) süreç başlatır. Süreç bitmeden <see cref="Bitir"/> (Unregister)
/// çağrılır: sonraki tıklamalar uygulamayı yeniden başlatabilsin; Bitir'den sonra gösterim yapılmaz. Tıklama UI iş parçacığına
/// Baslat'ta yakalanan DispatcherQueue ile aktarılır (MAUI MainThread pencere yokken InvalidOperationException atar).
/// <para>Kimlik: süreç her kipte önce <see cref="KimlikAyarla"/> ile sabit AppUserModelID alır (<see cref="BildirimKimligi.Aumid"/>);
/// Windows App SDK paketsiz kayıtta bu kimliği kullanır, etkinleştirici CLSID'sini kimliğin kaydına (HKCU\Software\Classes\
/// AppUserModelId\EmarKasa.Masaustu, CustomActivator) yazar. Pencereli açılışta kayıttan sonra arka planda COM sunucusu kaydı bu
/// exe'ye göre düzeltilir ve Başlat menüsü kısayolu (<see cref="BaslatKisayolu"/>) aynı kimlik ve CLSID ile güncellenir.</para>
/// Kaynaklar: learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register,
/// learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart,
/// github.com/microsoft/WindowsAppSDK dev/AppNotifications/AppNotificationManager.cpp ve AppNotificationUtility.cpp (v1.8.9).
/// </summary>
public sealed class WindowsBildirimGosterici : IBildirimGosterici
{
    /// <summary>Bildirimde görünen uygulama adı (paketsiz kayıt; verilmezse exe adı "Kasa" görünür).</summary>
    public const string GorunenAd = "Emar Kasa";

    /// <summary>Bildirim simgesi: MAUI'nin Resources/AppIcon'dan Windows için ürettiği ve derleme/yayın çıktısına (exe'nin yanına)
    /// kopyaladığı png. Dosya yoksa ad ve simge verilmeden kaydolunur.</summary>
    public const string SimgeDosyasi = "appiconLogo.targetsize-256.png";

    /// <summary>Aynı tıklamanın kısa arayla ikinci kez gelişini ayırt etme süresi.</summary>
    private const long CiftTiklamaMs = 5000;

    /// <summary>Gösterimden önce kısayol düzenlemesini en çok bu kadar bekleme.</summary>
    private static readonly TimeSpan KisayolBeklemesi = TimeSpan.FromSeconds(3);

    /// <summary>Sürecin tek örneği: App.xaml.cs DI kurulmadan önce başlatır, MauiProgram aynı örneği kaydeder.</summary>
    public static WindowsBildirimGosterici Ortak { get; } = new();

    private readonly Lock _kilit = new();
    private readonly Lock _tiklamaKilidi = new();
    private DispatcherQueue? _arayuz;
    private bool _basladi;
    private bool _kayitli;
    private bool _kapandi;
    /// <summary>Pencereli açılıştaki COM kaydı ve Başlat menüsü kısayolu düzenlemesi (<see cref="KayitlariDuzenle"/>); pencere açmadan
    /// çalışan görevde null (kısayolu daha önceki pencereli açılış yazmıştır).</summary>
    private Task? _kayitDuzeni;
    private BildirimTiklamasi? _sonTiklama;
    private long _sonTiklamaZamani;

    public BildirimTiklamalari Tiklamalar { get; } = new();

    /// <summary>Pencereli süreçte bir kez (OnLaunched, base.OnLaunched'dan önce, UI iş parçacığında).</summary>
    public void Baslat()
    {
        lock (_kilit)
        {
            if (_basladi)
                return;
            _basladi = true;
            _arayuz = DispatcherQueue.GetForCurrentThread();
            try
            {
                AppNotificationManager.Default.NotificationInvoked += (_, e) => Tiklandi(e.Arguments);
                Kaydol();
                _kayitli = true;
                var etkinlestirme = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (etkinlestirme.Kind == ExtendedActivationKind.AppNotification && etkinlestirme.Data is AppNotificationActivatedEventArgs bildirim)
                    Tiklandi(bildirim.Arguments);
            }
            catch (Exception ex)
            {
                // Bildirim altyapısı yoksa uygulama bildirimsiz açılır; Bildirimler ekranı Windows ayarı uyarısını gösterir.
                Debug.WriteLine($"Windows bildirim kaydı başarısız: {ex}");
            }
        }
        // Kayıt başarısız olsa da kısayol yazılır (uygulamayı açmak için de kullanılır); açılışı bekletmez. Gösterim bu işi
        // bekler (GostermeyeHazir): kısayol yokken gösterilen bildirim Windows'ta görünmeden düşer, kimliği ise gösterilmiş sayılırdı.
        _kayitDuzeni = Task.Run(KayitlariDuzenle);
    }

    /// <summary>Sürecin AppUserModelID'sini sabitler. AppNotificationManager.Default ilk kez oluşturulmadan ve pencere açılmadan önce
    /// çağrılmalıdır (OnLaunched'ın başı, bütün kipler): Windows App SDK kimliği yöneticinin kurucusunda okur. Hata yutulur; o
    /// durumda Windows App SDK exe yolundan kimlik üretir.</summary>
    public static void KimlikAyarla()
    {
        var sonuc = SetCurrentProcessExplicitAppUserModelID(BildirimKimligi.Aumid);
        if (sonuc < 0)
            Debug.WriteLine($"AppUserModelID atanamadı: 0x{sonuc:X8}");
    }

    /// <summary>Pencereli açılışta, kayıttan sonra (arka planda): Windows App SDK'nın etkinleştirici CLSID'si okunur; COM sunucusu kaydı
    /// başka exe'yi gösteriyorsa (uygulama başka klasörden açıldı) bu exe'ye çevrilir: sabit kimlikte Windows App SDK var olan kaydı
    /// yeniden kullanır, yolunu güncellemez (AppNotificationUtility.cpp, GetOrCreateComActivatorGuid). Sonra Başlat menüsü kısayolu
    /// güncellenir; CLSID okunamazsa kısayol etkinleştiricisiz yazılır. Hatalar yutulur.</summary>
    private static void KayitlariDuzenle()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        Guid? clsid = null;
        try
        {
            using var kimlik = Registry.CurrentUser.OpenSubKey(KimlikKaydi);
            if (kimlik?.GetValue("CustomActivator") is string deger && Guid.TryParse(deger, out var okunan))
            {
                clsid = okunan;
                using var sunucu = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{okunan:B}\LocalServer32");
                if (!BildirimKimligi.ComSunucusuGuncelMi(sunucu.GetValue(null) as string, exe))
                    sunucu.SetValue(null, BildirimKimligi.ComSunucusuKomutu(exe), RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bildirim COM kaydı düzeltilemedi: {ex}");
        }
        try
        {
            BaslatKisayolu.Guncelle(new KisayolBilgisi(exe, Path.GetDirectoryName(exe) ?? "", exe, 0, BildirimKimligi.Aumid, clsid));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Başlat menüsü kısayolu yazılamadı: {ex}");
        }
    }

    /// <summary>Windows App SDK'nın paketsiz kimlik kaydı (AppNotificationUtility.cpp, c_appIdentifierPath + kimlik).</summary>
    private const string KimlikKaydi = @"Software\Classes\AppUserModelId\" + BildirimKimligi.Aumid;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string kimlik);

    /// <summary>Süreç bitmeden (pencere kapanınca, süreç çıkışında, pencere açmadan çalışan görevin sonunda): Windows bildirim kaydı
    /// kaldırılır (kayıtlı değilse bir şey yapmaz). Birden çok kez çağrılabilir; bundan sonra gösterim yapılmaz.</summary>
    public void Bitir()
    {
        lock (_kilit)
        {
            _kapandi = true;
            if (!_kayitli)
                return;
            _kayitli = false;
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception) { }
        }
    }

    public bool WindowsAyarindaKapali
    {
        get
        {
            try
            {
                return AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public void Goster(BildirimDto bildirim) => Yayinla(new AppNotificationBuilder()
        .AddArgument(BildirimTiklamasi.KimlikAnahtari, bildirim.Id.ToString(CultureInfo.InvariantCulture))
        .AddArgument(BildirimTiklamasi.HedefAnahtari, bildirim.Hedef)
        .AddText(bildirim.Baslik)
        .AddText(bildirim.Mesaj));

    /// <summary>Pencereli açılıştaki kayıt düzenlemesini (kısayol) en çok <see cref="KisayolBeklemesi"/> bekler; genelde çoktan
    /// bitmiştir. Düzenleme UI iş parçacığına dönmez: UI'dan çağrılan Wait kilitlenmeye yol açmaz. Süre dolarsa false (yoklayıcı
    /// bildirimi ayırmaz, sonraki bakmada gösterir); düzenleme hata verdiyse bitmiş sayılır (true). Pencere açmadan çalışan görevde
    /// düzenleme yoktur: kısayolu daha önceki pencereli açılış yazmıştır.</summary>
    public bool GostermeyeHazir
    {
        get
        {
            try
            {
                return _kayitDuzeni?.Wait(KisayolBeklemesi) ?? true;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public bool DenemeGoster()
    {
        try
        {
            return Yayinla(new AppNotificationBuilder()
                .AddArgument(BildirimTiklamasi.HedefAnahtari, BildirimTiklamasi.DenemeHedefi)
                .AddText("Emar Kasa deneme bildirimi")
                .AddText("Windows bildirimleri bu bilgisayarda çalışıyor. Kart ve kredi hatırlatmaları böyle görünecek."));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Paketsiz kayıt: ad ve simgeyle (Windows App SDK 1.2+; ikisi birlikte verilir, simge yerel dosyanın mutlak Uri'si).</summary>
    private static void Kaydol()
    {
        var simge = Path.Combine(AppContext.BaseDirectory, SimgeDosyasi);
        if (File.Exists(simge))
            AppNotificationManager.Default.Register(GorunenAd, new Uri(simge));
        else
            AppNotificationManager.Default.Register();
    }

    /// <summary>Gösterir; süreç kayıtlı değilse (pencere açmadan çalışan görev) önce işleyicisiz kaydolur. <see cref="Bitir"/>'den
    /// sonra (geç kalan bakma) hiçbir şey yapmaz ve false döner: kaldırılan kayıt yeniden açılmaz.</summary>
    private bool Yayinla(AppNotificationBuilder icerik)
    {
        lock (_kilit)
        {
            if (_kapandi)
            {
                Debug.WriteLine("Windows bildirim kaydı kapandı; bildirim gösterilmedi.");
                return false;
            }
            if (!_kayitli)
            {
                Kaydol();
                _kayitli = true;
            }
        }
        // Deneme bildirimi de ilk açılışta kısayolu bekler; sunucu bildirimlerini yoklayıcı GostermeyeHazir ile önceden denetler.
        _ = GostermeyeHazir;
        AppNotificationManager.Default.Show(icerik.BuildNotification());
        return true;
    }

    /// <summary>Tıklama: UI iş parçacığında kuyruğa yazılır; uygulama zaten açıksa pencere öne getirilir. Olay arka plan iş
    /// parçacığında gelir.</summary>
    private void Tiklandi(IDictionary<string, string> argumanlar)
    {
        var tiklama = BildirimTiklamasi.Coz(argumanlar);
        if (!IlkKezMi(tiklama))
            return;
        if (_arayuz is null || _arayuz.HasThreadAccess)
        {
            Aktar(tiklama);
            return;
        }
        if (!_arayuz.TryEnqueue(() => Aktar(tiklama)))
            Debug.WriteLine("Bildirim tıklaması UI kuyruğuna alınamadı (uygulama kapanıyor).");
    }

    private void Aktar(BildirimTiklamasi tiklama)
    {
        try
        {
            Tiklamalar.Ekle(tiklama);
            OneGetir();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bildirim tıklaması aktarılamadı: {ex}");
        }
    }

    /// <summary>Açık pencereyi öne getirir; küçültülmüşse önce geri yükler (Activate küçültülmüş pencereyi açmayabilir). Pencere
    /// henüz yoksa (uygulama tıklamayla başlıyor) bir şey yapmaz.</summary>
    private static void OneGetir()
    {
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window pencere)
            return;
        if (pencere.AppWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } sunucu)
            sunucu.Restore();
        pencere.Activate();
    }

    /// <summary>Aynı tıklama <see cref="CiftTiklamaMs"/> içinde ikinci kez gelirse false. Savunma amaçlıdır: kaynağa göre tek
    /// etkinleştirme iki yoldan (etkinleştirme argümanı ve olay) gelmez; yine de gelirse rota iki kez açılmasın.</summary>
    private bool IlkKezMi(BildirimTiklamasi tiklama)
    {
        lock (_tiklamaKilidi)
        {
            var simdi = Environment.TickCount64;
            if (tiklama == _sonTiklama && simdi - _sonTiklamaZamani < CiftTiklamaMs)
                return false;
            _sonTiklama = tiklama;
            _sonTiklamaZamani = simdi;
            return true;
        }
    }
}
