using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Servisler;

/// <summary>Belge deposunda özeti verilen dosya yok (silinmiş, taşınmamış ya da geri yüklemede unutulmuş).</summary>
public sealed class BelgeDosyasiYokException(string ozet) : FileNotFoundException($"Belge dosyası bulunamadı: {ozet}.")
{
    public string Ozet { get; } = ozet;
}

/// <summary>Depoya yazılan içeriğin özeti (SHA-256, 64 haneli büyük harf onaltılık) ve bayt sayısı.</summary>
public readonly record struct BelgeYazimi(string Ozet, long Boyut);

/// <summary>
/// İçerik adresli belge deposu (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8): alış belgeleri ve ekstre PDF'leri
/// veritabanında değil <c>&lt;kök&gt;/&lt;özet[0..2]&gt;/&lt;özet&gt;</c> dosyalarında durur; veritabanı yalnız özeti tutar
/// (Belgeler.IcerikOzeti, EkstreBelgeler.DosyaOzeti ile aynı biçim). Böylece her yedek bütün belgeleri yeniden kopyalamaz
/// (yedek aynası artımlıdır, bkz. <see cref="YedekServisi"/>), bütünlük denetimi ve yedek kopyası kısa sürer.
/// <list type="bullet">
/// <item>Kök: <c>Belge:Dizin</c>; verilmezse veritabanı dosyasının klasörü altında <c>belgeler</c>. Bellek içi veritabanında
/// (yalnız test/geliştirme) geçici dizin.</item>
/// <item>Yazma: geçici dosya + diske işleme (fsync) + atomik yeniden adlandırma; aynı içerik zaten varsa boyutu ve özeti doğrulanır
/// (bozuksa doğrulanmış yeni içerikle değiştirilir), dosya tekrar yazılmaz. Aynı içerikli belgeler tek dosyayı paylaşır.</item>
/// <item>Dosya adı yalnız özetten gelir (kullanıcının verdiği ad yol olamaz); Unix'te dizinler 0700, dosyalar 0600.</item>
/// <item>Silme yoktur: belge silme yumuşak silmedir, içerik korunur. Hiçbir satırın göstermediği (yazılıp veritabanına
/// kaydedilemeyen) ve <see cref="Temizle"/>'ye verilen yaştan eski dosyalar bakımda kaldırılır.</item>
/// </list>
/// </summary>
public sealed partial class BelgeDeposu
{
    public const string VarsayilanKlasor = "belgeler";
    private const string GeciciUzanti = ".yaziliyor";
    private readonly object _kilit = new();
    private readonly ILogger? _logger;
    private readonly TimeProvider _saat;

    public BelgeDeposu(IConfiguration cfg, ILogger<BelgeDeposu> logger, TimeProvider saat) : this(KokDizini(cfg), logger, saat) { }

    public BelgeDeposu(string kok, ILogger? logger = null, TimeProvider? saat = null)
    {
        Kok = Path.GetFullPath(kok);
        _logger = logger;
        _saat = saat ?? TimeProvider.System;
    }

    /// <summary>Deponun tam yolu.</summary>
    public string Kok { get; }

    /// <summary>Bellek içi veritabanı için geçici dizinde depo (yalnız test/geliştirme; süreçle birlikte anlamını yitirir).</summary>
    public static BelgeDeposu Gecici() => new(Path.Combine(Path.GetTempPath(), "kasa-belgeler-" + Guid.NewGuid().ToString("N")));

    /// <summary><c>Belge:Dizin</c>; yoksa <c>ConnectionStrings:Kasa</c> veritabanı dosyasının klasörü/belgeler; bellek içi
    /// veritabanında geçici dizin.</summary>
    public static string KokDizini(IConfiguration cfg)
    {
        if (cfg["Belge:Dizin"] is { Length: > 0 } dizin)
            return Path.GetFullPath(dizin);
        var baglanti = new SqliteConnectionStringBuilder(cfg.GetConnectionString("Kasa") ?? "Data Source=kasa.db");
        if (baglanti.Mode == SqliteOpenMode.Memory || baglanti.DataSource is "" or ":memory:")
            return Path.Combine(Path.GetTempPath(), "kasa-belgeler-" + Guid.NewGuid().ToString("N"));
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baglanti.DataSource))!, VarsayilanKlasor);
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex OzetKalibi();

    /// <summary>64 haneli büyük harf onaltılık SHA-256.</summary>
    public static bool GecerliOzet(string? ozet) => ozet is not null && OzetKalibi().IsMatch(ozet);

    /// <summary>Özetin dosya yolu (<c>&lt;kök&gt;/AB/AB…</c>). Geçersiz özet yol üretmez.</summary>
    public string Yol(string ozet) => DosyaYolu(Kok, ozet);

    /// <summary>Verilen kök altında özetin yolu (depo ve yedek aynası aynı düzeni kullanır).</summary>
    public static string DosyaYolu(string kok, string ozet)
    {
        if (!GecerliOzet(ozet))
            throw new ArgumentException("Geçersiz belge özeti.", nameof(ozet));
        return Path.Combine(kok, ozet[..2], ozet);
    }

    /// <summary>Akışın tamamını depoya yazar, özetini ve boyutunu döner (bkz. sınıf açıklaması). Hata ya da iptalde geçici dosya
    /// kalmaz; depodaki mevcut dosyalar değişmez.</summary>
    public BelgeYazimi Yaz(Stream kaynak, CancellationToken ct = default)
    {
        DizinHazirla(Kok);
        var gecici = Path.Combine(Kok, "." + Guid.NewGuid().ToString("N") + GeciciUzanti);
        try
        {
            var (ozet, boyut) = GeciciyeYaz(kaynak, gecici, ct);
            Yerlestir(gecici, Yol(ozet), ozet, boyut);
            return new(ozet, boyut);
        }
        finally
        {
            try
            { if (File.Exists(gecici)) File.Delete(gecici); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _logger?.LogWarning(e, "Belge deposunda geçici dosya silinemedi: {Dosya}", gecici); }
        }
    }

    public BelgeYazimi Yaz(byte[] icerik, CancellationToken ct = default)
    {
        using var akis = new MemoryStream(icerik, writable: false);
        return Yaz(akis, ct);
    }

    /// <summary>Kaynağı <paramref name="gecici"/>'ye yazar (0600), diske işler ve SHA-256 özetini hesaplar.</summary>
    internal static (string Ozet, long Boyut) GeciciyeYaz(Stream kaynak, string gecici, CancellationToken ct)
    {
        var secenek = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0 };
        if (!OperatingSystem.IsWindows())
            secenek.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var hedef = new FileStream(gecici, secenek);
        var tampon = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int okunan;
            while ((okunan = kaynak.Read(tampon, 0, tampon.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(tampon, 0, okunan);
                hedef.Write(tampon, 0, okunan);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(tampon); }
        hedef.Flush(flushToDisk: true);
        return (Convert.ToHexString(hash.GetHashAndReset()), hedef.Length);
    }

    /// <summary>Doğrulanmış geçici dosyayı hedefe atomik taşır. Hedef varsa ve doğruysa dokunulur (bakım yaşını tazeler) ve
    /// geçici dosya silinir; bozuksa doğrulanmış içerikle değiştirilir. Kilit, bakımın aynı dosyayı silmesiyle yarışmayı önler.</summary>
    internal void Yerlestir(string gecici, string hedef, string ozet, long boyut)
    {
        DizinHazirla(Path.GetDirectoryName(hedef)!);
        lock (_kilit)
        {
            if (File.Exists(hedef))
            {
                if (DosyaDogru(hedef, ozet, boyut))
                {
                    File.SetLastWriteTimeUtc(hedef, _saat.GetUtcNow().UtcDateTime);
                    return;
                }
                _logger?.LogWarning("Belge deposunda özeti tutmayan dosya doğrulanmış içerikle değiştirildi: {Ozet}", ozet);
                File.Move(gecici, hedef, overwrite: true);
                return;
            }
            try
            { File.Move(gecici, hedef); }
            // Başka bir süreç aynı içeriği aynı anda yerleştirdiyse onunki doğrulanır.
            catch (IOException) when (File.Exists(hedef) && DosyaDogru(hedef, ozet, boyut)) { }
        }
    }

    /// <summary>Özetin dosyasını okuma için açar; yoksa <see cref="BelgeDosyasiYokException"/>. Özet büyük/küçük harf
    /// duyarsızdır; geçersiz özet de 'yok' sayılır.</summary>
    public Stream Ac(string ozet)
    {
        var buyuk = ozet.ToUpperInvariant();
        if (!GecerliOzet(buyuk))
            throw new BelgeDosyasiYokException(ozet);
        try
        { return new FileStream(Yol(buyuk), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.SequentialScan); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw new BelgeDosyasiYokException(ozet); }
    }

    public bool Var(string ozet)
    {
        var buyuk = ozet.ToUpperInvariant();
        return GecerliOzet(buyuk) && File.Exists(Yol(buyuk));
    }

    /// <summary>Dosyanın boyutu; yoksa null.</summary>
    public long? Boyut(string ozet)
    {
        var buyuk = ozet.ToUpperInvariant();
        if (!GecerliOzet(buyuk))
            return null;
        var bilgi = new FileInfo(Yol(buyuk));
        return bilgi.Exists ? bilgi.Length : null;
    }

    /// <summary>Dosya var ve içeriği özetiyle eşleşiyor mu (tamamı okunur).</summary>
    public bool Dogrula(string ozet)
    {
        var buyuk = ozet.ToUpperInvariant();
        return GecerliOzet(buyuk) && DosyaDogru(Yol(buyuk), buyuk, null);
    }

    /// <summary>Dosyanın SHA-256'sı <paramref name="ozet"/> (ve verilmişse boyutu <paramref name="boyut"/>) mi?</summary>
    internal static bool DosyaDogru(string yol, string ozet, long? boyut)
    {
        try
        {
            using var akis = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.SequentialScan);
            if (boyut is { } b && akis.Length != b)
                return false;
            return string.Equals(Convert.ToHexString(SHA256.HashData(akis)), ozet, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    /// <summary>Depodaki geçerli adlı dosyaların özetleri.</summary>
    public IEnumerable<string> Ozetler() => Ozetler(Kok);

    /// <summary>Verilen kök altında (depo ya da yedek aynası) geçerli adlı dosyaların özetleri.</summary>
    public static IEnumerable<string> Ozetler(string kok)
    {
        if (!Directory.Exists(kok))
            yield break;
        foreach (var alt in Directory.EnumerateDirectories(kok))
        {
            var ad = Path.GetFileName(alt);
            if (ad.Length != 2)
                continue;
            foreach (var dosya in Directory.EnumerateFiles(alt))
            {
                var ozet = Path.GetFileName(dosya);
                if (GecerliOzet(ozet) && ozet.StartsWith(ad, StringComparison.Ordinal))
                    yield return ozet;
            }
        }
    }

    /// <summary>
    /// Bakım: hiçbir kaydın göstermediği (<paramref name="referanslar"/> dışında kalan) ve son yazımı <paramref name="enAzYas"/>'tan
    /// eski dosyaları ve yarım kalmış geçici dosyaları siler. Yaş sınırı, dosyası yazılmış ama satırı henüz kaydedilmemiş
    /// yüklemeyi korur; aynı içeriğin yeniden yazımı dosyanın yaşını tazeler. Silinen dosya sayısını döner; silinemeyen dosya
    /// loglanır ve sonraki bakımda yeniden denenir.
    /// </summary>
    public int Temizle(IReadOnlySet<string> referanslar, TimeSpan enAzYas)
    {
        if (!Directory.Exists(Kok))
            return 0;
        var sinir = _saat.GetUtcNow().UtcDateTime - enAzYas;
        var silinen = 0;
        foreach (var gecici in Directory.EnumerateFiles(Kok, "*" + GeciciUzanti))
            if (File.GetLastWriteTimeUtc(gecici) < sinir && Sil(gecici))
                silinen++;
        foreach (var ozet in Ozetler().ToList())
        {
            if (referanslar.Contains(ozet))
                continue;
            var yol = Yol(ozet);
            lock (_kilit)
            {
                if (!File.Exists(yol) || File.GetLastWriteTimeUtc(yol) >= sinir)
                    continue;
                if (Sil(yol))
                {
                    silinen++;
                    _logger?.LogInformation("Hiçbir kaydın göstermediği belge dosyası silindi: {Ozet}", ozet);
                }
            }
        }
        return silinen;
    }

    private bool Sil(string yol)
    {
        try
        { File.Delete(yol); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(e, "Belge deposunda dosya silinemedi: {Dosya}", yol);
            return false;
        }
    }

    /// <summary>Dizini açar; Unix'te yalnız sahibine (0700).</summary>
    internal static void DizinHazirla(string dizin)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(dizin);
        else
            Directory.CreateDirectory(dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
