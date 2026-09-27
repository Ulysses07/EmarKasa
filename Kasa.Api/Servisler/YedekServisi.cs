using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>İlk dört alan eski istemcilerin okuduğu biçimdir; tür bazlı alanlar sonradan eklendi.
/// <see cref="RotasyonUyarisi"/>: son rotasyonda silinemeyen eski yedek (yedeğin kendisi başarılıdır, <see cref="Hata"/> boş kalır).</summary>
public record YedekDurumu(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata,
    DateTimeOffset? SonOtomatikYedek, int OtomatikYedekSayisi, DateTimeOffset? SonElleYedek, int ElleYedekSayisi, string? RotasyonUyarisi = null);

/// <summary>Saklama süresi dolan yedeği siler. Kayıt yoksa <see cref="File.Delete"/>; testler hata yolunu işletim
/// sisteminin dosya kilidine bağlı kalmadan sınamak için kendi işlevini kaydeder.</summary>
public delegate void YedekDosyaSilici(string yol);

public enum YedekTuru { Otomatik, Elle }

public readonly record struct YedekDosyasi(string Ad, YedekTuru Tur, DateTimeOffset Zaman);

/// <summary>
/// Yedek dosya adları ve saklama kuralı (saf; dosya sistemine dokunmaz). Ad türü ve UTC oluşturma
/// zamanını taşır; yaş dosya adından okunur, kopyalamayla değişebilen dosya zamanından değil.
/// Yalnız servisin ürettiği kalıba uyan dosyalar yedek sayılır; operatörün koyduğu başka dosyalara dokunulmaz.
/// </summary>
public static partial class YedekSaklama
{
    /// <summary>Son 30 günün bütün otomatik yedekleri tutulur.</summary>
    public const int GunlukGun = 30;
    /// <summary>Daha eskilerden, içinde bulunulan ay dahil son 12 takvim ayının (İstanbul) ilk otomatik yedeği tutulur.</summary>
    public const int AylikAy = 12;
    /// <summary>Yedekleme uzun süre durmuş olsa da en yeni 7 otomatik yedek yaşından bağımsız korunur.</summary>
    public const int OtomatikEnAz = 7;
    /// <summary>Elle yedeklerden yalnız en yeni 10'u tutulur; elle yedek otomatik yedeği hiçbir koşulda silmez.</summary>
    public const int ElleEnFazla = 10;

    // 2.3 ve öncesi türsüz 'kasa-yyyyMMdd-HHmmss-xxxxxxxx.zip' yazıyordu; bu adlar otomatik sayılır ve yaş kuralıyla döner.
    [GeneratedRegex(@"^kasa-(?:(oto|elle)-)?([0-9]{8}-[0-9]{6})-[0-9a-f]{8}\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex AdKalibi();

    public static string DosyaAdi(YedekTuru tur, DateTimeOffset zaman, string ek)
        => $"kasa-{(tur == YedekTuru.Otomatik ? "oto" : "elle")}-{zaman.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{ek}.zip";

    public static YedekDosyasi? Tani(string ad)
    {
        var m = AdKalibi().Match(ad);
        if (!m.Success || !DateTime.TryParseExact(m.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var zaman)) return null;
        return new(ad, m.Groups[1].Value == "elle" ? YedekTuru.Elle : YedekTuru.Otomatik, new DateTimeOffset(zaman, TimeSpan.Zero));
    }

    /// <summary>
    /// Verilen türün silinecek yedeklerini döner; başka türe ve kalıba uymayan dosyaya hiç bakmaz.
    /// <paramref name="koru"/> az önce yazılan yedektir: ad saniye duyarlı olduğundan aynı saniyedeki
    /// yedekler arasında en yeni sayılır ve hiçbir koşulda silinmez.
    /// </summary>
    public static IReadOnlyList<string> Silinecekler(IEnumerable<string> dosyaAdlari, YedekTuru tur, DateTimeOffset simdi, string? koru = null)
    {
        var adaylar = dosyaAdlari.Select(Tani).OfType<YedekDosyasi>().Where(y => y.Tur == tur)
            .OrderByDescending(y => y.Ad == koru).ThenByDescending(y => y.Zaman).ThenByDescending(y => y.Ad, StringComparer.Ordinal).ToList();
        if (tur == YedekTuru.Elle) return adaylar.Skip(ElleEnFazla).Select(y => y.Ad).ToList();
        var ilkAy = AyNumarasi(simdi) - (AylikAy - 1);
        var tut = adaylar.Take(OtomatikEnAz).Select(y => y.Ad).ToHashSet(StringComparer.Ordinal);
        tut.UnionWith(adaylar.Where(y => simdi - y.Zaman < TimeSpan.FromDays(GunlukGun)).Select(y => y.Ad));
        tut.UnionWith(adaylar.Where(y => AyNumarasi(y.Zaman) >= ilkAy).GroupBy(y => AyNumarasi(y.Zaman))
            .Select(ay => ay.OrderBy(y => y.Zaman).ThenBy(y => y.Ad, StringComparer.Ordinal).First().Ad));
        return adaylar.Where(y => !tut.Contains(y.Ad)).Select(y => y.Ad).ToList();
    }

    private static int AyNumarasi(DateTimeOffset zaman)
    {
        var yerel = BildirimTakvimi.Yerel(zaman);
        return yerel.Year * 12 + yerel.Month - 1;
    }
}

public sealed class YedekServisi(IConfiguration cfg, IWebHostEnvironment env, PushKimligi push, ILogger<YedekServisi> logger,
    TimeProvider saat, YedekDosyaSilici? silici = null)
{
    private readonly SemaphoreSlim kilit = new(1, 1);
    private readonly YedekDosyaSilici sil = silici ?? File.Delete;
    // Tür başına son rotasyonun uyarısı: bir türün başarılı rotasyonu diğer türün sorununu gizlemez.
    private readonly ConcurrentDictionary<YedekTuru, string> rotasyonUyarilari = new();
    private DateTimeOffset? sonYedek;
    private DateTimeOffset? sonOtomatikYedek;
    private DateTimeOffset? sonDogrulama;
    private string? hata;
    public bool Etkin => cfg.GetValue("Yedek:Etkin", !env.IsDevelopment());
    public string Dizin => Path.GetFullPath(cfg["Yedek:Dizin"] ?? Path.Combine(env.ContentRootPath, "yedekler"));

    public YedekDurumu Durum()
    {
        var yedekler = Yedekler();
        var otomatik = yedekler.Where(y => y.Tur == YedekTuru.Otomatik).ToList();
        var elle = yedekler.Where(y => y.Tur == YedekTuru.Elle).ToList();
        var uyarilar = rotasyonUyarilari.OrderBy(u => u.Key).Select(u => u.Value).ToList();
        return new(Etkin, sonYedek ?? EnYeni(yedekler), sonDogrulama, hata,
            sonOtomatikYedek ?? EnYeni(otomatik), otomatik.Count, EnYeni(elle), elle.Count,
            uyarilar.Count == 0 ? null : string.Join(" ", uyarilar));
    }

    /// <summary>Günlük zamanlama yalnız otomatik (ve eski adlı) yedeklere bakar; elle yedek onu ertelemez.</summary>
    public DateTimeOffset? SonOtomatikYedek() => sonOtomatikYedek ?? EnYeni(Yedekler().Where(y => y.Tur == YedekTuru.Otomatik));

    private static DateTimeOffset? EnYeni(IEnumerable<YedekDosyasi> yedekler) => yedekler.Select(y => (DateTimeOffset?)y.Zaman).Max();

    private List<YedekDosyasi> Yedekler()
    {
        try
        {
            return Directory.Exists(Dizin)
                ? Directory.EnumerateFiles(Dizin, "kasa-*.zip").Select(y => YedekSaklama.Tani(Path.GetFileName(y))).OfType<YedekDosyasi>().ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Yedek dizini okunamadı.");
            return [];
        }
    }

    /// <summary>
    /// Yalnız verilen türün yedeklerini saklama kuralına göre siler. Hata yedeği başarısız saymaz:
    /// loglanır, o türün rotasyon uyarısı olarak durumda görünür ve silinemeyen dosya sonraki yedekte
    /// yeniden denenir. Türün rotasyonu hatasız biterse uyarısı kalkar.
    /// </summary>
    public void Dondur(YedekTuru tur, DateTimeOffset simdi, string? koru = null)
    {
        var turAdi = tur == YedekTuru.Otomatik ? "Otomatik yedeklerin" : "Elle alınan yedeklerin";
        IReadOnlyList<string> silinecekler;
        try { silinecekler = YedekSaklama.Silinecekler(Directory.EnumerateFiles(Dizin, "kasa-*.zip").Select(y => Path.GetFileName(y)), tur, simdi, koru); }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Tur} yedek rotasyonu için yedek dizini okunamadı.", tur);
            rotasyonUyarilari[tur] = $"{turAdi} rotasyonu için yedek dizini okunamadı; eski yedekler silinmiyor. Sunucu kayıtlarını ve yedek dizininin izinlerini kontrol edin.";
            return;
        }
        var silinemeyen = new List<string>();
        foreach (var ad in silinecekler)
        {
            try
            {
                sil(Path.Combine(Dizin, ad));
                logger.LogInformation("Saklama süresi dolan {Tur} yedek silindi: {Dosya}", tur, ad);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Saklama süresi dolan {Tur} yedek silinemedi: {Dosya}", tur, ad);
                silinemeyen.Add(ad);
            }
        }
        if (silinemeyen.Count == 0) rotasyonUyarilari.TryRemove(tur, out _);
        else rotasyonUyarilari[tur] = $"{turAdi} rotasyonu tamamlanamadı: saklama süresi dolan {silinemeyen.Count} yedek silinemedi ({string.Join(", ", silinemeyen.Take(3))}{(silinemeyen.Count > 3 ? ", …" : "")}). "
            + "Yedekler alınmaya devam eder; disk dolmadan sunucu kayıtlarını ve yedek dizininin izinlerini kontrol edin.";
    }

    public async Task<string> Olustur(KasaDbContext db, YedekTuru tur, CancellationToken ct)
    {
        await kilit.WaitAsync(ct);
        string? temporary = null;
        string? zipTemporary = null;
        try
        {
            Directory.CreateDirectory(Dizin);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var now = saat.GetUtcNow();
            var suffix = Guid.NewGuid().ToString("N");
            temporary = Path.Combine(Dizin, $".{suffix}.db");
            var path = Path.Combine(Dizin, YedekSaklama.DosyaAdi(tur, now, suffix[..8]));
            zipTemporary = path + ".part";
            var source = (SqliteConnection)db.Database.GetDbConnection();
            bool close = source.State != System.Data.ConnectionState.Open;
            if (close) await source.OpenAsync(ct);
            try
            {
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
                target.Open();
                source.BackupDatabase(target);
            }
            finally { if (close) source.Close(); }
            // Yedek, özgün bağlantıdan bağımsız açılıp bütünlük ve ilişkiler sınanır.
            Dogrula(temporary);
            byte[] checksum;
            using (var stream = File.OpenRead(temporary)) checksum = await SHA256.HashDataAsync(stream, ct);
            using (var zip = ZipFile.Open(zipTemporary, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temporary, "kasa.db", CompressionLevel.Fastest);
                var keys = push.Get();
                string? keyHash = null;
                if (keys is not null)
                {
                    var keyBytes = JsonSerializer.SerializeToUtf8Bytes(keys);
                    keyHash = Convert.ToHexString(SHA256.HashData(keyBytes));
                    using var keyStream = zip.CreateEntry(".kasa-push-keys.json").Open();
                    await keyStream.WriteAsync(keyBytes, ct);
                }
                var manifest = zip.CreateEntry("manifest.json");
                using var stream = manifest.Open();
                JsonSerializer.Serialize(stream, new { surum = "2.1.0", olusturuldu = now, tur = tur == YedekTuru.Otomatik ? "otomatik" : "elle",
                    sha256 = Convert.ToHexString(checksum), belgelerDahil = true, bildirimAnahtariDahil = keys is not null, bildirimAnahtariSha256 = keyHash });
            }
            File.Move(zipTemporary, path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            sonYedek = now; sonDogrulama = now; hata = null;
            if (tur == YedekTuru.Otomatik) sonOtomatikYedek = now;
            // Yalnız aynı türün, servisin ad kalıbına uyan yedekleri döner; rotasyon hatası bu yedeği bozmaz.
            Dondur(tur, now, Path.GetFileName(path));
            return path;
        }
        catch
        {
            hata = "Son yedekleme tamamlanamadı. Sunucu kayıtlarını kontrol edin.";
            throw;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            if (zipTemporary is not null && File.Exists(zipTemporary)) File.Delete(zipTemporary);
            kilit.Release();
        }
    }

    public static void Dogrula(string path)
    {
        using var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        restored.Open();
        using var check = restored.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal)) throw new InvalidDataException("Yedek bütünlüğü doğrulanamadı.");
        check.CommandText = "PRAGMA foreign_key_check;";
        using (var reader = check.ExecuteReader()) if (reader.Read()) throw new InvalidDataException("Yedekte geçersiz ilişki var.");
        check.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";
        if (Convert.ToInt32(check.ExecuteScalar()) < 1) throw new InvalidDataException("Yedek şeması bulunamadı.");
    }
}

public sealed class OtomatikYedek(IServiceScopeFactory scopes, YedekServisi yedek, TimeProvider saat, ILogger<OtomatikYedek> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!yedek.Etkin) return;
        // Başlangıç geçişi tamamlandıktan sonra ilk günlük yedeği al.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                if (yedek.SonOtomatikYedek() is not { } son || saat.GetUtcNow() - son >= TimeSpan.FromDays(1))
                {
                    using var scope = scopes.CreateScope();
                    await yedek.Olustur(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), YedekTuru.Otomatik, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Otomatik Kasa yedeği oluşturulamadı."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
