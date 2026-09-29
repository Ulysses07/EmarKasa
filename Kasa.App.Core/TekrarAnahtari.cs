namespace Kasa.App.Core;

/// <summary>İstemcinin tek tekrar anahtarı mekanizması (sunucu tekrar koruması, IstekId): aynı gövdenin yeniden gönderimi aynı
/// kimliği taşır, yanıtı kaybolan (ağ hatası, zaman aşımı) istek sunucuda ikinci kez işlenmez. Gövde değişince yeni kimlik
/// alınır; yalnız son gövde tutulur (önceki gövdeye dönmek yeni kimlik alır). Başarıdan, sunucu reddinden ve form
/// sıfırlanmasından sonra çağıran <see cref="Temizle"/> ile kimliği yeniler. Gövde, isteğin hedefini (kayıt kimliği) de
/// taşımalıdır: başka kaydın isteği aynı kimliği almaz.</summary>
public sealed class TekrarAnahtari
{
    private string? _govde;
    private Guid _id;
    public Guid Al(object govde)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(govde);
        if (_govde != json)
        { _govde = json; _id = Guid.NewGuid(); }
        return _id;
    }
    public void Temizle() { _govde = null; _id = Guid.Empty; }
}

/// <summary>Kayıt (ör. kart) başına tekrar anahtarı: bir kaydın yanıtı belirsiz kalan isteğinin anahtarı başka kayıtta yapılan
/// işlemlerle ezilmez; aynı kayda dönülüp aynı gövde yeniden gönderilince aynı anahtar kullanılır (sunucu ikinci kez işlemez).
/// Gövde kaydın kimliğini de taşıdığından başka kaydın isteği hiçbir zaman bu anahtarı almaz.</summary>
public sealed class KayitBasinaTekrarAnahtari
{
    private readonly Dictionary<int, TekrarAnahtari> _kayitlar = new();
    public Guid Al(int kayitId, object govde)
    {
        if (!_kayitlar.TryGetValue(kayitId, out var anahtar))
            _kayitlar[kayitId] = anahtar = new();
        return anahtar.Al(govde);
    }
    public void Temizle(int kayitId) => _kayitlar.Remove(kayitId);
    public void Temizle() => _kayitlar.Clear();
}
