namespace Kasa.ApiClient;

/// <summary>Sunucuya ulaşılabilirliğin bildirimi (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3); UI bağımlılığı
/// içermez. Olaylar isteği gönderen iş parçacığında gelir; dinleyen UI'ya kendisi aktarır.</summary>
public interface IBaglantiBildirimleri
{
    /// <summary>Sunucudan HTTP yanıtı alındı. Durum kodu ne olursa olsun (4xx ve 5xx dahil) sunucuya ulaşılmıştır; 502, 504 ve
    /// iletisiz 503 bu kuralın dışındadır (bkz. <see cref="SunucuyaUlasilamadi"/>). API'nin kendi iletili 503'ü (ör. "Veritabanı
    /// meşgul.") sunucunun anlamlı yanıtıdır: ulaşıldı sayılır.</summary>
    event EventHandler? SunucuyaUlasildi;

    /// <summary>İstek sunucuya ulaşamadı (<see cref="HttpRequestException"/>), süre sınırında yanıt gelmedi
    /// (<see cref="TimeoutException"/>) ya da yanıt 502 Bad Gateway, iletisiz 503 Service Unavailable veya 504 Gateway Timeout
    /// oldu (ürün sahibi kararı 2026-10-03: proxy/ağ geçidi hataları "sunucuya ulaşılamıyor" sayılır; iletili 503 sayılmaz). Olay istisna istemciden
    /// çıkmadan önce gelir; çağıranın iptali bildirilmez.</summary>
    event EventHandler<Exception>? SunucuyaUlasilamadi;
}
