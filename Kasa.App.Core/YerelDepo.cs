using System.Globalization;
using System.Text;

namespace Kasa.App.Core;

/// <summary>
/// Cihaza özel küçük ayarlar (anahtar → metin): bildirim anahtarları, tahmin ufku, "son bakış" gibi.
/// Sunucuya gitmez; her cihaz kendi değerini tutar. Okuma/yazma hataları yutulur (ayar kaybolursa
/// varsayılana dönülür, uygulama çalışmaya devam eder).
/// </summary>
public interface IYerelDepo
{
    /// <summary>Anahtarın değeri; yoksa (ya da okunamadıysa) null.</summary>
    string? Oku(string anahtar);

    /// <summary>Değeri yazar; null anahtarı siler.</summary>
    void Yaz(string anahtar, string? deger);
}

/// <summary>Uygulamanın kullandığı yerel anahtarlar (tek yerde, çakışmasın).</summary>
public static class YerelAnahtarlar
{
    // Panel · nakit tahmini
    public const string TahminGun = "tahmin.gun";
    public const string TahminHaricCekler = "tahmin.haric";

    // Geçmiş · "son bakışınızdan beri"
    public const string GecmisSonGorulenId = "gecmis.sonGorulenId";

    // Bildirim anahtarları (açık/kapalı; varsayılan açık)
    public const string BildirimHaftalikOzet = "bildirim.haftalikOzet";
    public const string BildirimVadesiGecenCek = "bildirim.vadesiGecenCek";
    public const string BildirimGecmiseDonuk = "bildirim.gecmiseDonuk";
    public const string BildirimBugunYapilacaklar = "bildirim.bugunYapilacaklar";
    public const string BildirimKartHatirlatma = "bildirim.kartHatirlatma";

    // Bildirim durumu (en son ne zaman / hangi satıra kadar gönderildi)
    public const string SonHaftalikOzet = "bildirim.sonHaftalikOzet";
    public const string SonGecmiseDonukId = "bildirim.sonGecmiseDonukId";
    public const string SonGecmiseDonukGunu = "bildirim.sonGecmiseDonukGunu";
    public const string SonYapilacaklar = "bildirim.sonYapilacaklar";
}

/// <summary>Tür dönüşümlü okuma/yazma yardımcıları (değişmez kültür).</summary>
public static class YerelDepoUzantilari
{
    public static bool OkuBool(this IYerelDepo d, string anahtar, bool varsayilan)
        => d.Oku(anahtar) switch { "1" => true, "0" => false, _ => varsayilan };

    public static void YazBool(this IYerelDepo d, string anahtar, bool deger) => d.Yaz(anahtar, deger ? "1" : "0");

    public static int? OkuInt(this IYerelDepo d, string anahtar)
        => int.TryParse(d.Oku(anahtar), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public static void YazInt(this IYerelDepo d, string anahtar, int? deger)
        => d.Yaz(anahtar, deger?.ToString(CultureInfo.InvariantCulture));

    public static DateOnly? OkuTarih(this IYerelDepo d, string anahtar)
        => DateOnly.TryParseExact(d.Oku(anahtar), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    public static void YazTarih(this IYerelDepo d, string anahtar, DateOnly? deger)
        => d.Yaz(anahtar, deger?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>"3,7,12" → {3, 7, 12}; bozuk parçalar atlanır.</summary>
    public static IReadOnlySet<int> OkuIdler(this IYerelDepo d, string anahtar)
    {
        var sonuc = new SortedSet<int>();
        foreach (var p in (d.Oku(anahtar) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) sonuc.Add(id);
        return sonuc;
    }

    public static void YazIdler(this IYerelDepo d, string anahtar, IEnumerable<int> idler)
    {
        var metin = string.Join(",", idler.Distinct().Order().Select(i => i.ToString(CultureInfo.InvariantCulture)));
        d.Yaz(anahtar, metin.Length == 0 ? null : metin);
    }
}

/// <summary>Bellekte depo: testler ve depo verilmeyen VM'ler için (uygulama kapanınca unutulur).</summary>
public sealed class BellekYerelDepo : IYerelDepo
{
    private readonly Dictionary<string, string> _degerler = new();
    private readonly object _kilit = new();

    public string? Oku(string anahtar) { lock (_kilit) return _degerler.GetValueOrDefault(anahtar); }

    public void Yaz(string anahtar, string? deger)
    {
        lock (_kilit)
        {
            if (deger is null) _degerler.Remove(anahtar);
            else _degerler[anahtar] = deger;
        }
    }
}

/// <summary>
/// Dosyada depo: her anahtar bir dosya (<see cref="HatirlatmaDurumu"/> gibi). Uygulama ve arka plan
/// hatırlatıcısı aynı klasörü paylaşır; bildirim anahtarları ikisinde de geçerlidir.
/// </summary>
public sealed class DosyaYerelDepo : IYerelDepo
{
    private readonly string _klasor;
    public DosyaYerelDepo(string klasor) => _klasor = klasor;

    /// <summary>Varsayılan konum: %LOCALAPPDATA%\EmarKasa\yerel.</summary>
    public static DosyaYerelDepo Varsayilan() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmarKasa", "yerel"));

    public string? Oku(string anahtar)
    {
        try
        {
            var yol = Yol(anahtar);
            return File.Exists(yol) ? File.ReadAllText(yol, Encoding.UTF8).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Yaz(string anahtar, string? deger)
    {
        try
        {
            var yol = Yol(anahtar);
            if (deger is null) { if (File.Exists(yol)) File.Delete(yol); return; }
            Directory.CreateDirectory(_klasor);
            File.WriteAllText(yol, deger, Encoding.UTF8);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Anahtar dosya adına güvenli çevrilir (harf, rakam, nokta, tire, alt çizgi).</summary>
    public string Yol(string anahtar)
    {
        var ad = new string(anahtar.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        if (ad.Length == 0 || ad.Trim('.').Length == 0) ad = "_" + ad;
        return Path.Combine(_klasor, ad + ".txt");
    }
}
