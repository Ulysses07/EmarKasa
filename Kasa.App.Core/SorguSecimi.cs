using System.Globalization;

namespace Kasa.App.Core;

/// <summary>
/// Sayfaya Shell sorgu parametresiyle istenen kayıt seçimi (bildirim tıklaması: //kartlar?KartId=3, //krediler?KrediId=2;
/// KartTakipPage ve KrediTakipPage). Shell zaten görünen sayfaya gezinmede yalnız ApplyQueryAttributes çağırır, OnAppearing
/// çağırmayabilir: sayfa görünürken (<see cref="Gorunuyor"/>) gelen istek hemen uygulanır, görünmezken sonraki görünüşte
/// (OnAppearing) uygulanır. Başarılı yüklemeden sonra kimlik, kayıt bulunamasa da temizlenir (her ziyarette "bulunamadı" hatası
/// tekrarlanmaz); yükleme başarısızsa kimlik sonraki görünüşe kalır. Uygulama başlarken kimlik alınır: eşzamanlı ikinci çağrı
/// (görünüş ile sorgu aynı anda) yeniden yüklemez.
/// </summary>
/// <param name="anahtar">Sorgu parametresinin adı (KartId, KrediId).</param>
public sealed class SorguSecimi(string anahtar)
{
    /// <summary>Sayfa şu an görünüyor mu (sayfa OnAppearing'de true, OnDisappearing'de false yazar).</summary>
    public bool Gorunuyor { get; set; }

    /// <summary>Uygulanmayı bekleyen kimlik; yoksa null.</summary>
    public int? Istenen { get; private set; }

    /// <summary>Sorgudaki pozitif kimliği bekleyen seçim olarak alır (geçersiz ya da eksik değer yok sayılır). Sayfa görünüyorsa
    /// true döner: seçim hemen uygulanmalıdır.</summary>
    public bool Iste(IDictionary<string, object> sorgu)
    {
        if (!sorgu.TryGetValue(anahtar, out var deger)
            || !int.TryParse(Convert.ToString(deger, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            return false;
        Istenen = id;
        return Gorunuyor;
    }

    /// <summary>Bekleyen kimlik varsa listeyi yükler (<paramref name="yukle"/>), yükleme başarılıysa (<paramref name="hazir"/>)
    /// seçer (<paramref name="sec"/>) ve seçimin sonucunu döner. Bekleyen yoksa ya da yükleme başarısızsa false.</summary>
    public async Task<bool> UygulaAsync(Func<Task> yukle, Func<bool> hazir, Func<int, bool> sec)
    {
        if (Istenen is not { } id)
            return false;
        Istenen = null;
        await yukle();
        if (!hazir())
        {
            // Bu arada yeni istek geldiyse o geçerlidir; gelmediyse kimlik sonraki görünüşte yeniden denenir.
            Istenen ??= id;
            return false;
        }
        return sec(id);
    }
}
