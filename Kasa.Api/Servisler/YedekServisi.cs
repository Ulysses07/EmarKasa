using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>İlk dört alan eski istemcilerin okuduğu biçimdir; tür bazlı alanlar sonradan eklendi.
/// <see cref="RotasyonUyarisi"/>: son rotasyonda silinemeyen eski yedek (yedeğin kendisi başarılıdır, <see cref="Hata"/> boş kalır).
/// Disk alanları (data-3; okunamazsa null): yedek dizininin ve belge deposunun (veri) diskindeki boş alan, servis yedeklerinin ve
/// yedek aynasının toplam boyutu, yedekten sonra kalması gereken asgari boş alan. <see cref="DiskUyarisi"/>: boş alan asgarinin
/// altında ya da toplam boyut sınırı aşıldı. <see cref="BelgeUyarisi"/>: son yedekte bulunamayan belge içerikleri.
/// <see cref="SonGeriYukleme"/>, <see cref="GeriYuklemeRaporu"/> (gap-geri-yukleme-durum-geri-sarma-1): son geri yüklemenin anı ve
/// açılışta yapılanlarla yapılması gerekenlerin Türkçe maddeleri (SistemDurumu); hiç geri yükleme olmadıysa null.</summary>
public record YedekDurumu(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata,
    DateTimeOffset? SonOtomatikYedek, int OtomatikYedekSayisi, DateTimeOffset? SonElleYedek, int ElleYedekSayisi, string? RotasyonUyarisi = null,
    long? YedekDiskiBosAlanBayt = null, long? VeriDiskiBosAlanBayt = null, long? ToplamYedekBayt = null, long? AsgariBosAlanBayt = null,
    string? DiskUyarisi = null, string? BelgeUyarisi = null, DateTimeOffset? SonGeriYukleme = null, IReadOnlyList<string>? GeriYuklemeRaporu = null);

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
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var zaman))
            return null;
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
        if (tur == YedekTuru.Elle)
            return adaylar.Skip(ElleEnFazla).Select(y => y.Ad).ToList();
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

/// <summary>Yedek alınmadı: yedek diskinde gereken boş alan yok (data-3). Elle yedek ucu 507 döner; hiçbir dosya yazılmaz.</summary>
public sealed class YedekDiskAlaniYetersizException(string ileti) : IOException(ileti);

/// <summary>Yedeğin belge listesindeki (belgeler.json) bir içerik: özeti ve bayt sayısı.</summary>
public sealed record YedekBelgesi(string Ozet, long Boyut);

/// <summary>Yedek anındaki veritabanının gösterdiği belge içerikleri: yedek aynasında (ve elle indirilende ZIP'te) bulunanlar ve
/// ne depoda ne aynada bulunamayanlar.</summary>
public sealed record YedekBelgeListesi(IReadOnlyList<YedekBelgesi> Belgeler, IReadOnlyList<string> Eksik);

public sealed class YedekServisi(IConfiguration cfg, IWebHostEnvironment env, PushKimligi push, ILogger<YedekServisi> logger,
    TimeProvider saat, YedekDosyaSilici? silici = null, BelgeDeposu? depo = null, IDiskAlani? disk = null)
{
    /// <summary>Belge deposu biçimli yedeklerin manifest sürümü: kasa.db belge içeriği taşımaz; belgeler.json içerik özetlerini listeler,
    /// içerikler yedek aynasında (<see cref="AynaDizini"/>) ya da elle indirilen yedekte belgeler/&lt;özet&gt; girdilerindedir.</summary>
    public const string DepoluSurum = "2.2.0";
    /// <summary>İçeriği veritabanında (BLOB) taşıyan eski biçim (göç öncesi yedek, belge deposundan önceki şema).</summary>
    public const string EskiSurum = "2.1.0";
    public const string BelgeListesiGirdisi = "belgeler.json";
    public const string GomuluBelgeOneki = "belgeler/";
    private const long Mb = 1024L * 1024;

    private readonly SemaphoreSlim kilit = new(1, 1);
    private readonly YedekDosyaSilici sil = silici ?? File.Delete;
    // Tür başına son rotasyonun uyarısı: bir türün başarılı rotasyonu diğer türün sorununu gizlemez.
    private readonly ConcurrentDictionary<YedekTuru, string> rotasyonUyarilari = new();
    private DateTimeOffset? sonYedek;
    private DateTimeOffset? sonOtomatikYedek;
    private DateTimeOffset? sonDogrulama;
    private string? hata;
    private string? boyutUyarisi;
    private string? belgeUyarisi;
    public bool Etkin => cfg.GetValue("Yedek:Etkin", !env.IsDevelopment());
    public string Dizin => Path.GetFullPath(cfg["Yedek:Dizin"] ?? Path.Combine(env.ContentRootPath, "yedekler"));
    /// <summary>Belge içeriklerinin yedek aynası: <c>&lt;Yedek:Dizin&gt;/belgeler/&lt;ab&gt;/&lt;özet&gt;</c> (belge deposuyla aynı düzen).
    /// Her yedekte yalnız eksik içerikler, özeti doğrulanarak kopyalanır; hiçbir tutulan yedeğin listesinde geçmeyenler rotasyonda silinir.</summary>
    public string AynaDizini => Path.Combine(Dizin, BelgeDeposu.VarsayilanKlasor);
    /// <summary>Yedekten sonra diskte kalması gereken boş alan (<c>Yedek:AsgariBosAlanMb</c>, varsayılan 2048): canlı veritabanı ve
    /// belgeler aynı diskteyse onların yazılabilmesi için ayrılır.</summary>
    public long AsgariBosAlanBayt => Math.Max(0, cfg.GetValue("Yedek:AsgariBosAlanMb", 2048L)) * Mb;
    /// <summary>İsteğe bağlı üst sınır (<c>Yedek:AzamiToplamMb</c>): aşılırsa en eski otomatik yedekler (en yeni 7 korunur) silinir.</summary>
    public long? AzamiToplamBayt => cfg.GetValue<long?>("Yedek:AzamiToplamMb") is > 0 and var mb ? mb * Mb : null;
    /// <summary>Son yedekte aynaya kopyalanan belge sayısı (izleme ve sınama).</summary>
    public int SonYansitilanBelge { get; private set; }

    public YedekDurumu Durum()
    {
        var yedekler = Yedekler();
        var otomatik = yedekler.Where(y => y.Tur == YedekTuru.Otomatik).ToList();
        var elle = yedekler.Where(y => y.Tur == YedekTuru.Elle).ToList();
        var uyarilar = rotasyonUyarilari.OrderBy(u => u.Key).Select(u => u.Value).ToList();
        var yedekBos = disk?.BosAlan(Dizin);
        var veriBos = depo is null ? null : disk?.BosAlan(depo.Kok);
        var diskUyarilari = new List<string>();
        if (yedekBos is { } yb && yb < AsgariBosAlanBayt)
            diskUyarilari.Add($"Yedek diskinde {Gb(yb)} GB boş alan kaldı (asgari {Gb(AsgariBosAlanBayt)} GB). Yeni yedekler alınamayabilir: eski yedekleri sunucu dışına taşıyın ya da diski büyütün.");
        if (veriBos is { } vb && vb < AsgariBosAlanBayt)
            diskUyarilari.Add($"Veri diskinde {Gb(vb)} GB boş alan kaldı (asgari {Gb(AsgariBosAlanBayt)} GB): kasa kayıtları ve belgeler yazılamayabilir; diski büyütün.");
        if (boyutUyarisi is not null)
            diskUyarilari.Add(boyutUyarisi);
        return new(Etkin, sonYedek ?? EnYeni(yedekler), sonDogrulama, hata,
            sonOtomatikYedek ?? EnYeni(otomatik), otomatik.Count, EnYeni(elle), elle.Count,
            uyarilar.Count == 0 ? null : string.Join(" ", uyarilar),
            yedekBos, veriBos, ToplamYedekBayt(), AsgariBosAlanBayt,
            diskUyarilari.Count == 0 ? null : string.Join(" ", diskUyarilari), belgeUyarisi);
    }

    private static string Gb(long bayt) => (bayt / (1024.0 * Mb)).ToString("0.0", CultureInfo.GetCultureInfo("tr-TR"));
    private static string MbMetni(long bayt) => (bayt / (double)Mb).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Yedek dizinindeki servis yedeklerinin (her tür, göç öncesi dahil) ve yedek aynasının toplam boyutu; okunamazsa null.</summary>
    public long? ToplamYedekBayt()
    {
        try
        {
            if (!Directory.Exists(Dizin))
                return 0;
            long toplam = Directory.EnumerateFiles(Dizin, "kasa-*.zip").Sum(y => new FileInfo(y).Length);
            if (Directory.Exists(AynaDizini))
                toplam += Directory.EnumerateFiles(AynaDizini, "*", SearchOption.AllDirectories).Sum(y => new FileInfo(y).Length);
            return toplam;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Yedeklerin toplam boyutu okunamadı.");
            return null;
        }
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
        try
        { silinecekler = YedekSaklama.Silinecekler(Directory.EnumerateFiles(Dizin, "kasa-*.zip").Select(y => Path.GetFileName(y)), tur, simdi, koru); }
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
        if (silinemeyen.Count == 0)
            rotasyonUyarilari.TryRemove(tur, out _);
        else
            rotasyonUyarilari[tur] = $"{turAdi} rotasyonu tamamlanamadı: saklama süresi dolan {silinemeyen.Count} yedek silinemedi ({string.Join(", ", silinemeyen.Take(3))}{(silinemeyen.Count > 3 ? ", …" : "")}). "
            + "Yedekler alınmaya devam eder; disk dolmadan sunucu kayıtlarını ve yedek dizininin izinlerini kontrol edin.";
    }

    /// <summary>
    /// Sunucu yedeği (günlük otomatik ya da elle). Belge deposu biçiminde (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8):
    /// <list type="number">
    /// <item>Disk denetimi: kopyadan önce yedek diskinde veritabanının geçici kopyası (bütün sayfalar) + arşivi (kullanılan sayfalar) +
    /// aynaya yansıtılacak yeni belge dosyaları + <see cref="AsgariBosAlanBayt"/> aranır. Yetmezse yedek alınmaz, hiçbir dosya yazılmaz,
    /// durumda Türkçe hata görünür ve <see cref="YedekDiskAlaniYetersizException"/> yükselir (elle yedek ucu 507).</item>
    /// <item>Veritabanı adımlı yedekleme API'siyle tek dosyaya kopyalanır ve doğrulanır (data-10).</item>
    /// <item>Kopyanın gösterdiği belge içerikleri yedek aynasına yalnız eksik olanlar, özeti doğrulanarak kopyalanır (artımlı).</item>
    /// <item>ZIP: kasa.db + manifest.json (<see cref="DepoluSurum"/>) + belgeler.json (özet listesi) [+ bildirim anahtarı]; belge
    /// içeriği ZIP'e girmez (elle indirilen kopyaya <see cref="KendiKendineYeterliYaz"/> ekler).</item>
    /// <item>Rotasyon (türe ve yaşa göre), isteğe bağlı toplam boyut sınırı, hiçbir tutulan yedeğin listesinde geçmeyen ayna dosyalarının
    /// ve belge deposunda hiçbir kaydın göstermediği 24 saatten eski dosyaların temizliği.</item>
    /// </list>
    /// Veritabanı hâlâ içerikleri BLOB olarak taşıyorsa (belge deposundan önceki şema) eski biçim (<see cref="EskiSurum"/>) yazılır.
    /// </summary>
    public async Task<string> Olustur(KasaDbContext db, YedekTuru tur, CancellationToken ct)
    {
        await kilit.WaitAsync(ct);
        string? temporary = null;
        string? zipTemporary = null;
        try
        {
            Directory.CreateDirectory(Dizin);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var now = saat.GetUtcNow();
            var suffix = Guid.NewGuid().ToString("N");
            temporary = Path.Combine(Dizin, $".{suffix}.db");
            var path = Path.Combine(Dizin, YedekSaklama.DosyaAdi(tur, now, suffix[..8]));
            zipTemporary = path + ".part";
            var source = (SqliteConnection)db.Database.GetDbConnection();
            bool close = source.State != System.Data.ConnectionState.Open;
            if (close)
                await source.OpenAsync(ct);
            try
            {
                DiskDenetimi(source);
                // Kopya, kopyalamanın başladığı anı (manifest 'olusturuldu' ile aynı) yedek anı olarak taşır: geri yüklemede güvenlik
                // günlüğünün bu andan sonraki olayları yeniden uygulanır. Başlangıç anı güvenli yöndedir: kopya sürerken kaydedilen bir
                // değişiklik yedekte olsa da yeniden uygulanır (fazladan sıkılaştırma), yedekte olmayan hiçbir değişiklik atlanmaz.
                TekDosyaKopyala(source, temporary, ct, now);
            }
            finally { if (close) source.Close(); }
            // Yedek, özgün bağlantıdan bağımsız açılıp bütünlük ve ilişkiler sınanır.
            Dogrula(temporary);
            var belgeler = BelgeListesiOku(temporary) is { } ozetler ? Yansit(ozetler, ct) : null;
            byte[] checksum;
            using (var stream = File.OpenRead(temporary))
                checksum = await SHA256.HashDataAsync(stream, ct);
            Arsivle(temporary, zipTemporary, now, tur == YedekTuru.Otomatik ? "otomatik" : "elle", checksum, null, belgeler);
            File.Move(zipTemporary, path);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            sonYedek = now;
            sonDogrulama = now;
            hata = null;
            if (tur == YedekTuru.Otomatik)
                sonOtomatikYedek = now;
            belgeUyarisi = belgeler is { Eksik.Count: > 0 } ? BelgeUyarisiMetni(belgeler.Eksik) : null;
            // Yalnız aynı türün, servisin ad kalıbına uyan yedekleri döner; rotasyon hatası bu yedeği bozmaz.
            Dondur(tur, now, Path.GetFileName(path));
            // Bakım adımlarının hatası yedeği başarısız saymaz: loglanır, sonraki yedekte yeniden denenir.
            if (tur == YedekTuru.Otomatik)
                Bakim(() => BoyutSiniri(Path.GetFileName(path)));
            Bakim(AynaTemizle);
            if (belgeler is not null)
                Bakim(() => DepoBakimi(belgeler));
            Bakim(BoyutUyarisiniGuncelle);
            return path;
        }
        catch (YedekDiskAlaniYetersizException ex)
        {
            hata = ex.Message;
            logger.LogError("{Hata}", ex.Message);
            throw;
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
                    if (File.Exists(file))
                        File.Delete(file);
            if (zipTemporary is not null && File.Exists(zipTemporary))
                File.Delete(zipTemporary);
            kilit.Release();
        }
    }

    private static string BelgeUyarisiMetni(IReadOnlyList<string> eksik) =>
        $"Son yedekte {eksik.Count} belge dosyası ne belge deposunda ne yedek aynasında bulundu ya da içeriği özetiyle eşleşmedi "
        + $"(ilk: {eksik[0][..12]}…). Yedek alındı; bu belgeler indirilemez. Sunucu kayıtlarını ve belge deposu dizinini kontrol edin.";

    /// <summary>Kopyadan önce gereken alan: geçici kopya (bütün sayfalar, serbest sayfalar dahil kopyalanır) + arşiv (üst sınır:
    /// kullanılan sayfalar) + aynaya yansıtılacak yeni belge dosyaları + asgari boş alan. Boş alan okunamazsa denetim yapılmaz.</summary>
    private void DiskDenetimi(SqliteConnection source)
    {
        if (disk is null)
            return;
        var sayfa = Tamsayi(source, "PRAGMA page_size;");
        var toplamSayfa = Tamsayi(source, "PRAGMA page_count;");
        var kullanilan = toplamSayfa - Tamsayi(source, "PRAGMA freelist_count;");
        var yeniBelgeler = YansitilacakBoyut(source);
        var gereken = toplamSayfa * sayfa + kullanilan * sayfa + yeniBelgeler + AsgariBosAlanBayt;
        if (disk.BosAlan(Dizin) is not { } bos || bos >= gereken)
            return;
        throw new YedekDiskAlaniYetersizException(
            $"Yedek alınmadı: yedek diskinde yeterli boş alan yok (gereken ~{MbMetni(gereken)} MB, boş {MbMetni(bos)} MB; bunun "
            + $"{MbMetni(AsgariBosAlanBayt)} MB'ı canlı veritabanı ve belgeler için ayrılır). Eski yedekleri sunucu dışına taşıyın ya da diski büyütün.");
    }

    private static long Tamsayi(SqliteConnection c, string sql)
    {
        using var k = c.CreateCommand();
        k.CommandText = sql;
        return Convert.ToInt64(k.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Canlı veritabanının gösterdiği ve aynada henüz bulunmayan içeriklerin toplam boyutu (depodaki dosyalardan).</summary>
    private long YansitilacakBoyut(SqliteConnection c)
    {
        if (depo is null)
            return 0;
        long toplam = 0;
        foreach (var ozet in Ozetler(c) ?? [])
            if (!File.Exists(BelgeDeposu.DosyaYolu(AynaDizini, ozet)) && depo.Boyut(ozet) is { } boyut)
                toplam += boyut;
        return toplam;
    }

    /// <summary>Veritabanındaki belge içeriği özetleri (alış belgeleri, silinmişler dahil, ve ekstre PDF'leri; tekil, sıralı).
    /// İçerikler hâlâ veritabanındaysa (BLOB sütunları duruyor) null: yedek eski biçimdir. Geçersiz biçimli özet de listelenir
    /// (bulunamayan belge olarak raporlanır).</summary>
    private static List<string>? Ozetler(SqliteConnection c)
    {
        if (!BelgeDeposuAktarimi.SutunVar(c, "Belgeler", "IcerikOzeti") || BelgeDeposuAktarimi.SutunVar(c, "Belgeler", "Icerik")
            || BelgeDeposuAktarimi.SutunVar(c, "EkstreBelgeler", "Dosya"))
            return null;
        var sql = "SELECT \"IcerikOzeti\" FROM \"Belgeler\" WHERE \"IcerikOzeti\" IS NOT NULL";
        if (BelgeDeposuAktarimi.SutunVar(c, "EkstreBelgeler", "DosyaOzeti"))
            sql += " UNION SELECT upper(\"DosyaOzeti\") FROM \"EkstreBelgeler\"";
        var liste = new SortedSet<string>(StringComparer.Ordinal);
        using var k = c.CreateCommand();
        k.CommandText = sql + ";";
        using var r = k.ExecuteReader();
        while (r.Read())
            liste.Add(r.GetString(0));
        return [.. liste];
    }

    private static List<string>? BelgeListesiOku(string dbYolu)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbYolu, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        return Ozetler(c);
    }

    /// <summary>
    /// Özetleri yedek aynasına yansıtır: aynada doğru boyutta bulunan dosya yeniden kopyalanmaz; eksik olan depodan geçici dosyaya
    /// kopyalanıp özeti doğrulanarak (diske işlenmiş) yerine taşınır. Depoda da aynada da bulunamayan ya da depodaki içeriği özetiyle
    /// eşleşmeyen belge 'eksik' listelenir (yedek yine alınır). Kopyalanan sayısı <see cref="SonYansitilanBelge"/>'dedir.
    /// </summary>
    private YedekBelgeListesi Yansit(IReadOnlyList<string> ozetler, CancellationToken ct)
    {
        var bulunan = new List<YedekBelgesi>();
        var eksik = new List<string>();
        var kopyalanan = 0;
        foreach (var ozet in ozetler)
        {
            ct.ThrowIfCancellationRequested();
            if (!BelgeDeposu.GecerliOzet(ozet))
            { eksik.Add(ozet); continue; }
            var ayna = BelgeDeposu.DosyaYolu(AynaDizini, ozet);
            var aynadaki = new FileInfo(ayna);
            var depodaki = depo?.Boyut(ozet);
            if (aynadaki.Exists && (depodaki is null || aynadaki.Length == depodaki))
            {
                bulunan.Add(new(ozet, aynadaki.Length));
                continue;
            }
            if (depo is null || depodaki is null)
            {
                logger.LogError("Belge içeriği ne belge deposunda ne yedek aynasında var: {Ozet}", ozet);
                eksik.Add(ozet);
                continue;
            }
            BelgeDeposu.DizinHazirla(AynaDizini);
            var gecici = Path.Combine(AynaDizini, "." + Guid.NewGuid().ToString("N") + ".yaziliyor");
            try
            {
                (string Ozet, long Boyut) yazilan;
                using (var kaynak = depo.Ac(ozet))
                    yazilan = BelgeDeposu.GeciciyeYaz(kaynak, gecici, ct);
                if (yazilan.Ozet != ozet)
                {
                    logger.LogError("Belge deposundaki dosyanın içeriği özetiyle eşleşmiyor, aynaya kopyalanmadı: {Ozet}", ozet);
                    eksik.Add(ozet);
                    continue;
                }
                BelgeDeposu.DizinHazirla(Path.GetDirectoryName(ayna)!);
                File.Move(gecici, ayna, overwrite: true);
                kopyalanan++;
                bulunan.Add(new(ozet, yazilan.Boyut));
            }
            catch (BelgeDosyasiYokException)
            {
                logger.LogError("Belge içeriği ne belge deposunda ne yedek aynasında var: {Ozet}", ozet);
                eksik.Add(ozet);
            }
            finally { if (File.Exists(gecici)) File.Delete(gecici); }
        }
        SonYansitilanBelge = kopyalanan;
        if (kopyalanan > 0)
            logger.LogInformation("Yedek aynasına {Sayi} yeni belge kopyalandı.", kopyalanan);
        return new(bulunan, eksik);
    }

    /// <summary><c>Yedek:AzamiToplamMb</c> verilmişse ve aşıldıysa en eski otomatik yedekleri siler; en yeni 7 otomatik yedek, az önce
    /// yazılan, elle ve göç öncesi yedekler korunur.</summary>
    private void BoyutSiniri(string koru)
    {
        if (AzamiToplamBayt is not { } azami || ToplamYedekBayt() is not { } toplam || toplam <= azami)
            return;
        var eskidenYeniye = Yedekler().Where(y => y.Tur == YedekTuru.Otomatik)
            .OrderByDescending(y => y.Ad == koru).ThenByDescending(y => y.Zaman).ThenByDescending(y => y.Ad, StringComparer.Ordinal)
            .Skip(YedekSaklama.OtomatikEnAz).Reverse().ToList();
        foreach (var y in eskidenYeniye)
        {
            if (toplam <= azami)
                break;
            var yol = Path.Combine(Dizin, y.Ad);
            try
            {
                var boyut = new FileInfo(yol).Length;
                sil(yol);
                toplam -= boyut;
                logger.LogWarning("Yedeklerin toplam boyutu Yedek:AzamiToplamMb sınırını aştığı için en eski otomatik yedek silindi: {Dosya}", y.Ad);
            }
            catch (Exception ex) { logger.LogError(ex, "Boyut sınırı için otomatik yedek silinemedi: {Dosya}", y.Ad); }
        }
    }

    private void BoyutUyarisiniGuncelle()
    {
        boyutUyarisi = AzamiToplamBayt is { } azami && ToplamYedekBayt() is { } toplam && toplam > azami
            ? $"Yedeklerin toplam boyutu ({Gb(toplam)} GB) Yedek:AzamiToplamMb sınırını ({Gb(azami)} GB) aşıyor; en yeni {YedekSaklama.OtomatikEnAz} otomatik, "
              + "elle ve göç öncesi yedekler korunduğundan daha fazlası silinmedi. Eski yedekleri sunucu dışına taşıyın ya da sınırı gözden geçirin."
            : null;
    }

    /// <summary>
    /// Yedek aynasının temizliği: dizindeki servis yedeklerinden (her tür, göç öncesi dahil) hiçbirinin belge listesinde geçmeyen
    /// ayna dosyaları ve yarım kalmış geçici dosyalar silinir. Bir yedeğin listesi okunamazsa hiçbir ayna dosyası silinmez.
    /// </summary>
    private void AynaTemizle()
    {
        if (!Directory.Exists(AynaDizini))
            return;
        var tutulan = new HashSet<string>(StringComparer.Ordinal);
        foreach (var yol in Directory.EnumerateFiles(Dizin, "kasa-*.zip"))
        {
            try
            {
                using var arsiv = ZipFile.OpenRead(yol);
                if (arsiv.GetEntry(BelgeListesiGirdisi) is not { } girdi)
                    continue;
                using var akis = girdi.Open();
                foreach (var b in BelgeListesiniOku(akis).Belgeler)
                    tutulan.Add(b.Ozet);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Yedeğin belge listesi okunamadı; yedek aynası bu kez temizlenmedi: {Dosya}", Path.GetFileName(yol));
                return;
            }
        }
        var sinir = saat.GetUtcNow().UtcDateTime.AddHours(-1);
        foreach (var gecici in Directory.EnumerateFiles(AynaDizini, "*.yaziliyor"))
            if (File.GetLastWriteTimeUtc(gecici) < sinir)
                Sil(gecici);
        foreach (var ozet in BelgeDeposu.Ozetler(AynaDizini).ToList())
            if (!tutulan.Contains(ozet) && Sil(BelgeDeposu.DosyaYolu(AynaDizini, ozet)))
                logger.LogInformation("Hiçbir yedeğin göstermediği ayna dosyası silindi: {Ozet}", ozet);
    }

    /// <summary>Belge deposunun bakımı: yedeklenen veritabanının hiçbir satırının göstermediği ve 24 saatten eski dosyalar (yazılıp satırı
    /// kaydedilemeyen yüklemeler) silinir. Olağandışı çok dosya (depodakilerin yarısından fazlası) sahipsiz görünürse — ör. eski bir
    /// yedek geri yüklendiyse — hiçbiri silinmez, uyarı yazılır.</summary>
    private void DepoBakimi(YedekBelgeListesi belgeler)
    {
        if (depo is null)
            return;
        try
        {
            var referanslar = belgeler.Belgeler.Select(b => b.Ozet).Concat(belgeler.Eksik).ToHashSet(StringComparer.Ordinal);
            var depodakiler = depo.Ozetler().ToList();
            var sahipsiz = depodakiler.Count(o => !referanslar.Contains(o));
            if (sahipsiz > 10 && sahipsiz * 2 > depodakiler.Count)
            {
                logger.LogWarning("Belge deposunda {Sahipsiz}/{Toplam} dosyayı hiçbir kayıt göstermiyor (eski yedek geri yüklenmiş olabilir); bakım bu kez silme yapmadı.", sahipsiz, depodakiler.Count);
                return;
            }
            depo.Temizle(referanslar, TimeSpan.FromHours(24));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Belge deposu bakımı tamamlanamadı; sonraki yedekte yeniden denenir.");
        }
    }

    private void Bakim(Action adim)
    {
        try
        { adim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            logger.LogWarning(ex, "Yedekten sonraki bakım adımı tamamlanamadı; sonraki yedekte yeniden denenir.");
        }
    }

    private bool Sil(string yol)
    {
        try
        { sil(yol); return true; }
        catch (Exception ex) { logger.LogWarning(ex, "Dosya silinemedi: {Dosya}", yol); return false; }
    }

    /// <summary>belgeler.json: <c>{"belgeler":[{"ozet","boyut"}],"eksik":[özet]}</c>.</summary>
    public static YedekBelgeListesi BelgeListesiniOku(Stream akis)
    {
        using var belge = JsonDocument.Parse(akis);
        var belgeler = belge.RootElement.GetProperty("belgeler").EnumerateArray()
            .Select(b => new YedekBelgesi(b.GetProperty("ozet").GetString()!, b.GetProperty("boyut").GetInt64())).ToList();
        var eksik = belge.RootElement.TryGetProperty("eksik", out var e) ? e.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        return new(belgeler, eksik);
    }

    private static byte[] BelgeListesiJson(YedekBelgeListesi liste) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        belgeler = liste.Belgeler.Select(b => new { ozet = b.Ozet, boyut = b.Boyut }),
        eksik = liste.Eksik,
    });

    /// <summary>
    /// Elle indirilen yedeğin kendi kendine yeterli biçimi (data-3): sunucudaki <paramref name="zipYolu"/> yedeğinin kasa.db, bildirim
    /// anahtarı ve belge listesi girdilerine, listedeki her içerik <c>belgeler/&lt;özet&gt;</c> girdisi olarak (sıkıştırmasız) eklenir;
    /// manifest <c>belgelerGomulu</c> alanını taşır. ZIP diske yazılmadan <paramref name="hedef"/>'e akıtılır: içerikler yedek aynasından
    /// (yoksa belge deposundan) okunur ve yazılırken özeti doğrulanır; tutmayan içerik akışı hatayla keser (yarım ZIP restore aracında
    /// reddedilir). Eski biçimli (belge listesiz) yedek olduğu gibi akıtılır.
    /// </summary>
    public async Task KendiKendineYeterliYaz(string zipYolu, Stream hedef, CancellationToken ct)
    {
        var boru = new System.IO.Pipelines.Pipe(new System.IO.Pipelines.PipeOptions(pauseWriterThreshold: 1 << 20, resumeWriterThreshold: 1 << 19));
        // ZipArchive eşzamanlı yazar; yanıt gövdesi eşzamanlı G/Ç kabul etmez. Arşiv ayrı iş parçacığında boruya yazılır, boru gövdeye
        // eşzamansız aktarılır (ters basınçla: istemci yavaşsa üretici bekler, bellek büyümez).
        var uretici = Task.Factory.StartNew(() =>
        {
            try
            {
                using (var akis = boru.Writer.AsStream(leaveOpen: true))
                    KendiKendineYeterliArsiv(zipYolu, akis, ct);
                boru.Writer.Complete();
            }
            catch (Exception ex) { boru.Writer.Complete(ex); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await boru.Reader.CopyToAsync(hedef, ct);
            await boru.Reader.CompleteAsync();
        }
        catch (Exception ex)
        {
            await boru.Reader.CompleteAsync(ex);
            throw;
        }
        finally { await uretici; }
    }

    private void KendiKendineYeterliArsiv(string zipYolu, Stream hedef, CancellationToken ct)
    {
        using var kaynak = ZipFile.OpenRead(zipYolu);
        using var zip = new ZipArchive(hedef, ZipArchiveMode.Create, leaveOpen: true);
        var liste = kaynak.GetEntry(BelgeListesiGirdisi);
        foreach (var girdi in kaynak.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var yeni = zip.CreateEntry(girdi.FullName, girdi.FullName == "kasa.db" ? CompressionLevel.Fastest : CompressionLevel.Optimal);
            using var yaz = yeni.Open();
            using var oku = girdi.Open();
            if (girdi.FullName == "manifest.json" && liste is not null)
            {
                var manifest = System.Text.Json.Nodes.JsonNode.Parse(oku)!.AsObject();
                manifest["belgelerGomulu"] = true;
                JsonSerializer.Serialize(yaz, manifest);
            }
            else
                oku.CopyTo(yaz);
        }
        if (liste is null)
            return;
        YedekBelgeListesi belgeler;
        using (var akis = liste.Open())
            belgeler = BelgeListesiniOku(akis);
        var tampon = new byte[81920];
        foreach (var b in belgeler.Belgeler)
        {
            ct.ThrowIfCancellationRequested();
            var ayna = BelgeDeposu.DosyaYolu(AynaDizini, b.Ozet);
            using var oku = File.Exists(ayna) ? File.OpenRead(ayna) : depo?.Ac(b.Ozet) ?? throw new BelgeDosyasiYokException(b.Ozet);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var yaz = zip.CreateEntry(GomuluBelgeOneki + b.Ozet, CompressionLevel.NoCompression).Open())
            {
                int okunan;
                while ((okunan = oku.Read(tampon, 0, tampon.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    hash.AppendData(tampon, 0, okunan);
                    yaz.Write(tampon, 0, okunan);
                }
            }
            if (Convert.ToHexString(hash.GetHashAndReset()) != b.Ozet)
                throw new InvalidDataException($"Yedeğe eklenen belge içeriği özetiyle eşleşmiyor: {b.Ozet}. Elle yedek kesildi; sunucu kayıtlarını kontrol edin.");
        }
    }

    /// <summary>
    /// Göç öncesi yedek (kullanıcı kararı: veri dönüştüren her migration'dan önce otomatik, tutarlı yedek). Başlatıcı
    /// (<see cref="KasaDatabaseInitializer"/>) dosya tabanlı, boş olmayan veritabanında bekleyen migration ya da veri adımı
    /// varsa Migrate'ten ÖNCE, HTTP sunucusu açılmadan çağırır. Kopya olağan yedekle aynı yoldan alınır (adımlı yedekleme
    /// API'si, tek dosya) ve olağan yedekle aynı ZIP biçimindedir (kasa.db + manifest.json, varsa bildirim anahtarı;
    /// restore_backup.py açar); manifest türü "goc-oncesi"dir ve bekleyen işleri listeler. Veritabanı belge deposu biçimindeyse
    /// belge listesi de yazılır ve içerikler yedek aynasına yansıtılır; içerikler hâlâ veritabanındaysa (belge deposu geçişinden
    /// önce) yedek onları kasa.db içinde taşır. Kopya salt okunur açılıp
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
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var now = saat.GetUtcNow();
            temporary = Path.Combine(Dizin, $".{Guid.NewGuid():N}.db");
            TekDosyaKopyala(kaynak, temporary, CancellationToken.None);
            Butunluk(temporary);
            byte[] checksum;
            using (var stream = File.OpenRead(temporary))
                checksum = SHA256.HashData(stream);
            var ozet = Convert.ToHexString(checksum);
            if (MevcutGocOncesiYedegi(ozet) is { } mevcut)
            {
                logger.LogInformation("Aynı veritabanının göç öncesi yedeği zaten var, yeniden alınmadı: {Dosya}", Path.GetFileName(mevcut));
                return mevcut;
            }
            var belgeler = BelgeListesiOku(temporary) is { } ozetler ? Yansit(ozetler, CancellationToken.None) : null;
            var path = Path.Combine(Dizin, YedekSaklama.GocOncesiDosyaAdi(now, ozet[..8].ToLowerInvariant()));
            zipTemporary = path + ".part";
            Arsivle(temporary, zipTemporary, now, "goc-oncesi", checksum, bekleyenIsler, belgeler);
            File.Move(zipTemporary, path);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return path;
        }
        finally
        {
            if (temporary is not null)
                foreach (var file in new[] { temporary, temporary + "-wal", temporary + "-shm", temporary + "-journal" })
                    if (File.Exists(file))
                        File.Delete(file);
            if (zipTemporary is not null && File.Exists(zipTemporary))
                File.Delete(zipTemporary);
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
                if (manifest is null)
                    continue;
                using var belge = JsonDocument.Parse(manifest);
                if (belge.RootElement.TryGetProperty("sha256", out var deger) && string.Equals(deger.GetString(), sha256, StringComparison.OrdinalIgnoreCase))
                    return yol;
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
    /// salt okunur doğrulama ve restore aracı -wal/-shm olmadan açar): kopya geri alma günlüğü kipine çevrilir.
    /// Kopya geri yükleme işaretini taşır (<c>PRAGMA user_version</c> = <see cref="GeriYuklemeIsleyici.Isaret"/>; canlı dosyada 0):
    /// bu dosyayla açılan uygulama geri yüklemeyi tanır, oturumları ve izleyici girişini kapatıp kimlikleri ileri alır. İşaret
    /// sabittir, aynı kaynağın kopyaları yine aynı özeti verir (göç öncesi yedeğin tekrar denetimi); özet işaretten sonra alınır.
    /// <paramref name="yedekZamani"/> verilirse kopyanın SistemDurumu satırına yedek anı yazılır (canlı dosyaya değil); göç öncesi
    /// yedek vermez (aynı kaynağın kopyası aynı özeti vermeli), onun anını restore_backup.py manifestten yazar.</summary>
    private static void TekDosyaKopyala(SqliteConnection kaynak, string hedefYol, CancellationToken ct, DateTimeOffset? yedekZamani = null)
    {
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = hedefYol, Pooling = false }.ToString());
        target.Open();
        AdimliKopyala(kaynak, target, ct);
        using var mode = target.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode = DELETE;";
        mode.ExecuteNonQuery();
        mode.CommandText = $"PRAGMA user_version = {GeriYuklemeIsleyici.Isaret.ToString(CultureInfo.InvariantCulture)};";
        mode.ExecuteNonQuery();
        if (yedekZamani is not { } an)
            return;
        mode.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SistemDurumu';";
        if (mode.ExecuteScalar() is null)
            return;
        mode.CommandText = """
            INSERT INTO "SistemDurumu" ("Id", "OturumDonemi", "YedekZamani") VALUES (1, '', $an)
            ON CONFLICT ("Id") DO UPDATE SET "YedekZamani" = excluded."YedekZamani";
            """;
        mode.Parameters.AddWithValue("$an", an);
        mode.ExecuteNonQuery();
    }

    private void Arsivle(string dbYolu, string zipYolu, DateTimeOffset now, string tur, byte[] checksum, IReadOnlyList<string>? bekleyenIsler, YedekBelgeListesi? belgeler)
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
        byte[]? liste = belgeler is null ? null : BelgeListesiJson(belgeler);
        if (liste is not null)
        {
            using var listeAkisi = zip.CreateEntry(BelgeListesiGirdisi).Open();
            listeAkisi.Write(liste);
        }
        // Eski biçimde (2.1.0) belgeler kasa.db içindedir ('belgelerDahil'); belge deposu biçiminde (2.2.0) kasa.db yalnız özetleri taşır,
        // belgeler.json listeyi ve özetini verir; içerikler yedek aynasında ya da elle indirilende belgeler/<özet> girdilerindedir.
        var manifest = new Dictionary<string, object?>
        {
            ["surum"] = belgeler is null ? EskiSurum : DepoluSurum,
            ["olusturuldu"] = now,
            ["tur"] = tur,
            ["sha256"] = Convert.ToHexString(checksum),
            ["belgelerDahil"] = belgeler is null,
        };
        if (belgeler is not null)
        {
            manifest["belgeDeposu"] = true;
            manifest["belgeSayisi"] = belgeler.Belgeler.Count;
            manifest["belgeToplamBayt"] = belgeler.Belgeler.Sum(b => b.Boyut);
            manifest["belgeListesiSha256"] = Convert.ToHexString(SHA256.HashData(liste!));
            manifest["eksikBelgeSayisi"] = belgeler.Eksik.Count;
            manifest["belgelerGomulu"] = false;
        }
        manifest["bildirimAnahtariDahil"] = keys is not null;
        manifest["bildirimAnahtariSha256"] = keyHash;
        // Göç öncesi yedekte bekleyen işler (migration kimlikleri, veri adımları) listelenir; restore aracının 8 KB manifest
        // sınırını aşmasın diye sayı ve uzunluk kısaltılır.
        if (bekleyenIsler is not null)
            manifest["bekleyenIsler"] = bekleyenIsler.Take(40).Select(i => i.Length > 120 ? i[..120] : i).ToList();
        using var stream = zip.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(stream, manifest);
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
        if (yedek.IsInvalid)
            SqliteException.ThrowExceptionForRC(raw.sqlite3_errcode(hedef.Handle), hedef.Handle);
        int mesgul = 0, bastan = 0, oncekiKalan = int.MaxValue;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var rc = raw.sqlite3_backup_step(yedek, bastan >= BastanAlmaSiniri ? -1 : sayfaGrubu);
            if (rc == raw.SQLITE_DONE)
                return;
            if (rc is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
            {
                if (++mesgul > MesgulDenemeSiniri)
                    SqliteException.ThrowExceptionForRC(rc, hedef.Handle);
                if (ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50)))
                    ct.ThrowIfCancellationRequested();
                continue;
            }
            SqliteException.ThrowExceptionForRC(rc, hedef.Handle);
            mesgul = 0;
            var kalan = raw.sqlite3_backup_remaining(yedek);
            if (kalan > oncekiKalan)
                bastan++; // kaynak başka bağlantıdan yazıldı; SQLite kopyayı baştan aldı
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
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Göç öncesi yedeğin bütünlüğü doğrulanamadı (integrity_check).");
    }

    public static void Dogrula(string path)
    {
        using var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        restored.Open();
        using var check = restored.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Yedek bütünlüğü doğrulanamadı.");
        check.CommandText = "PRAGMA foreign_key_check;";
        using (var reader = check.ExecuteReader())
            if (reader.Read())
                throw new InvalidDataException("Yedekte geçersiz ilişki var.");
        check.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";
        if (Convert.ToInt32(check.ExecuteScalar()) < 1)
            throw new InvalidDataException("Yedek şeması bulunamadı.");
    }
}

public sealed class OtomatikYedek(IServiceScopeFactory scopes, YedekServisi yedek, TimeProvider saat, ILogger<OtomatikYedek> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!yedek.Etkin)
            return;
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
