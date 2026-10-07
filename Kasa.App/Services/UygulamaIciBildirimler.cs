using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Services;

/// <summary>iOS'ta uygulama içindeki hatırlatma listesi ve rozet için ön plan durumu. Kalıcı Windows anahtarı kullanılmaz;
/// uygulama arka plana geçince yeni bildirim yoklaması başlamaz. İşletim sistemi bildirimi veya arka plan görevi sağlamaz.</summary>
public sealed class UygulamaIciBildirimAyari : IBildirimAyari
{
    private int _onPlanda = 1;

    public bool Acik
    {
        get => Volatile.Read(ref _onPlanda) != 0;
        set => OnPlanAyarla(value);
    }

    /// <summary>Değişiklik olduysa true; ön plana dönüşte bir kez yenilemek için kullanılır.</summary>
    public bool OnPlanAyarla(bool onPlanda) => Interlocked.Exchange(ref _onPlanda, onPlanda ? 1 : 0) != (onPlanda ? 1 : 0);
}

/// <summary>Yerel iOS/uzak APNs gösterimi uygulanmadı. Ortak yoklayıcı okunmamış sayısını yükler, fakat hiçbir bildirimi
/// işletim sisteminde gösterilmiş diye ayırmaz. Hatırlatmalar Bildirimler ekranından okunur.</summary>
public sealed class UygulamaIciBildirimGosterici : IBildirimGosterici
{
    public bool WindowsAyarindaKapali => false;
    public bool GostermeyeHazir => false;
    public bool DenemeGoster() => false;

    public void Goster(BildirimDto bildirim) => throw new NotSupportedException("Bu platformda sistem bildirimi gösterimi uygulanmadı.");
}

/// <summary>iOS'ta Windows zamanlanmış görevi kurulmaz; sunucudaki hatırlatma saati uygulama içi liste için korunur.</summary>
public sealed class BildirimGoreviYok : IBildirimGorevi
{
    public bool Kurulu => false;
    public Task<bool> GuncelleAsync(int saat, int dakika) => Task.FromResult(false);
    public Task<bool> SilAsync() => Task.FromResult(true);
}

/// <summary>Bağlantı kopukken çalışan sağlık yoklaması iOS uygulaması görünür değilken yeni ağ isteği başlatmaz.
/// Atlanan yoklama başarı sayılmaz; mevcut kopuk durum korunur.</summary>
public sealed class OnPlanBaglantiYoklamasi(IBaglantiYoklamasi yoklama, UygulamaIciBildirimAyari ayar) : IBaglantiYoklamasi
{
    public Task YoklaAsync(CancellationToken ct = default) => ayar.Acik
        ? yoklama.YoklaAsync(ct)
        : Task.FromCanceled(new CancellationToken(canceled: true));
}
