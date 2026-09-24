using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket A — "Bugün yapılacaklar" → "Çeki aç" derin bağlantısı (DerinBaglanti.Cek).
public partial class CeklerViewModel
{
    public const string CekBulunamadiMesaji = "Çek bulunamadı; silinmiş olabilir.";

    /// <summary>Sorgudaki çeki (<c>?id=</c>) düzenleme formunda açar; sorguda geçerli id yoksa hiçbir şey yapmaz.</summary>
    public Task CekIstegiUygulaAsync(IDictionary<string, object>? sorgu)
        => DerinBaglanti.Id(sorgu) is { } id ? CekAcAsync(id) : Task.CompletedTask;

    /// <summary>
    /// Çeki düzenleme formunda açar (tahsil edildi / ödendi olarak işaretleyip kaydetmek kullanıcıya kalır).
    /// Liste filtresinden bağımsızdır: çek kendi isteğiyle okunur. Sayfa yüklemesiyle aynı anda çalışabilir;
    /// yükleme formu sıfırlamaz, kanal çipleri kanallar gelince yeniden kurulur.
    /// </summary>
    public Task CekAcAsync(int id) => CalistirAsync(async () =>
    {
        var cek = (await _api.CeklerAsync()).FirstOrDefault(c => c.Id == id);
        Dogrula(cek is not null, CekBulunamadiMesaji);
        Duzenle(new CekGorunum(cek!, BugunTarih));
    });
}
