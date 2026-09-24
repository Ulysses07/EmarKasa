using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Platforms.Windows;

// Paket A — günlük arka plan hatırlatıcısında (09:00, kaçırılırsa ilk fırsatta) haftalık özet, vadesi geçen
// çekler, geçmişe dönük düzeltme ve (yalnız editöre) bugün yapılacaklar. Kart hatırlatmalarından bağımsızdır;
// hatası yutulur. Aç/kapa seçimleri uygulamayla aynı yerel depodan okunur.
public static partial class HatirlatmaKontrol
{
    private static bool KartHatirlatmaAcik()
        => DosyaYerelDepo.Varsayilan().OkuBool(YerelAnahtarlar.BildirimKartHatirlatma, true);

    private static async Task PaketABildirimleriAsync(IServiceProvider sp)
    {
        try
        {
            if (sp.GetService(typeof(IKasaApi)) is not IKasaApi api) return;
            var rol = SekmeModeli.RolCoz(await api.BenKimAsync());   // oturum yoksa 401 → sessizce çık
            var zaman = sp.GetService(typeof(TimeProvider)) as TimeProvider;
            await new BildirimPlanlayici(api, DosyaYerelDepo.Varsayilan(), new TembelBildirim(), zaman)
                .CalistirAsync(rol);
        }
        catch { /* oturum yok / ağ hatası: sonraki çalıştırmada yeniden denenir */ }
    }

    /// <summary>
    /// Toast kaydı yalnız gösterilecek bildirim olduğunda yapılır (kart hatırlatmalarındaki gibi). Aynı süreçte
    /// kart hatırlatması zaten kaydolduysa Register() tekrarlanmaz (<see cref="WindowsBildirimServisi.KayitOl"/>).
    /// Kayıt hata atarsa servis tutulmaz; sonraki bildirim yeniden dener.
    /// </summary>
    private sealed class TembelBildirim : IKisaBildirim
    {
        private WindowsBildirimServisi? _servis;

        public void Goster(KisaBildirim bildirim)
        {
            if (_servis is null)
            {
                var servis = new WindowsBildirimServisi();
                servis.KayitOl();
                _servis = servis;
            }
            _servis.Goster(bildirim);
        }
    }
}
