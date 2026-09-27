using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using SQLitePCL;
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

    /// <summary>Göç öncesi yedeklerin ad ön eki. Bu ad <see cref="Tani"/> kalıbına bilerek uymaz: rotasyon göç öncesi yedeğe hiç
    /// dokunmaz (silinmez) ve günlük/elle yedek sayılarına girmez. Gereksiz olanları operatör elle kaldırır.</summary>
    public const string GocOncesiOnEki = "kasa-goc-oncesi-";

    /// <summary>'kasa-goc-oncesi-yyyyMMdd-HHmmss-xxxxxxxx.zip' (zaman UTC; ek: kopyanın SHA-256 özetinin ilk 8 hanesi).</summary>
    public static string GocOncesiDosyaAdi(DateTimeOffset zaman, string ek)
        => $"{GocOncesiOnEki}{zaman.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{ek}.zip";

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
            try { TekDosyaKopyala(source, temporary, ct); }
            finally { if (close) source.Close(); }
            // Yedek, özgün bağlantıdan bağımsız açılıp bütünlük ve ilişkiler sınanır.
            Dogrula(temporary);
            byte[] checksum;
            using (var stream = File.OpenRead(temporary)) checksum = await SHA256.HashDataAsync(stream, ct);
            Arsivle(temporary, zipTemporary, now, tur == YedekTuru.Otomatik ? "otomatik" : "elle", checksum, null);
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
            if (temporary is not null)
                foreach (var file in new[] { temporary, temporary + "-wal", temporary + "-shm", temporary + "-journal" })
                    if (File.Exists(file)) File.Delete(file);
            if (zipTemporary is not null && File.Exists(zipTemporary)) File.Delete(zipTemporary);
            kilit.Release();
        }
    }

    /// <summary>
    /// Göç öncesi yedek (kullanıcı kararı: veri dönüştüren her migration'dan önce otomatik, tutarlı yedek). Başlatıcı
    /// (<see cref="KasaDatabaseInitializer"/>) dosya tabanlı, boş olmayan veritabanında bekleyen migration ya da veri adımı
    /// varsa Migrate'ten ÖNCE, HTTP sunucusu açılmadan çağırır. Kopya olağan yedekle aynı yoldan alınır (adımlı yedekleme
    /// API'si, tek dosya) ve olağan yedekle aynı ZIP biçimindedir (kasa.db + manifest.json, varsa bildirim anahtarı;
    /// restore_backup.py açar); manifest türü "goc-oncesi"dir ve bekleyen işleri listeler. Kopya salt okunur açılıp
    /// <c>PRAGMA integrity_check</c> = ok doğrulanır; göç öncesi veritabanı henüz migration geçmişi taşımayabileceği için
    /// (eski EnsureCreated şeması) şema/ilişki denetimi migration'a bırakılır. Ad rotasyon kalıbına uymaz: silinmez.
    /// Aynı kaynak için tekrar yedek alınmaz: dizinde aynı SHA-256 özetli göç öncesi yedek varsa (ör. migration hatasıyla
    /// yeniden başlayan konteyner) yeni dosya yazılmaz, mevcut yol döner. Her hata çağırana yükselir; çağıran migration'ı
    /// çalıştırmaz.
    /// </summary>
    public string GocOncesiYedekAl(SqliteConnection kaynak, IReadOnlyList<string> bekleyenIsler)
    {
        kilit.Wait();
        string? temporary = null;
        string? zipTemporary = null;
        try
        {
            Directory.CreateDirectory(Dizin);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var now = saat.GetUtcNow();
            temporary = Path.Combine(Dizin, $".{Guid.NewGuid():N}.db");
            TekDosyaKopyala(kaynak, temporary, CancellationToken.None);
            Butunluk(temporary);
            byte[] checksum;
            using (var stream = File.OpenRead(temporary)) checksum = SHA256.HashData(stream);
            var ozet = Convert.ToHexString(checksum);
            if (MevcutGocOncesiYedegi(ozet) is { } mevcut)
            {
                logger.LogInformation("Aynı veritabanının göç öncesi yedeği zaten var, yeniden alınmadı: {Dosya}", Path.GetFileName(mevcut));
                return mevcut;
            }
            var path = Path.Combine(Dizin, YedekSaklama.GocOncesiDosyaAdi(now, ozet[..8].ToLowerInvariant()));
            zipTemporary = path + ".part";
            Arsivle(temporary, zipTemporary, now, "goc-oncesi", checksum, bekleyenIsler);
            File.Move(zipTemporary, path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return path;
        }
        finally
        {
            if (temporary is not null)
                foreach (var file in new[] { temporary, temporary + "-wal", temporary + "-shm", temporary + "-journal" })
                    if (File.Exists(file)) File.Delete(file);
            if (zipTemporary is not null && File.Exists(zipTemporary)) File.Delete(zipTemporary);
            kilit.Release();
        }
    }

    private string? MevcutGocOncesiYedegi(string sha256)
    {
        foreach (var yol in Directory.EnumerateFiles(Dizin, YedekSaklama.GocOncesiOnEki + "*.zip"))
        {
            try
            {
                using var arsiv = ZipFile.OpenRead(yol);
                using var manifest = arsiv.GetEntry("manifest.json")?.Open();
                if (manifest is null) continue;
                using var belge = JsonDocument.Parse(manifest);
                if (belge.RootElement.TryGetProperty("sha256", out var deger) && string.Equals(deger.GetString(), sha256, StringComparison.OrdinalIgnoreCase)) return yol;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Göç öncesi yedek okunamadı, karşılaştırmaya katılmadı: {Dosya}", Path.GetFileName(yol));
            }
        }
        return null;
    }

    /// <summary>Kaynağı <paramref name="hedefYol"/>'a tek dosya olarak kopyalar. Yedekleme API'si WAL'daki işlenmiş sayfaları da
    /// tutarlı anlık görüntüyle kopyalar; ancak kaynağın WAL başlığını da kopyalar. Yedek tek dosya olmalı (ZIP'teki kasa.db,
    /// salt okunur doğrulama ve restore aracı -wal/-shm olmadan açar): kopya geri alma günlüğü kipine çevrilir.</summary>
    private static void TekDosyaKopyala(SqliteConnection kaynak, string hedefYol, CancellationToken ct)
    {
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = hedefYol, Pooling = false }.ToString());
        target.Open();
        AdimliKopyala(kaynak, target, ct);
        using var mode = target.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode = DELETE;";
        mode.ExecuteNonQuery();
    }

    private void Arsivle(string dbYolu, string zipYolu, DateTimeOffset now, string tur, byte[] checksum, IReadOnlyList<string>? bekleyenIsler)
    {
        using var zip = ZipFile.Open(zipYolu, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(dbYolu, "kasa.db", CompressionLevel.Fastest);
        var keys = push.Get();
        string? keyHash = null;
        if (keys is not null)
        {
            var keyBytes = JsonSerializer.SerializeToUtf8Bytes(keys);
            keyHash = Convert.ToHexString(SHA256.HashData(keyBytes));
            using var keyStream = zip.CreateEntry(".kasa-push-keys.json").Open();
            keyStream.Write(keyBytes);
        }
        var manifest = zip.CreateEntry("manifest.json");
        using var stream = manifest.Open();
        // Göç öncesi yedekte bekleyen işler (migration kimlikleri, veri adımları) listelenir; restore aracının 8 KB manifest
        // sınırını aşmasın diye sayı ve uzunluk kısaltılır.
        if (bekleyenIsler is null)
            JsonSerializer.Serialize(stream, new { surum = "2.1.0", olusturuldu = now, tur,
                sha256 = Convert.ToHexString(checksum), belgelerDahil = true, bildirimAnahtariDahil = keys is not null, bildirimAnahtariSha256 = keyHash });
        else
            JsonSerializer.Serialize(stream, new { surum = "2.1.0", olusturuldu = now, tur,
                sha256 = Convert.ToHexString(checksum), belgelerDahil = true, bildirimAnahtariDahil = keys is not null, bildirimAnahtariSha256 = keyHash,
                bekleyenIsler = bekleyenIsler.Take(40).Select(i => i.Length > 120 ? i[..120] : i).ToList() });
    }

    /// <summary>Bir yedekleme adımında kopyalanan sayfa sayısı (4 KB sayfada ~1 MB).</summary>
    public const int VarsayilanSayfaGrubu = 256;
    /// <summary>Kaynak meşgulken (SQLITE_BUSY/LOCKED) bir adımın en çok kaç kez yeniden deneneceği; bekleme arası 50 ms (~10 sn).</summary>
    public const int MesgulDenemeSiniri = 200;
    private const int BastanAlmaSiniri = 3;

    /// <summary>
    /// Yedek kopyası (data-10): SQLite yedekleme API'siyle sayfa grupları halinde kopyalar. Her adım kaynağın paylaşılan
    /// kilidini yalnız o grup boyunca tutar ve adım dönmeden bırakır; geri alma günlüğü kipinde (ör. henüz WAL'a geçmemiş
    /// eski dosya) yazanlar bütün kopya boyunca beklemez, WAL'da okuma zaten yazanı bekletmez. Kaynak meşgulse
    /// (SQLITE_BUSY/LOCKED: ör. yazan commit anında) adım 50 ms aralıklarla en çok <see cref="MesgulDenemeSiniri"/> kez
    /// yeniden denenir; sınır aşılırsa hata SQLite iletisiyle yükselir. Kaynak kopya sürerken başka bir bağlantıdan
    /// yazılırsa SQLite kopyayı baştan alır (sonuç yine tutarlı anlık görüntüdür); üç kez baştan alındıysa kalan kopya tek
    /// adımda bitirilir, sürekli yazılan veritabanında kopya sonsuza dek uzamaz. <paramref name="adimSonrasi"/> her başarılı
    /// adımdan sonra (kilit bırakılmışken) çağrılır.
    /// </summary>
    public static void AdimliKopyala(SqliteConnection kaynak, SqliteConnection hedef, CancellationToken ct = default,
        int sayfaGrubu = VarsayilanSayfaGrubu, Action? adimSonrasi = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sayfaGrubu, 1);
        using var yedek = raw.sqlite3_backup_init(hedef.Handle, "main", kaynak.Handle, "main");
        if (yedek.IsInvalid) SqliteException.ThrowExceptionForRC(raw.sqlite3_errcode(hedef.Handle), hedef.Handle);
        int mesgul = 0, bastan = 0, oncekiKalan = int.MaxValue;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var rc = raw.sqlite3_backup_step(yedek, bastan >= BastanAlmaSiniri ? -1 : sayfaGrubu);
            if (rc == raw.SQLITE_DONE) return;
            if (rc is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
            {
                if (++mesgul > MesgulDenemeSiniri) SqliteException.ThrowExceptionForRC(rc, hedef.Handle);
                if (ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50))) ct.ThrowIfCancellationRequested();
                continue;
            }
            SqliteException.ThrowExceptionForRC(rc, hedef.Handle);
            mesgul = 0;
            var kalan = raw.sqlite3_backup_remaining(yedek);
            if (kalan > oncekiKalan) bastan++; // kaynak başka bağlantıdan yazıldı; SQLite kopyayı baştan aldı
            oncekiKalan = kalan;
            adimSonrasi?.Invoke();
        }
    }

    /// <summary>Göç öncesi kopyanın salt okunur bütünlük denetimi (<c>PRAGMA integrity_check</c> = ok).</summary>
    private static void Butunluk(string path)
    {
        using var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        restored.Open();
        using var check = restored.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal)) throw new InvalidDataException("Göç öncesi yedeğin bütünlüğü doğrulanamadı (integrity_check).");
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
