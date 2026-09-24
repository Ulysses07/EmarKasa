using Kasa.App.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.Platforms.Windows;

/// <summary>Windows App SDK toast bildirimleri (paketsiz .exe).</summary>
public sealed class WindowsBildirimServisi : IBildirimServisi
{
    private bool _kayitli;

    public void KayitOl()
    {
        if (_kayitli) return;
        var mgr = AppNotificationManager.Default;
        mgr.NotificationInvoked += (_, args) =>
        {
            if (args.Arguments.TryGetValue("git", out var hedef) && hedef == "kredikartlari")
                Yonlendir();
        };
        mgr.Register();
        _kayitli = true;
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

    private static void Yonlendir()
    {
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Microsoft.Maui.Controls.Shell.Current is { } shell)
                await shell.GoToAsync("//kartlar");
        });
    }
}
