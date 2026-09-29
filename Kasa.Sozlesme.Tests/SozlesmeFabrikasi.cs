using System.Collections.Concurrent;
using System.Security.Cryptography;
using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.ApiClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Sözleşme testlerinin sunucusu: gerçek Kasa.Api, açık tutulan bellek içi SQLite, sabit saat (İstanbul'da
/// <see cref="Bugun"/> 12:00), yüksek hız sınırları, sahte push göndericisi ve sabit PDF metni. Ağa ve diske (geçici
/// yedek dizini dışında) çıkılmaz. Sunucu, istemcinin gönderdiği JSON gövdelerini ucun bağladığı türle birlikte kaydeder
/// (<see cref="Istekler"/>): istemcinin gönderip sunucunun tanımadığı alan sessizce kaybolmasın.
/// </summary>
public sealed class SozlesmeFabrikasi : WebApplicationFactory<Program>
{
    public static readonly DateOnly Bugun = new(2026, 9, 25);
    public const string EditorSifresi = "kasa-sozlesme-123";

    private readonly SqliteConnection _baglanti = new("Data Source=:memory:");
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "kasa-sozlesme-" + Guid.NewGuid().ToString("N"));
    private readonly string _acikAnahtar;
    private readonly string _gizliAnahtar;

    public SahtePushGonderici Push { get; } = new();
    public IstekKaydedici Istekler { get; } = new();
    /// <summary>Varsayılanların üzerine yazılan ayarlar (ör. düşük hız sınırı).</summary>
    public Dictionary<string, string?> EkAyarlar { get; init; } = [];
    /// <summary>Sahte PDF okuyucunun döndürdüğü metin: bugün tarihli tek banka komisyonu.</summary>
    public string PdfMetni { get; init; } = $"İşlem Tarihi    Açıklama                Tutar        Bakiye\n{Bugun:dd.MM.yyyy}    KOMİSYON                  -10,00 TL    990,00 TL\n";

    public SozlesmeFabrikasi()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        _acikAnahtar = PushDogrulama.Encode([4, .. p.Q.X!, .. p.Q.Y!]);
        _gizliAnahtar = PushDogrulama.Encode(p.D!);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _baglanti.Open();
        builder.ConfigureLogging(l => l.ClearProviders());
        // JWT anahtarı Program.cs'de derleme anında builder.Configuration'dan okunur; ortam değişkeni o okumadan önce görünür.
        Environment.SetEnvironmentVariable("Kasa__EditorKullanici", "editor");
        Environment.SetEnvironmentVariable("Kasa__EditorSifre", EditorSifresi);
        Environment.SetEnvironmentVariable("Kasa__JwtKey", "sozlesme-testleri-jwt-anahtari-32-bayt-ustu!!");
        var ayarlar = new Dictionary<string, string?>
        {
            ["Kasa:EditorKullanici"] = "editor",
            ["Kasa:EditorSifre"] = EditorSifresi,
            ["Kasa:JwtKey"] = "sozlesme-testleri-jwt-anahtari-32-bayt-ustu!!",
            ["Kasa:HizSiniri:GuvenlikIzni"] = "100000",
            ["Kasa:HizSiniri:GirisIpIzni"] = "100000",
            ["Kasa:HizSiniri:GirisKullaniciIzni"] = "100000",
            ["Kasa:HizSiniri:GirisAgIzni"] = "100000",
            ["Kasa:HizSiniri:HedefBasarisizIzni"] = "100000",
            ["Kasa:HizSiniri:AgBasarisizIzni"] = "50000",
            ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "100000",
            ["Kasa:HizSiniri:YedekIzni"] = "100000",
            ["Yedek:Dizin"] = _dizin,
            ["Yedek:Etkin"] = "false",
            ["Finans:BakimEtkin"] = "false",
            ["Bildirim:PushEtkin"] = "true",
            ["Bildirim:WorkerEtkin"] = "false",
            ["Bildirim:PublicKey"] = _acikAnahtar,
            ["Bildirim:PrivateKey"] = _gizliAnahtar,
        };
        foreach (var (anahtar, deger) in EkAyarlar)
            ayarlar[anahtar] = deger;
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(ayarlar));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<KasaDbContext>>();
            s.AddDbContext<KasaDbContext>(o => o.UseSqlite(_baglanti));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(new SabitSaat(Bugun));
            s.RemoveAll<IPushGonderici>();
            s.AddSingleton<IPushGonderici>(Push);
            s.RemoveAll<IPdfMetinOkuyucu>();
            s.AddSingleton<IPdfMetinOkuyucu>(new SabitPdf(PdfMetni));
            s.AddSingleton<IStartupFilter>(Istekler);
        });
    }

    /// <summary>Masaüstündeki gibi çerezsiz istemci (tek oturum kaynağı Bearer token); yanıtlar <paramref name="kayit"/>'a yazılır.</summary>
    public KasaApiClient Istemci(YanitKaydedici kayit, ITokenStore? depo = null) => new(CreateDefaultClient(kayit), depo ?? new BellekTokenStore());

    /// <summary>Sunucu verisini doğrudan (DI kapsamındaki bağlamla) hazırlar: istemcisi olmayan eski kayıtlar ve bildirimler.</summary>
    public void Veri(Action<KasaDbContext> hazirla)
    {
        using var scope = Services.CreateScope();
        hazirla(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _baglanti.Dispose();
        try
        { if (Directory.Exists(_dizin)) Directory.Delete(_dizin, true); }
        catch (IOException) { /* geçici dizin işletim sistemine kalır */ }
    }

    private sealed class SabitSaat(DateOnly gun) : TimeProvider
    {
        private readonly DateTimeOffset _an = new(TimeZoneInfo.ConvertTimeToUtc(gun.ToDateTime(new TimeOnly(12, 0)), KasaSaati.Istanbul), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _an;
    }

    private sealed class SabitPdf(string metin) : IPdfMetinOkuyucu
    {
        public Task<string> OkuAsync(byte[] pdf, CancellationToken ct = default) => Task.FromResult(metin);
    }
}

public sealed class SahtePushGonderici : IPushGonderici
{
    public ConcurrentQueue<PushIleti> Iletiler { get; } = new();
    public Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct)
    {
        Iletiler.Enqueue(ileti);
        return Task.FromResult(PushSonuc.Basarili);
    }
}

/// <summary>İstemciden gelen JSON gövdesi ve onu bağlayan uç (sunucu tarafında, yönlendirme sonrası).</summary>
public sealed record SunucuIstegi(string Uc, Type? GovdeTuru, string Govde);

/// <summary>Sunucu hattının en dışında JSON gövdelerini kaydeder; uç ve bağlanan tür (IAcceptsMetadata) yanıt üretildikten
/// sonra okunur.</summary>
public sealed class IstekKaydedici : IStartupFilter
{
    private readonly ConcurrentQueue<SunucuIstegi> _kayitlar = new();
    public IReadOnlyList<SunucuIstegi> Kayitlar => _kayitlar.ToArray();

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (http, devam) =>
        {
            var json = http.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
            if (json)
                http.Request.EnableBuffering();
            await devam(http);
            if (!json || http.GetEndpoint() is not RouteEndpoint uc)
                return;
            http.Request.Body.Position = 0;
            var govde = await new StreamReader(http.Request.Body).ReadToEndAsync();
            var tur = uc.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType;
            _kayitlar.Enqueue(new(SozlesmeDenetimi.UcAdi(http.Request.Method, http.Request.Path.Value ?? ""), tur, govde));
        });
        next(app);
    };
}
