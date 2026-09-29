using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Fiziksel SQLite dosyasıyla (WAL, havuzsuz bağlantı) çalışan uygulama. Kısa bekleme süresi
/// (Default Timeout=5) kilit bekleyen yazmayı testte hızla görünür kılar.</summary>
internal sealed class DosyaFabrikasi : KasaWebFactory
{
    public string Yol { get; } = Path.Combine(Path.GetTempPath(), "kasa-okuma-" + Guid.NewGuid().ToString("N") + ".db");
    public string Baglanti => new SqliteConnectionStringBuilder { DataSource = Yol, Pooling = false, DefaultTimeout = 5 }.ToString();
    public DbCommandInterceptor[] Kesiciler { get; init; } = [];
    public Dictionary<string, string?> EkAyarlar { get; init; } = [];
    public DosyaFabrikasi() => Saat = new SabitSaat(VarsayilanBugun);
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(EkAyarlar));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<KasaDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
            services.AddDbContext<KasaDbContext>(o => o.UseSqlite(Baglanti).AddInterceptors(Kesiciler));
        });
    }
    public KasaDbContext Baglam() => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti).Options);
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            foreach (var ek in new[] { "", "-wal", "-shm", "-journal" })
                try
                { File.Delete(Yol + ek); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
