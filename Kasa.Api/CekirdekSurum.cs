namespace Kasa.Api;

/// <summary>
/// Çekirdek kasa kayıtlarının (gider, gelir, kanal, ayarlar) iyimser eşzamanlılığı (contract-6). Kayıt her değiştiğinde sürümü
/// artar (KasaDbContext kaydetme kancası; gelir upsert'ünde SQL). Yazma isteği okuduğu sürümü gönderir: kayıttakiyle uyuşmazsa 409
/// ve kayıt değişmez. Gider silme dışında sürüm göndermeyen eski istemci (canlıdaki 2.3.0 masaüstü, önbellekteki eski web)
/// denetlenmez: eski davranış (son yazan kazanır) sürer, sürüm yine artar. Gider silme uç noktası sürümü zorunlu tutar.
/// Yeni istemciler (web, masaüstü) her düzenlemede sürümü gönderir.
/// </summary>
internal static class CekirdekSurum
{
    internal const string GiderIletisi = "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";
    internal const string KanalIletisi = "Kanal başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";
    internal const string AyarIletisi = "Ayarlar başka bir oturumda değişti. Güncel değerleri yükleyip tekrar deneyin.";
    internal const string GelenIletisi = "Bu dönem ve kanalın geliri başka bir oturumda değişti. Güncel toplamı yükleyip tekrar deneyin.";

    /// <summary>İstek sürüm gönderdiyse ve kayıttakinden farklıysa 409 (iletisiyle); göndermediyse ya da aynıysa null.</summary>
    internal static IResult? Denetle(int? istek, int kayit, string ileti) =>
        istek is { } surum && surum != kayit ? Results.Conflict(new { hata = ileti }) : null;
}
