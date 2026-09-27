using CommunityToolkit.Mvvm.ComponentModel;
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Ortak Mesgul + Hata durumu ve güvenli çalıştırma sarmalayıcısı (nit c).</summary>
public partial class TemelViewModel : ObservableObject
{
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _hata;

    /// <summary>İşlemi Mesgul/Hata sarmalayıcısında çalıştırır; istisnada spinner iner, Hata yazılır.</summary>
    protected async Task CalistirAsync(Func<Task> islem)
    {
        Hata = null;
        Mesgul = true;
        try { await islem(); }
        catch (Exception hata) { Hata = HataMesaji(hata); }
        finally { Mesgul = false; }
    }

    protected static string HataMesaji(Exception hata) => hata switch
    {
        KasaApiException { DurumKodu: HttpStatusCode.Unauthorized } => "Oturumunuz sona erdi. Yeniden giriş yapın.",
        KasaApiException { DurumKodu: HttpStatusCode.Forbidden } => "Bu işlem için yetkiniz yok.",
        KasaApiException api when api.DurumKodu is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => api.Message,
        KasaApiException => "Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin.",
        // İstek sunucuya ulaşmış olabilir: kayıt işlemleri tamamlanmış olabileceği için önce kontrol istenir.
        TimeoutException => "Sunucu zamanında yanıt vermedi. İşlem sunucuda tamamlanmış olabilir; yeniden denemeden önce listeyi yenileyip kontrol edin. Büyük dosyalarda bağlantınızı kontrol edin.",
        HttpRequestException or TaskCanceledException => "Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.",
        _ => "İşlem tamamlanamadı. Lütfen yeniden deneyin.",
    };

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi: okuma sunucuda bir şey değiştirmez; zaman aşımında
    /// "işlem sunucuda tamamlanmış olabilir" denmez, yalnız yeniden deneme istenir.</summary>
    protected static string OkumaHataMesaji(Exception hata) => hata is TimeoutException
        ? "Sunucu zamanında yanıt vermedi. Bağlantınızı kontrol edip yeniden deneyin."
        : HataMesaji(hata);
}
