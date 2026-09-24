using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Servisler;

/// <summary>
/// Fiş/fatura eklerinin diskteki deposu. Klasör <c>Kasa:BelgeKlasoru</c>; tanımlı değilse
/// veritabanı dosyasının yanındaki <c>belgeler/</c> (Docker'da <c>/data/belgeler</c>).
/// <list type="bullet">
/// <item>Dosya adı kullanıcıdan gelmez: 32 hex rastgele ad + içerikten belirlenen uzantı.</item>
/// <item>Tür, uzantıya DEĞİL dosyanın ilk baytlarına (imza) göre belirlenir; uzantı imzayla
///       uyuşmazsa ya da tür izinli değilse reddedilir.</item>
/// <item>Önce geçici dosyaya yazılır, sonra atomik olarak yerine taşınır.</item>
/// </list>
/// </summary>
public sealed partial class BelgeDeposu
{
    /// <summary>Dosya başına en fazla boyut (10 MB).</summary>
    public const long EnFazlaBoyut = 10L * 1024 * 1024;
    /// <summary>İşlem başına en fazla ek sayısı.</summary>
    public const int IslemBasinaEnFazla = 10;
    /// <summary>Özgün adın saklanan en fazla uzunluğu.</summary>
    public const int AdEnFazla = 120;

    /// <summary>İzinli uzantılar (küçük harf, noktasız).</summary>
    public static readonly IReadOnlyList<string> IzinliUzantilar = ["jpg", "jpeg", "png", "webp", "heic", "pdf"];

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp|heic|pdf)$")]
    private static partial Regex DepoAdiKalibi();

    public string Klasor { get; }

    public BelgeDeposu(IConfiguration cfg, IWebHostEnvironment env)
        : this(KlasorBul(cfg["Kasa:BelgeKlasoru"], cfg.GetConnectionString("Kasa"), env.ContentRootPath)) { }

    public BelgeDeposu(string klasor) => Klasor = Path.GetFullPath(klasor);

    /// <summary>
    /// Ek klasörü: açık ayar; yoksa DB dosyasının klasöründeki <c>belgeler</c>; DB bellekteyse
    /// geçici klasördeki <c>kasa-belgeler</c>.
    /// </summary>
    public static string KlasorBul(string? ayar, string? baglanti, string icerikKoku)
    {
        if (!string.IsNullOrWhiteSpace(ayar)) return ayar;
        string? kaynak = null;
        try { kaynak = new SqliteConnectionStringBuilder(baglanti ?? "Data Source=kasa.db").DataSource; }
        catch (ArgumentException) { }
        if (string.IsNullOrWhiteSpace(kaynak) || kaynak.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            || kaynak.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetTempPath(), "kasa-belgeler");
        var tam = Path.IsPathRooted(kaynak) ? kaynak : Path.GetFullPath(kaynak);
        return Path.Combine(Path.GetDirectoryName(tam) ?? icerikKoku, "belgeler");
    }

    /// <summary>Depo adı bizim ürettiğimiz biçimde mi (yol kaçışına karşı her okumada doğrulanır).</summary>
    public static bool GecerliDepoAdi(string ad) => DepoAdiKalibi().IsMatch(ad);

    /// <summary>Tam yol; depo adı geçersizse null.</summary>
    public string? Yol(string depoAdi) => GecerliDepoAdi(depoAdi) ? Path.Combine(Klasor, depoAdi) : null;

    /// <summary>
    /// İçeriğin türünü ilk baytlarından belirler: (uzantı, içerik tipi) ya da tanınmıyorsa null.
    /// JPEG FF D8 FF · PNG 89 50 4E 47 0D 0A 1A 0A · WEBP "RIFF"....“WEBP” · HEIC ....“ftyp” + heic/heix/heim/heis/hevc/hevx/mif1/msf1 · PDF "%PDF-".
    /// </summary>
    public static (string Uzanti, string IcerikTipi)? TurBelirle(ReadOnlySpan<byte> bas)
    {
        if (bas.Length >= 3 && bas[0] == 0xFF && bas[1] == 0xD8 && bas[2] == 0xFF) return ("jpg", "image/jpeg");
        if (bas.Length >= 8 && bas[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("png", "image/png");
        if (bas.Length >= 12 && bas[..4].SequenceEqual("RIFF"u8) && bas[8..12].SequenceEqual("WEBP"u8)) return ("webp", "image/webp");
        if (bas.Length >= 12 && bas[4..8].SequenceEqual("ftyp"u8))
        {
            var marka = Encoding.ASCII.GetString(bas[8..12]);
            if (marka is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" or "mif1" or "msf1") return ("heic", "image/heic");
        }
        if (bas.Length >= 5 && bas[..5].SequenceEqual("%PDF-"u8)) return ("pdf", "application/pdf");
        return null;
    }

    /// <summary>Kullanıcının verdiği uzantı, içerikten belirlenen türle uyuşuyor mu (jpeg = jpg).</summary>
    public static bool UzantiUyuyor(string dosyaAdi, string tur)
    {
        var u = Path.GetExtension(dosyaAdi).TrimStart('.').ToLowerInvariant();
        if (u == "jpeg") u = "jpg";
        return u == tur;
    }

    /// <summary>
    /// Özgün adı gösterime uygun hale getirir: yol parçaları, kontrol karakterleri ve dosya
    /// sistemlerinde sorun çıkaran karakterler atılır, en fazla <see cref="AdEnFazla"/> karakter.
    /// </summary>
    public static string AdTemizle(string? ad)
    {
        var s = (ad ?? "").Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsControl(c) || c is '"' or '<' or '>' or '|' or ':' or '*' or '?' or '/' or '\\') continue;
            sb.Append(c);
        }
        var t = sb.ToString().Trim().Trim('.');
        if (t.Length == 0) t = "belge";
        if (t.Length > AdEnFazla)
        {
            var uz = Path.GetExtension(t);
            if (uz.Length > 10) uz = "";
            t = t[..(AdEnFazla - uz.Length)] + uz;
        }
        return t;
    }

    /// <summary>
    /// İçeriği geçici dosyaya yazar ve yeni bir depo adıyla yerine taşır; depo adını döner.
    /// Kayıt (DB satırı) başarısız olursa çağıran <see cref="Sil"/> ile dosyayı kaldırmalıdır.
    /// </summary>
    public async Task<string> YazAsync(Stream icerik, string uzanti, CancellationToken iptal)
    {
        Directory.CreateDirectory(Klasor);
        var ad = $"{Guid.NewGuid():N}.{uzanti}";
        var hedef = Path.Combine(Klasor, ad);
        var gecici = Path.Combine(Klasor, $".{ad}.tmp");
        try
        {
            await using (var f = new FileStream(gecici, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await icerik.CopyToAsync(f, iptal);
            File.Move(gecici, hedef);
        }
        catch
        {
            try { File.Delete(gecici); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return ad;
    }

    /// <summary>Dosyayı siler (yoksa sessizce geçer). Silinemezse false (gece temizliği yeniden dener).</summary>
    public bool Sil(string depoAdi)
    {
        if (Yol(depoAdi) is not { } yol) return false;
        try { File.Delete(yol); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
