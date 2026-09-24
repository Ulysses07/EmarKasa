using Kasa.App.Core;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.Platforms.Windows;

/// <summary>Windows App SDK toast bildirimleri (paketsiz .exe).</summary>
public sealed class WindowsBildirimServisi : IBildirimServisi
{
    private readonly Yonlendirme? _yonlendirme;
    private bool _kayitli;

    /// <param name="yonlendirme">Toast'a tıklanınca istenen sayfa buraya yazılır; Shell oturum açıkken uygular.
    /// Headless hatırlatıcıda null (orada yönlendirme yok).</param>
    public WindowsBildirimServisi(Yonlendirme? yonlendirme = null) => _yonlendirme = yonlendirme;

    public void KayitOl()
    {
        if (_kayitli) return;
        var mgr = AppNotificationManager.Default;
        // Uygulama açıkken tıklama: bu olay gelir (arka plan iş parçacığında).
        mgr.NotificationInvoked += (_, args) => HedefiIste(args.Arguments);
        mgr.Register();
        _kayitli = true;

        // Uygulama kapalıyken tıklama: exe bildirim argümanlarıyla başlatılır; olay GELMEZ,
        // hedef etkinleştirme argümanlarından okunmalıdır.
        try
        {
            var etkinlestirme = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (etkinlestirme.Kind == ExtendedActivationKind.AppNotification
                && etkinlestirme.Data is AppNotificationActivatedEventArgs bildirim)
                HedefiIste(bildirim.Arguments);
        }
        catch (Exception) { /* etkinleştirme bilgisi okunamazsa normal açılış */ }
    }

    private void HedefiIste(IDictionary<string, string> argumanlar)
    {
        if (argumanlar.TryGetValue("git", out var hedef))
            _yonlendirme?.Iste(hedef);   // giriş yapılmamışsa login sonrasına bekletilir
    }

    public Task GosterAsync(IReadOnlyList<Hatirlatma> hatirlatmalar)
    {
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        foreach (var h in hatirlatmalar)
        {
            var metin = h.Tur switch
            {
                HatirlatmaTuru.Kesim        => $"Ekstre kesildi — güncel borç {Bicim.Tl(h.EkstreBorc)}",
                HatirlatmaTuru.SonOdeme3Gun => $"Son ödemeye 3 gün — {Bicim.Tl(h.EkstreBorc)}",
                _ when h.Tarih == bugun     => $"Son ödeme bugün — {Bicim.Tl(h.EkstreBorc)}. Ödedin mi?",
                _                           => $"Son ödeme günü {h.Tarih:dd.MM} idi — kalan {Bicim.Tl(h.EkstreBorc)}",
            };
            var toast = new AppNotificationBuilder()
                .AddArgument("git", "kredikartlari")               // gövdeye tıklama da karta götürür
                .AddText(h.KartAd)
                .AddText(metin)
                .AddButton(new AppNotificationButton("Uygulamada işaretle")
                    .AddArgument("git", "kredikartlari")
                    .AddArgument("kartId", h.KartId.ToString()))
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        return Task.CompletedTask;
    }

    /// <summary>Düğmesiz bilgi bildirimi (örn. oturum süresi doldu).</summary>
    public void MetinGoster(string baslik, string metin)
    {
        var toast = new AppNotificationBuilder().AddText(baslik).AddText(metin).BuildNotification();
        AppNotificationManager.Default.Show(toast);
    }
}
