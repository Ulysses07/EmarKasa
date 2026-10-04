using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Denetim;
using Kasa.Api.Servisler;

namespace Kasa.Api.Auth;

/// <summary>
/// Veritabanı dışındaki güvenlik günlüğü (gap-geri-yukleme-durum-geri-sarma-1, -11). Kimlik durumunu değiştiren kararlar (editör
/// şifresi, kurtarma kodu ve operatörün şifre sıfırlaması, izleyici şifresi, alıcı hesapları, cihaz bildirim kayıtları, dönem
/// kilidi) ve geri yüklemeler, veritabanıyla birlikte geri sarılmayan bir dosyaya satır satır JSON olarak eklenir. Geri yüklenen
/// veritabanı yedek anından sonraki kararları taşımaz; açılıştaki geri yükleme işlemi (<see cref="GeriYuklemeIsleyici"/>) yedek
/// anından sonraki olayları bu dosyadan okur ve yalnız sıkılaştırıcı olanları yeniden uygular.
/// <list type="bullet">
/// <item>Yer: <c>GuvenlikGunlugu:Yol</c>; verilmezse yedek dizininde (<c>Yedek:Dizin</c>, compose'da <c>/yedekler</c>)
/// <see cref="DosyaAdi"/>. Canlı veritabanının dizininden (<c>/data</c>) bağımsızdır: veritabanı geri yüklense de kalır. Yedek
/// rotasyonu ve sunucu dışı yedek yalnız <c>kasa-*.zip</c> dosyalarına bakar, bu dosyaya dokunmaz. Silinmemelidir.</item>
/// <item>Yazma: <c>GuvenlikGunlugu:Etkin</c> (varsayılan: geliştirme ortamı dışında açık). Kimlik ve yetki kararları için olay,
/// veritabanı transaction'ı commit edilmeden önce zorunlu olarak yazılır; yazılamazsa transaction geri alınır.
/// Dosyaya yalnız eklenir, her satır diske zorlanır (write-through); Unix'te 0600. Döndürülmez: olaylar seyrektir (yalnız editörün
/// ya da kurtarma kodunun yaptığı değişiklikler) ve eski bir yedeğin geri yüklenmesi o andan sonraki bütün olaylara ihtiyaç duyar.</item>
/// <item>İçerik: an (UTC), tür, hedef kimliği, kullanıcı adı ve gizli bilgi taşımayan ayrıntı. Şifre, şifre özeti, kurtarma kodu,
/// oturum ya da cihaz belirteci ve bildirim uç adresi (endpoint) ASLA yazılmaz.</item>
/// <item>İlk satır <see cref="Basladi"/>'dır: günlüğün hangi andan beri tutulduğu. Bu andan önce alınmış bir yedek geri yüklenirse
/// aradaki değişikliklerin bilinemediği rapora yazılır. Dosya açılışta (<see cref="Hazirla"/>) yoksa oluşturulur.</item>
/// <item>Her olay 'Kasa.Guvenlik' loguna da düşer (konteyner logu: veritabanından bağımsız ikinci iz).</item>
/// </list>
/// </summary>
public sealed class GuvenlikGunlugu
{
    public const string DosyaAdi = "guvenlik-gunlugu.jsonl";
    public const string KontrolDosyasiEki = ".kontrol.json";
    /// <summary>Dosyanın ilk satırı: günlüğün tutulmaya başladığı an.</summary>
    public const string Basladi = "GunlukBasladi";
    /// <summary>Editörün şifre değişikliği (<see cref="GuvenlikOlaylari.SifreDegisti"/> ile aynı ad).</summary>
    public const string EditorSifresiDegisti = GuvenlikOlaylari.SifreDegisti;
    public const string KurtarmaKullanildi = GuvenlikOlaylari.KurtarmaKullanildi;
    /// <summary>Operatörün editör şifresi sıfırlaması (<see cref="EditorSifreSifirlama"/>). Ayrıntı: oturumlarKapatildi,
    /// kurtarmaKoduIptal, girisKilidiKaldirildi. Şifre ve sıfırlama izi yazılmaz.</summary>
    public const string EditorSifresiSifirlandi = GuvenlikOlaylari.EditorSifresiSifirlandi;
    public const string KurtarmaKoduUretildi = GuvenlikOlaylari.KurtarmaKoduUretildi;
    public const string IzleyiciSifresiDegisti = "IzleyiciSifresiDegisti";
    /// <summary>Ayrıntı: aktif.</summary>
    public const string AliciOlusturuldu = "AliciOlusturuldu";
    /// <summary>Ayrıntı: oncekiKullanici, oncekiAktif, aktif, sifreDegisti.</summary>
    public const string AliciGuncellendi = "AliciGuncellendi";
    public const string PushAboneligiKaldirildi = "PushAboneligiKaldirildi";
    /// <summary>Ayrıntı: onceki, yeni (kilitli son tarih, ISO; açıkta boş), aciklama (ilk 200 karakter).</summary>
    public const string AyKilidiKapatildi = "AyKilidiKapatildi";
    public const string AyKilidiAcildi = "AyKilidiAcildi";
    public const string GeriYuklemeIslendi = GuvenlikOlaylari.GeriYuklemeIslendi;

    /// <param name="Ayrinti">Olayın ayrıntı nesnesi; yoksa <see cref="JsonValueKind.Undefined"/>.</param>
    public sealed record Olay(DateTimeOffset Zaman, string Tur, int? HedefId, string? Kullanici, JsonElement Ayrinti)
    {
        /// <summary>Bu satırın bitiminden sonraki bayt konumu; yedek kesimini saatten bağımsız belirler.</summary>
        public long SonBayt { get; init; }
        public bool? Mantiksal(string ad) => Ayrinti.ValueKind == JsonValueKind.Object && Ayrinti.TryGetProperty(ad, out var d)
            && d.ValueKind is JsonValueKind.True or JsonValueKind.False ? d.GetBoolean() : null;
        public string? Metin(string ad) => Ayrinti.ValueKind == JsonValueKind.Object && Ayrinti.TryGetProperty(ad, out var d)
            && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
    }

    /// <param name="Baslangic">Dosyadaki ilk okunabilir satırın anı (olağan durumda <see cref="Basladi"/>); okunabilir satır yoksa null.</param>
    public sealed record Icerik(DateTimeOffset? Baslangic, IReadOnlyList<Olay> Olaylar, long DogrulamaBaslangiciBayt);

    /// <summary>Yedek anında, ortak seri kilit altında alınan günlük kesimi.</summary>
    public sealed record KesimNoktasi(long Bayt, string Sha256);

    private sealed record Kontrol(int Surum, long Bayt, string Sha256, long DogrulamaBaslangiciBayt,
        long? BekleyenBayt = null, string? BekleyenSha256 = null);
    private static readonly UTF8Encoding KatiUtf8 = new(false, true);

    private readonly Lock _kilit = new();
    private readonly GuvenlikYedekSeriKilidi _seriKilit;
    private readonly TimeProvider _saat;
    private readonly ILogger _log;

    public GuvenlikGunlugu(IConfiguration cfg, IWebHostEnvironment env, TimeProvider saat, ILoggerFactory loglar,
        GuvenlikYedekSeriKilidi seriKilit)
    {
        var yedekDizini = Path.GetFullPath(cfg["Yedek:Dizin"] ?? Path.Combine(env.ContentRootPath, "yedekler"));
        Yol = Path.GetFullPath(cfg["GuvenlikGunlugu:Yol"] is { Length: > 0 } yol ? yol : Path.Combine(yedekDizini, DosyaAdi));
        KontrolYolu = Yol + KontrolDosyasiEki;
        Etkin = cfg.GetValue("GuvenlikGunlugu:Etkin", !env.IsDevelopment());
        if (!Etkin && !env.IsDevelopment())
            throw new InvalidOperationException("GuvenlikGunlugu:Etkin=false üretim ortamında desteklenmez: kimlik kararlarının geri yükleme günlüğü zorunludur.");
        _seriKilit = seriKilit;
        _saat = saat;
        _log = loglar.CreateLogger("Kasa.Guvenlik");
    }

    public string Yol { get; }
    public string KontrolYolu { get; }
    public bool Etkin { get; }

    /// <summary>Kimlik/izin değişikliği transaction'ından önce alınır, günlük append ve DB commit tamamlanana kadar tutulur.</summary>
    public IDisposable IslemKilidiAl() => _seriKilit.Al();

    /// <summary>Olayı ekler. Zorunlu olayda yazma hatası çağırana iletilir; çağıran transaction'ı commit etmemelidir.</summary>
    /// <param name="ayrinti">Şifre, özet, kod, belirteç ve bildirim uç adresi içermeyen ek bilgi.</param>
    public void Yaz(string tur, int? hedefId = null, string? kullanici = null, object? ayrinti = null, bool zorunlu = false)
    {
        _log.LogInformation("Güvenlik günlüğü olayı {Tur}: hedef {Hedef}, kullanıcı {Kullanici}.", tur,
            hedefId?.ToString(CultureInfo.InvariantCulture) ?? "-", kullanici ?? "-");
        if (Etkin)
            Ekle(tur, Satir(_saat.GetUtcNow(), tur, hedefId, kullanici, ayrinti), zorunlu);
    }

    /// <summary>Açılışta günlük ve bağımsız bütünlük kontrolünü hazırlar. Eski, kontrolsüz günlükler bu andan itibaren doğrulanır.</summary>
    public void Hazirla()
    {
        if (!Etkin)
            return;
        var yeni = !File.Exists(Yol);
        Ekle(Basladi, null, zorunlu: true);
        if (yeni && File.Exists(Yol))
            _log.LogInformation("Güvenlik günlüğü oluşturuldu: {Yol}. Silmeyin; geri yüklemede yedekten sonraki kararlar buradan yeniden uygulanır.", Yol);
    }

    /// <summary>Günlüğü okur; dosya gerçekten yoksa null. Etkin günlük okunamaz veya bozuksa geri yükleme durur.</summary>
    public Icerik? Oku()
    {
        try
        {
            lock (_kilit)
                return DurumOku(onar: false).Icerik;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Güvenlik günlüğü okunamadı ({Yol}).", Yol);
            if (Etkin)
                throw;
            return null;
        }
    }

    /// <summary>Yedek kopyasındaki bayt kesimi ve özeti; çağıran ortak güvenlik/yedek seri kilidini tutmalıdır.</summary>
    public KesimNoktasi KesimNoktasiAl()
    {
        if (!Etkin)
            throw new InvalidOperationException("Kapalı güvenlik günlüğünden yedek kesimi alınamaz.");
        lock (_kilit)
        {
            var (baytlar, kontrol, _) = DurumOku(onar: true);
            if (kontrol is null)
                throw new InvalidDataException("Güvenlik günlüğü ve bütünlük kontrolü hazırlanmamış.");
            return new KesimNoktasi(baytlar.LongLength, kontrol.Sha256);
        }
    }

    /// <summary>Yedekte saklanan kesimi doğrular ve o kesimden sonraki olayları döndürür; saat geri alınsa da sıra değişmez.</summary>
    public Icerik KesimdenSonraOku(KesimNoktasi kesim)
    {
        if (!Etkin)
            throw new InvalidOperationException("Kapalı güvenlik günlüğünden yedek sonrası olaylar okunamaz.");
        lock (_kilit)
        {
            var (baytlar, kontrol, icerik) = DurumOku(onar: false);
            if (kontrol is null || icerik is null || kesim.Bayt < kontrol.DogrulamaBaslangiciBayt
                || kesim.Bayt < 0 || kesim.Bayt > baytlar.LongLength
                || (kesim.Bayt > 0 && baytlar[checked((int)kesim.Bayt) - 1] != '\n')
                || !string.Equals(Ozet(baytlar.AsSpan(0, checked((int)kesim.Bayt))), kesim.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Yedekteki güvenlik günlüğü kesimi doğrulanamadı; yedekten sonraki kararlar eksik olabilir.");
            return icerik with { Olaylar = icerik.Olaylar.Where(o => o.SonBayt > kesim.Bayt).ToList() };
        }
    }

    private (byte[] Baytlar, Kontrol? Kontrol, Icerik? Icerik) DurumOku(bool onar)
    {
        if (!File.Exists(Yol))
        {
            if (File.Exists(KontrolYolu))
                throw new InvalidDataException($"Güvenlik günlüğü yok ama bütünlük kontrolü duruyor: {Yol}");
            return ([], null, null);
        }
        var baytlar = File.ReadAllBytes(Yol);
        var icerik = IcerikCoz(baytlar, 0);
        var kontrol = KontrolOku();
        if (kontrol is not null)
        {
            var guncel = Ozet(baytlar);
            var tamam = kontrol.Bayt == baytlar.LongLength && string.Equals(kontrol.Sha256, guncel, StringComparison.Ordinal);
            var bekleyen = kontrol.BekleyenBayt == baytlar.LongLength
                && string.Equals(kontrol.BekleyenSha256, guncel, StringComparison.Ordinal);
            if (!tamam && !bekleyen)
                throw new InvalidDataException($"Güvenlik günlüğünün bütünlüğü doğrulanamadı: {Yol}");
            if (bekleyen || kontrol.BekleyenBayt is not null)
            {
                kontrol = bekleyen
                    ? kontrol with { Bayt = kontrol.BekleyenBayt!.Value, Sha256 = kontrol.BekleyenSha256!, BekleyenBayt = null, BekleyenSha256 = null }
                    : kontrol with { BekleyenBayt = null, BekleyenSha256 = null };
                if (onar)
                    KontrolYaz(kontrol);
            }
        }
        var kapsama = kontrol?.DogrulamaBaslangiciBayt ?? baytlar.LongLength;
        return (baytlar, kontrol, icerik with { DogrulamaBaslangiciBayt = kapsama });
    }

    private static Icerik IcerikCoz(byte[] baytlar, long kapsama)
    {
        DateTimeOffset? baslangic = null;
        var olaylar = new List<Olay>();
        if (baytlar.Length > 0 && baytlar[^1] != '\n')
            throw new InvalidDataException("Güvenlik günlüğünün son satırı tamamlanmamış.");
        var ilk = 0;
        for (var i = 0; i < baytlar.Length; i++)
        {
            if (baytlar[i] != '\n')
                continue;
            var bas = ilk == 0 && baytlar.Length >= 3 && baytlar[0] == 0xEF && baytlar[1] == 0xBB && baytlar[2] == 0xBF ? 3 : ilk;
            var uzunluk = i - bas - (i > bas && baytlar[i - 1] == '\r' ? 1 : 0);
            Olay? olay;
            try
            { olay = Coz(KatiUtf8.GetString(baytlar, bas, uzunluk)); }
            catch (DecoderFallbackException) { olay = null; }
            if (olay is null)
                throw new InvalidDataException("Güvenlik günlüğünde bozuk satır var.");
            baslangic ??= olay.Zaman;
            if (olay.Tur != Basladi)
                olaylar.Add(olay with { SonBayt = i + 1L });
            ilk = i + 1;
        }
        return new Icerik(baslangic, olaylar, kapsama);
    }

    private static Olay? Coz(string satir)
    {
        if (string.IsNullOrWhiteSpace(satir))
            return null;
        try
        {
            using var belge = JsonDocument.Parse(satir);
            var kok = belge.RootElement;
            if (kok.ValueKind != JsonValueKind.Object
                || !kok.TryGetProperty("zaman", out var zaman) || !zaman.TryGetDateTimeOffset(out var an)
                || !kok.TryGetProperty("tur", out var tur) || tur.ValueKind != JsonValueKind.String)
                return null;
            int? hedef = kok.TryGetProperty("hedefId", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var id) ? id : null;
            var kullanici = kok.TryGetProperty("kullanici", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            var ayrinti = kok.TryGetProperty("ayrinti", out var a) && a.ValueKind == JsonValueKind.Object ? a.Clone() : default;
            return new Olay(an, tur.GetString()!, hedef, kullanici, ayrinti);
        }
        catch (JsonException) { return null; }
    }

    private static string Satir(DateTimeOffset zaman, string tur, int? hedefId, string? kullanici, object? ayrinti)
        => JsonSerializer.Serialize(new { zaman, tur, hedefId, kullanici, ayrinti }, DenetimYazici.JsonAyarlari);

    private static string Ozet(ReadOnlySpan<byte> baytlar) => Convert.ToHexString(SHA256.HashData(baytlar));

    private Kontrol? KontrolOku()
    {
        if (!File.Exists(KontrolYolu))
            return null;
        Kontrol? kontrol;
        try
        { kontrol = JsonSerializer.Deserialize<Kontrol>(File.ReadAllBytes(KontrolYolu)); }
        catch (JsonException ex) { throw new InvalidDataException($"Güvenlik günlüğü bütünlük kontrolü bozuk: {KontrolYolu}", ex); }
        static bool OzetGecerli(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
        if (kontrol is null || kontrol.Surum != 1 || kontrol.Bayt < 0
            || kontrol.DogrulamaBaslangiciBayt < 0 || kontrol.DogrulamaBaslangiciBayt > kontrol.Bayt
            || !OzetGecerli(kontrol.Sha256)
            || (kontrol.BekleyenBayt is null) != (kontrol.BekleyenSha256 is null)
            || (kontrol.BekleyenBayt is { } b && (b < kontrol.Bayt || !OzetGecerli(kontrol.BekleyenSha256))))
            throw new InvalidDataException($"Güvenlik günlüğü bütünlük kontrolü geçersiz: {KontrolYolu}");
        return kontrol;
    }

    private void KontrolYaz(Kontrol kontrol)
    {
        var gecici = KontrolYolu + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var baytlar = JsonSerializer.SerializeToUtf8Bytes(kontrol);
            var secenekler = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
                secenekler.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var akis = new FileStream(gecici, secenekler))
            {
                akis.Write(baytlar);
                akis.Flush(flushToDisk: true);
            }
            File.Move(gecici, KontrolYolu, overwrite: true);
        }
        finally
        {
            if (File.Exists(gecici))
                File.Delete(gecici);
        }
    }

    private void Ekle(string tur, string? satir, bool zorunlu)
    {
        try
        {
            lock (_kilit)
            {
                var dizin = Path.GetDirectoryName(Yol)!;
                if (OperatingSystem.IsWindows())
                    Directory.CreateDirectory(dizin);
                else
                    Directory.CreateDirectory(dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                // Yeni dosya önce boş olarak kalıcılaşır; böylece kontrol dosyasından sonra bir çökme olursa
                // açılış boş başlığı görüp güvenle devam edebilir.
                if (!File.Exists(Yol) && !File.Exists(KontrolYolu))
                {
                    var olustur = new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.Read,
                        Options = FileOptions.WriteThrough,
                    };
                    if (!OperatingSystem.IsWindows())
                        olustur.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    using var bos = new FileStream(Yol, olustur);
                    bos.Flush(flushToDisk: true);
                }
                var (onceki, kontrol, _) = DurumOku(onar: true);
                if (kontrol is null)
                {
                    // 2.4 günlüğü bozulmadan kalır; geçmiş bölümün kesilmediği bilinemez. İlk yeni
                    // kontrol noktasından önce alınmış yedekler geri yüklenince kimlikler sıkılaştırılır.
                    kontrol = new Kontrol(1, onceki.LongLength, Ozet(onceki), onceki.LongLength);
                    KontrolYaz(kontrol);
                }
                var metin = onceki.Length == 0 ? Satir(_saat.GetUtcNow(), Basladi, null, null, null) + "\n" : "";
                if (satir is not null)
                    metin += satir + "\n";
                if (metin.Length == 0)
                    return;
                var ek = Encoding.UTF8.GetBytes(metin);
                var yeni = new byte[checked(onceki.Length + ek.Length)];
                onceki.CopyTo(yeni, 0);
                ek.CopyTo(yeni, onceki.Length);
                var bekleyen = kontrol with { BekleyenBayt = yeni.LongLength, BekleyenSha256 = Ozet(yeni) };
                // Önce beklenen başlık, sonra günlük, en son kesin başlık: aradaki çökmede
                // eski veya tam yeni baytlar tanınır; eksik/başka içerik hiçbir zaman sessizce kabul edilmez.
                KontrolYaz(bekleyen);
                try
                {
                    using (var akis = new FileStream(Yol, new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Write,
                        Share = FileShare.Read,
                        Options = FileOptions.WriteThrough,
                    }))
                    {
                        if (akis.Length != onceki.LongLength)
                            throw new IOException($"Güvenlik günlüğü eşzamanlı değişti: {Yol}");
                        akis.Seek(0, SeekOrigin.End);
                        akis.Write(ek);
                        akis.Flush(flushToDisk: true);
                    }
                    KontrolYaz(kontrol with { Bayt = yeni.LongLength, Sha256 = bekleyen.BekleyenSha256!, BekleyenBayt = null, BekleyenSha256 = null });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // DB henüz commit edilmedi. Yazma başarısızsa günlüğü eski güvenli sınıra döndür;
                    // bu da başarısız olursa bir sonraki açılış eşleşmeyen dosyayı reddeder.
                    try
                    {
                        using var akis = new FileStream(Yol, FileMode.Open, FileAccess.Write, FileShare.Read);
                        akis.SetLength(onceki.LongLength);
                        akis.Flush(flushToDisk: true);
                        KontrolYaz(kontrol);
                    }
                    catch (Exception onarmaHatasi) when (onarmaHatasi is IOException or UnauthorizedAccessException)
                    { _log.LogError(onarmaHatasi, "Güvenlik günlüğü başarısız yazımdan sonra geri alınamadı ({Yol}).", Yol); }
                    throw;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Güvenlik günlüğüne yazılamadı ({Yol}); {Tur} olayı kaydedilmedi.", Yol, tur);
            if (zorunlu)
                throw;
        }
    }
}
