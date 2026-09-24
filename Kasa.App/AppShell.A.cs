using Kasa.ApiClient;
using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App;

// Paket A — oturum açılınca (menü açılışında bir kez) uygulama içi bildirimler: haftalık özet, vadesi geçen
// çekler, geçmişe dönük düzeltme. "Bugün yapılacaklar" yalnız sabah arka plan hatırlatıcısında gönderilir.
// Bildirim servisi olmayan platformda (Windows dışı) hiçbir şey yapmaz; hata uygulamayı etkilemez.
public partial class AppShell
{
    private async Task PaketABildirimleriAsync()
    {
        try
        {
            var sp = IPlatformApplication.Current?.Services;
            if (sp is null) return;
            if (sp.GetService<IKisaBildirim>() is not { } bildirim || sp.GetService<IKasaApi>() is not { } api) return;
            var depo = sp.GetService<IYerelDepo>() ?? DosyaYerelDepo.Varsayilan();
            var zaman = sp.GetService<TimeProvider>();
            var rol = _auth.AktifRol;
            await Task.Run(() => new BildirimPlanlayici(api, depo, bildirim, zaman).CalistirAsync(rol, arkaPlan: false));
        }
        catch (Exception) { /* bildirim gönderilemedi: sonraki açılışta / sabah yeniden denenir */ }
    }
}
