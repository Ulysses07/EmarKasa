using System.Diagnostics;
using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.WinUI;

/// <summary>
/// Windows App SDK uygulama bildirimleri (AppNotificationManager; paketsiz uygulama, WindowsPackageType=None; tasarım 2026-09-30
/// masaüstü bildirimleri §1 Gösterim). Paketsiz uygulamada Register() çağıran süreci COM sunucusu olarak kaydeder; uygulama adı ve
/// simgesi kabuktan alınır. Pencereli süreç <see cref="Baslat"/> ile önce NotificationInvoked işleyicisini bağlar, sonra kaydolur:
/// uygulama açıkken tıklama bu süreçte olay olarak gelir. Uygulama kapalıyken tıklama yeni süreç başlatır; tıklama etkinleştirme
/// bağımsız değişkenlerinden (AppInstance.GetActivatedEventArgs, ExtendedActivationKind.AppNotification) ya da kayıttan sonra aynı
/// olayla gelir; ikisi de <see cref="Tiklamalar"/>'a yazılır (son değer geçerli). Aynı tıklama iki yoldan kısa arayla gelirse ikincisi
/// atlanır (rota iki kez açılmaz). Pencere açmadan çalışan görev işleyici bağlamadan kaydolur (ilk gösterimde): o bildirime tıklanırsa
/// Windows yeni (pencereli) süreç başlatır. Süreç bitmeden <see cref="Bitir"/> (Unregister) çağrılır: sonraki tıklamalar uygulamayı
/// yeniden başlatabilsin. Tıklama UI iş parçacığına Baslat'ta yakalanan DispatcherQueue ile aktarılır (MAUI MainThread pencere yokken
/// InvalidOperationException atar).
/// Kaynaklar: learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register,
/// learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart.
/// </summary>
public sealed class WindowsBildirimGosterici : IBildirimGosterici
{
    /// <summary>Aynı tıklamanın ikinci yoldan (etkinleştirme argümanı + olay) gelişini ayırt etme süresi.</summary>
    private const long CiftTiklamaMs = 5000;

    /// <summary>Sürecin tek örneği: App.xaml.cs DI kurulmadan önce başlatır, MauiProgram aynı örneği kaydeder.</summary>
    public static WindowsBildirimGosterici Ortak { get; } = new();

    private readonly Lock _kilit = new();
    private readonly Lock _tiklamaKilidi = new();
    private DispatcherQueue? _arayuz;
    private bool _basladi;
    private bool _kayitli;
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
                AppNotificationManager.Default.Register();
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

    /// <summary>Süreç bitmeden: Windows bildirim kaydı kaldırılır (kayıtlı değilse bir şey yapmaz).</summary>
    public void Bitir()
    {
        lock (_kilit)
        {
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
            Yayinla(new AppNotificationBuilder()
                .AddArgument(BildirimTiklamasi.HedefAnahtari, BildirimTiklamasi.DenemeHedefi)
                .AddText("Emar Kasa deneme bildirimi")
                .AddText("Windows bildirimleri bu bilgisayarda çalışıyor. Kart ve kredi hatırlatmaları böyle görünecek."));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Gösterir; süreç kayıtlı değilse (pencere açmadan çalışan görev) önce işleyicisiz kaydolur.</summary>
    private void Yayinla(AppNotificationBuilder icerik)
    {
        lock (_kilit)
        {
            if (!_kayitli)
            {
                AppNotificationManager.Default.Register();
                _kayitli = true;
            }
        }
        AppNotificationManager.Default.Show(icerik.BuildNotification());
    }

    /// <summary>Tıklama: UI iş parçacığında kuyruğa yazılır; uygulama zaten açıksa pencere öne getirilir. Olay arka plan iş
    /// parçacığında gelir; kapalı uygulamada aynı tıklama hem etkinleştirme argümanıyla hem olayla gelebilir, ikincisi atlanır.</summary>
    private void Tiklandi(IDictionary<string, string> argumanlar)
    {
        var tiklama = BildirimTiklamasi.Coz(argumanlar);
        if (!IlkKezMi(tiklama))
            return;
        if (_arayuz is null || _arayuz.HasThreadAccess)
        {
            Tiklamalar.Ekle(tiklama);
            return;
        }
        _arayuz.TryEnqueue(() =>
        {
            try
            {
                Tiklamalar.Ekle(tiklama);
                if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window pencere)
                    pencere.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bildirim tıklaması aktarılamadı: {ex}");
            }
        });
    }

    /// <summary>Aynı tıklama <see cref="CiftTiklamaMs"/> içinde ikinci kez gelirse false (bildirim tıklanınca kapanır; aynı bildirim
    /// pratikte bu sürede iki kez tıklanamaz).</summary>
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
