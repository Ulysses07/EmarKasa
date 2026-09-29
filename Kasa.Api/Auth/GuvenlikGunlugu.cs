using System.Globalization;
using System.Text;
using System.Text.Json;
using Kasa.Api.Denetim;
using Kasa.Api.Servisler;

namespace Kasa.Api.Auth;

/// <summary>
/// Veritabanı dışındaki güvenlik günlüğü (gap-geri-yukleme-durum-geri-sarma-1, -11). Kimlik durumunu değiştiren kararlar (editör
/// şifresi ve kurtarma kodu, izleyici şifresi, alıcı hesapları, cihaz bildirim kayıtları, dönem kilidi) ve geri yüklemeler,
/// veritabanıyla birlikte geri sarılmayan bir dosyaya satır satır JSON olarak eklenir. Geri yüklenen veritabanı yedek anından
/// sonraki kararları taşımaz; açılıştaki geri yükleme işlemi (<see cref="GeriYuklemeIsleyici"/>) yedek anından sonraki olayları bu
/// dosyadan okur ve yalnız sıkılaştırıcı olanları yeniden uygular.
/// <list type="bullet">
/// <item>Yer: <c>GuvenlikGunlugu:Yol</c>; verilmezse yedek dizininde (<c>Yedek:Dizin</c>, compose'da <c>/yedekler</c>)
/// <see cref="DosyaAdi"/>. Canlı veritabanının dizininden (<c>/data</c>) bağımsızdır: veritabanı geri yüklense de kalır. Yedek
/// rotasyonu ve sunucu dışı yedek yalnız <c>kasa-*.zip</c> dosyalarına bakar, bu dosyaya dokunmaz. Silinmemelidir.</item>
/// <item>Yazma: <c>GuvenlikGunlugu:Etkin</c> (varsayılan: geliştirme ortamı dışında açık). Olay, veritabanı değişikliği kaydedildikten
/// SONRA yazılır; yazılamazsa hata loglanır ve isteğin sonucu değişmez (yedek diski dolu diye şifre değişikliği engellenmez).
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
    /// <summary>Dosyanın ilk satırı: günlüğün tutulmaya başladığı an.</summary>
    public const string Basladi = "GunlukBasladi";
    /// <summary>Editörün şifre değişikliği (<see cref="GuvenlikOlaylari.SifreDegisti"/> ile aynı ad).</summary>
    public const string EditorSifresiDegisti = GuvenlikOlaylari.SifreDegisti;
    public const string KurtarmaKullanildi = GuvenlikOlaylari.KurtarmaKullanildi;
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
        public bool? Mantiksal(string ad) => Ayrinti.ValueKind == JsonValueKind.Object && Ayrinti.TryGetProperty(ad, out var d)
            && d.ValueKind is JsonValueKind.True or JsonValueKind.False ? d.GetBoolean() : null;
        public string? Metin(string ad) => Ayrinti.ValueKind == JsonValueKind.Object && Ayrinti.TryGetProperty(ad, out var d)
            && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
    }

    /// <param name="Baslangic">Dosyadaki ilk okunabilir satırın anı (olağan durumda <see cref="Basladi"/>); okunabilir satır yoksa null.</param>
    public sealed record Icerik(DateTimeOffset? Baslangic, IReadOnlyList<Olay> Olaylar);

    private readonly Lock _kilit = new();
    private readonly TimeProvider _saat;
    private readonly ILogger _log;

    public GuvenlikGunlugu(IConfiguration cfg, IWebHostEnvironment env, YedekServisi yedek, TimeProvider saat, ILoggerFactory loglar)
    {
        Yol = Path.GetFullPath(cfg["GuvenlikGunlugu:Yol"] is { Length: > 0 } yol ? yol : Path.Combine(yedek.Dizin, DosyaAdi));
        Etkin = cfg.GetValue("GuvenlikGunlugu:Etkin", !env.IsDevelopment());
        _saat = saat;
        _log = loglar.CreateLogger("Kasa.Guvenlik");
    }

    public string Yol { get; }
    public bool Etkin { get; }

    /// <summary>Olayı ekler (değişiklik kaydedildikten sonra çağrılır). Hata isteği bozmaz: loglanır.</summary>
    /// <param name="ayrinti">Şifre, özet, kod, belirteç ve bildirim uç adresi içermeyen ek bilgi.</param>
    public void Yaz(string tur, int? hedefId = null, string? kullanici = null, object? ayrinti = null)
    {
        _log.LogInformation("Güvenlik günlüğü olayı {Tur}: hedef {Hedef}, kullanıcı {Kullanici}.", tur,
            hedefId?.ToString(CultureInfo.InvariantCulture) ?? "-", kullanici ?? "-");
        if (Etkin)
            Ekle(tur, Satir(_saat.GetUtcNow(), tur, hedefId, kullanici, ayrinti));
    }

    /// <summary>Açılışta: günlük açıksa ve dosya yoksa <see cref="Basladi"/> satırıyla oluşturur (yeri loglanır).</summary>
    public void Hazirla()
    {
        if (!Etkin)
            return;
        var yeni = !File.Exists(Yol);
        Ekle(Basladi, null);
        if (yeni && File.Exists(Yol))
            _log.LogInformation("Güvenlik günlüğü oluşturuldu: {Yol}. Silmeyin; geri yüklemede yedekten sonraki kararlar buradan yeniden uygulanır.", Yol);
    }

    /// <summary>Günlüğü okur; dosya yoksa ya da okunamazsa null. Bozuk (ör. yarım yazılmış) satırlar atlanır.</summary>
    public Icerik? Oku()
    {
        var satirlar = new List<string>();
        try
        {
            lock (_kilit)
            {
                if (!File.Exists(Yol))
                    return null;
                using var akis = new FileStream(Yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var okuyucu = new StreamReader(akis, Encoding.UTF8);
                while (okuyucu.ReadLine() is { } satir)
                    satirlar.Add(satir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Güvenlik günlüğü okunamadı ({Yol}).", Yol);
            return null;
        }
        DateTimeOffset? baslangic = null;
        var olaylar = new List<Olay>();
        foreach (var satir in satirlar)
        {
            if (Coz(satir) is not { } olay)
                continue;
            baslangic ??= olay.Zaman;
            if (olay.Tur != Basladi)
                olaylar.Add(olay);
        }
        return new Icerik(baslangic, olaylar);
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

    private void Ekle(string tur, string? satir)
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
                var secenekler = new FileStreamOptions
                {
                    Mode = FileMode.Append,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    Options = FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                    secenekler.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var akis = new FileStream(Yol, secenekler);
                var metin = new StringBuilder();
                if (akis.Length == 0)
                    metin.Append(Satir(_saat.GetUtcNow(), Basladi, null, null, null)).Append('\n');
                if (satir is not null)
                    metin.Append(satir).Append('\n');
                if (metin.Length == 0)
                    return;
                akis.Write(Encoding.UTF8.GetBytes(metin.ToString()));
                akis.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Güvenlik günlüğüne yazılamadı ({Yol}); {Tur} olayı yalnız bu logda.", Yol, tur);
        }
    }
}
