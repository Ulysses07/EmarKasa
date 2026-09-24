using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>İstemci tarafı doğrulama hatası: mesajı olduğu gibi kullanıcıya gösterilir.</summary>
public sealed class DogrulamaHatasi(string mesaj) : Exception(mesaj);

/// <summary>İstisnaları kullanıcıya gösterilecek Türkçe mesaja çevirir (tüm sayfalar ortak).</summary>
public static class HataMesaji
{
    public const string OturumSonaErdi = "Oturumunuz sona erdi, tekrar giriş yapın";
    public const string OturumlarKapatildi = "Tüm oturumlar kapatıldı. Tekrar giriş yapın.";
    public const string Ulasilamadi = "Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.";
    public const string SunucuHatasi = "Sunucuda bir hata oluştu. Biraz sonra tekrar deneyin.";
    public const string Yetkisiz = "Bu işlem için yetkiniz yok.";
    public const string Bulunamadi = "Kayıt bulunamadı; silinmiş olabilir. Sayfayı yenileyin.";
    public const string GecersizIstek = "Bilgiler geçersiz. Alanları kontrol edin.";
    public const string GirisBasarisiz = "Giriş başarısız. Bilgileri kontrol edin.";

    public static string Coz(Exception ex) => ex switch
    {
        DogrulamaHatasi d => d.Message,
        KasaApiException { DurumKodu: HttpStatusCode.Unauthorized } => OturumSonaErdi,
        KasaApiException { SunucuMesaji: { } m } when !string.IsNullOrWhiteSpace(m) => m,
        KasaApiException a => a.DurumKodu switch
        {
            HttpStatusCode.Forbidden => Yetkisiz,
            HttpStatusCode.NotFound => Bulunamadi,
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => GecersizIstek,
            HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => Ulasilamadi,
            >= HttpStatusCode.InternalServerError => SunucuHatasi,
            var k => $"İşlem başarısız ({(int)k}).",
        },
        HttpRequestException or TaskCanceledException or TimeoutException => Ulasilamadi,
        _ => "Beklenmeyen bir hata oluştu.",
    };

    /// <summary>Giriş ekranı: 401 yanlış bilgi demektir; diğer durumlar (429, 500, ağ) kendi mesajıyla.</summary>
    public static string GirisIcin(Exception ex) => ex switch
    {
        // Sunucu açıklama verdiyse (ör. "Bu hesap pasif") o gösterilir.
        KasaApiException { DurumKodu: HttpStatusCode.Unauthorized, SunucuMesaji: { } m } when !string.IsNullOrWhiteSpace(m) => m,
        KasaApiException { DurumKodu: HttpStatusCode.Unauthorized } => GirisBasarisiz,
        _ => Coz(ex),
    };

    /// <summary>Hata sunucuya hiç ulaşılamadığını mı gösteriyor (ağ yok / zaman aşımı / ağ geçidi hatası)?</summary>
    public static bool BaglantiHatasiMi(Exception ex) => ex switch
    {
        HttpRequestException or TaskCanceledException or TimeoutException => true,
        KasaApiException { DurumKodu: HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout } => true,
        _ => false,
    };
}
