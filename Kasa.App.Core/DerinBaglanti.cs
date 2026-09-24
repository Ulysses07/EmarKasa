using System.Globalization;

namespace Kasa.App.Core;

/// <summary>
/// "Bugün yapılacaklar" satırlarının derin bağlantıları: Shell rotası + sorgu. Hedef sayfa (IQueryAttributable)
/// sorguyu VM'ine iletir; VM formu o kayıtla hazırlar ya da kaydı açar. Hiçbiri kendiliğinden KAYDETMEZ:
/// son adım (Kaydet / Ekle) kullanıcınındır.
/// <list type="bullet">
/// <item><c>//islemler?donem=2026-09-14&amp;kanal=MEZAT</c>: gelen formu eksik dönem ve kanalla açılır.</item>
/// <item><c>//cekler?id=12</c>: çek düzenleme formunda açılır.</item>
/// <item><c>//kartlar?id=3</c>: kart vurgulanır, ödeme girişi ekstre borcuyla doldurulur.</item>
/// </list>
/// Shell, IQueryAttributable'a değerleri kod çözmeden verir; <see cref="Oku"/> çözer (kanal adı Türkçe harf,
/// boşluk ya da &amp; içerebilir).
/// </summary>
public static class DerinBaglanti
{
    public const string DonemAnahtari = "donem";
    public const string KanalAnahtari = "kanal";
    public const string IdAnahtari = "id";

    /// <summary>Gelen formunu dönemin başlangıcı ve (varsa) kanalla açan bağlantı.</summary>
    public static string GelenGir(DateOnly donemStart, string? kanal)
    {
        var s = $"{YapilacakListesi.RotaIslemler}?{DonemAnahtari}={donemStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return string.IsNullOrWhiteSpace(kanal) ? s : $"{s}&{KanalAnahtari}={Uri.EscapeDataString(kanal)}";
    }

    /// <summary>Çeki düzenleme formunda açan bağlantı.</summary>
    public static string Cek(int id) => $"{YapilacakListesi.RotaCekler}?{IdAnahtari}={id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Kartı vurgulayıp ödeme girişini hazırlayan bağlantı.</summary>
    public static string Kart(int id) => $"{YapilacakListesi.RotaKartlar}?{IdAnahtari}={id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Bağlantının rota kısmı ("//cekler?id=12" → "//cekler").</summary>
    public static string Rota(string hedef)
    {
        var i = hedef.IndexOf('?');
        return i < 0 ? hedef : hedef[..i];
    }

    /// <summary>Sorgudaki değer (kodu çözülmüş, kırpılmış); yoksa ya da boşsa null.</summary>
    public static string? Oku(IDictionary<string, object>? sorgu, string anahtar)
    {
        if (sorgu is null || !sorgu.TryGetValue(anahtar, out var ham) || ham is null) return null;
        var deger = Uri.UnescapeDataString(ham.ToString() ?? "").Trim();
        return deger.Length == 0 ? null : deger;
    }

    /// <summary>"id" (pozitif tam sayı); yoksa ya da geçersizse null.</summary>
    public static int? Id(IDictionary<string, object>? sorgu)
        => int.TryParse(Oku(sorgu, IdAnahtari), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    /// <summary>"donem" (yyyy-MM-dd); yoksa ya da geçersizse null.</summary>
    public static DateOnly? Donem(IDictionary<string, object>? sorgu)
        => DateOnly.TryParseExact(Oku(sorgu, DonemAnahtari), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>
    /// Bağlantının sorgusunu Shell'in verdiği biçimde ayrıştırır (değerler kodlu kalır). Testler ve sayfa dışı
    /// gezinme için; Shell kendi ayrıştırmasını yapar.
    /// </summary>
    public static Dictionary<string, object> Sorgu(string hedef)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        var i = hedef.IndexOf('?');
        if (i < 0) return d;
        foreach (var parca in hedef[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = parca.Split('=');
            if (kv.Length == 2) d[kv[0]] = kv[1];
        }
        return d;
    }
}
