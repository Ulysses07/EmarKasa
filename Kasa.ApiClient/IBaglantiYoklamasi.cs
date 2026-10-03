namespace Kasa.ApiClient;

/// <summary>Bağlantı kopukken sunucunun geri gelip gelmediğini anlamak için hafif yoklama (ekran denemesi H-2): token göndermeden
/// GET /health. Sonuç <see cref="IBaglantiBildirimleri"/> olaylarıyla da bildirilir; ulaşılamazsa istisna çıkar.</summary>
public interface IBaglantiYoklamasi
{
    Task YoklaAsync(CancellationToken ct = default);
}
