using Kasa.App.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.Platforms.Windows;

// Paket A — tek satırlık bildirimler (haftalık özet, vadesi geçen çek, geçmişe dönük düzeltme, bugün yapılacaklar).
public sealed partial class WindowsBildirimServisi
{
    /// <summary>Başlık + metin; hedef varsa tıklama o sayfayı açar (<see cref="Yonlendirme.RotaCoz"/>).</summary>
    public void Goster(KisaBildirim bildirim)
    {
        var b = new AppNotificationBuilder();
        if (!string.IsNullOrEmpty(bildirim.Hedef)) b.AddArgument("git", bildirim.Hedef);
        var toast = b.AddText(bildirim.Baslik).AddText(bildirim.Metin).BuildNotification();
        AppNotificationManager.Default.Show(toast);
    }
}
