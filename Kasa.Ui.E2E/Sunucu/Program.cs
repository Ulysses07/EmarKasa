using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Ui.E2E.Sunucu;

/// <summary>
/// Kasa.Api'yi uçtan uca testler için açar (playwright.config.mjs webServer). Uygulamanın kendi yapılandırması kullanılır;
/// yalnız dışarıdan verilenler değişir:
/// - Saat: DI TimeProvider, KASA_E2E_BUGUN gününün İstanbul 12:00'sinde durur (Kasa.Api.Tests SabitSaat ile aynı kural).
///   Tarayıcı saati testte aynı ana sabitlenir; dönem, ay ve "bugün" her koşuda aynıdır.
/// - Veri: her açılışta sıfırdan oluşturulan geçici dizin (işletim sistemi geçici klasörü/kasa-e2e-&lt;port&gt;): SQLite
///   dosyası, yedek ve belge dizini. Canlı veritabanına, canlı sunucuya ve depo içindeki kasa.db'ye dokunulmaz.
/// - Kimlik: Kasa__EditorKullanici, Kasa__EditorSifre, Kasa__JwtKey ortamdan gelir (playwright.config.mjs her koşuda üretir).
/// - Ortam: WebApplicationFactory Development açar (appsettings.Development.json: yüksek hız sınırları, çerez Secure değil).
/// Üst düzey deyim yerine Main: derlemenin kendi Program türü olmaz, WebApplicationFactory Kasa.Api'nin Program'ını alır.
/// </summary>
internal static class Baslangic
{
    public static async Task<int> Main()
    {
        var port = int.Parse(Ortam("KASA_E2E_PORT") ?? "5390", CultureInfo.InvariantCulture);
        var bugun = DateOnly.ParseExact(Ortam("KASA_E2E_BUGUN") ?? "2026-09-25", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var ad in new[] { "Kasa__EditorKullanici", "Kasa__EditorSifre", "Kasa__JwtKey" })
        {
            if (Ortam(ad) is null)
            {
                await Console.Error.WriteLineAsync($"{ad} ortam değişkeni verilmedi; sunucuyu playwright.config.mjs başlatır.");
                return 2;
            }
        }

        var veri = Path.Combine(Path.GetTempPath(), $"kasa-e2e-{port}");
        if (Directory.Exists(veri))
            Directory.Delete(veri, recursive: true);
        Directory.CreateDirectory(veri);
        // Program.cs bağlantı dizesini ve yapılandırmayı oluşturucu kurulurken okur: ortam değişkeni en son sağlayıcıdır,
        // appsettings dosyalarındakini ezer.
        Environment.SetEnvironmentVariable("ConnectionStrings__Kasa", $"Data Source={Path.Combine(veri, "kasa.db")}");
        Environment.SetEnvironmentVariable("Yedek__Dizin", Path.Combine(veri, "yedekler"));
        Environment.SetEnvironmentVariable("Belge__Dizin", Path.Combine(veri, "belgeler"));
        Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Warning");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command", "Warning");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore.Infrastructure", "Warning");

        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var an = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(bugun.ToDateTime(new TimeOnly(12, 0)), istanbul), TimeSpan.Zero);

        var bitti = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var kesme = PosixSignalRegistration.Create(PosixSignal.SIGINT, s => { s.Cancel = true; bitti.TrySetResult(); });
        using var sonlandirma = PosixSignalRegistration.Create(PosixSignal.SIGTERM, s => { s.Cancel = true; bitti.TrySetResult(); });

        var sunucu = new E2eSunucusu(new DurmusSaat(an));
        try
        {
            sunucu.UseKestrel(port);
            sunucu.StartServer();
            Console.WriteLine($"Kasa e2e sunucusu hazır: http://127.0.0.1:{port} (bugün {bugun:yyyy-MM-dd}, veri {veri})");
            await bitti.Task;
        }
        finally
        {
            await sunucu.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            { Directory.Delete(veri, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return 0;
    }

    private static string? Ortam(string ad) => Environment.GetEnvironmentVariable(ad) is { Length: > 0 } deger ? deger : null;
}

/// <summary>Kasa.Api'yi değiştirmeden yalnız saati sabitleyen fabrika (Kasa.Api.Tests KasaWebFactory ile aynı yol).</summary>
internal sealed class E2eSunucusu(TimeProvider saat) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(saat);
        });
}

/// <summary>Durmuş saat: her okuma aynı anı verir (zaman damgaları ve "bugün" koşudan koşuya aynı).</summary>
internal sealed class DurmusSaat(DateTimeOffset an) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => an;
}
