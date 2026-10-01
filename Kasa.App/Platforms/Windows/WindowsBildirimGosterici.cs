using System.Diagnostics;
using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
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
/// Kaynaklar: learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register,
/// learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart.
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

    /// <summary>Sürecin tek örneği: App.xaml.cs DI kurulmadan önce başlatır, MauiProgram aynı örneği kaydeder.</summary>
    public static WindowsBildirimGosterici Ortak { get; } = new();

    private readonly Lock _kilit = new();
    private readonly Lock _tiklamaKilidi = new();
    private DispatcherQueue? _arayuz;
    private bool _basladi;
    private bool _kayitli;
    private bool _kapandi;
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
    }

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
