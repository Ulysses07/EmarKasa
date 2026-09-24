using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kasa.Api.Tests;

/// <summary>
/// Testler için uygulamayı açık tutulan bir SQLite in-memory bağlantısıyla
/// (kalıcı şema) ve sabit editör/JWT config'iyle ayağa kaldırır.
/// </summary>
public class KasaWebFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

    /// <summary>Testler çok sayıda giriş yapar; sınır testi bunu düşürür.</summary>
    protected virtual int GirisLimiti => 1000;
    protected virtual int GirisGlobalLimiti => 1000;

    /// <summary>Testlerin işlem eklerken kullandığı cariler (işlem cariye bağlı olmalı).</summary>
    public static readonly string[] TestCarileri = ["X", "A", "B", "Market", "PORT KARGO"];

    /// <summary>
    /// Test başlığı: TestServer'da bağlantı IP'si yoktur; <c>X-Test-Ip</c> ile verilir
    /// (ForwardedHeaders'tan ÖNCE çalışır, yani gerçek bağlantı adresini taklit eder).
    /// </summary>
    public const string TestIpBasligi = "X-Test-Ip";

    /// <summary>Editör giriş gövdesi; değerler yukarıda ayarlanan test ortam değişkenlerinden okunur.</summary>
    public static object EditorGirisi => new
    {
        kullanici = Environment.GetEnvironmentVariable("Kasa__EditorKullanici"),
        sifre = Environment.GetEnvironmentVariable("Kasa__EditorSifre"),
    };

    /// <summary>Testlerin DB'si: açık tutulan in-memory bağlantı (eşzamanlı istek testleri dosya DB'si kullanır).</summary>
    protected virtual void VeritabaniAyarla(DbContextOptionsBuilder o) => o.UseSqlite(_conn);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conn.Open(); // bağlantı açık kaldıkça in-memory DB yaşar

        // JwtBearer imzalama anahtarı Program.cs'de builder.Configuration'dan
        // (build anında) okunuyor; ConfigureAppConfiguration bu okumadan SONRA
        // çalıştığından geç kalıyor. Env değişkenleri WebApplication.CreateBuilder'ın
        // varsayılan config sağlayıcısına (appsettings.json'dan sonra) girer, böylece
        // hem build-anı hem çalışma-anı okumaları test değerlerini görür.
        Environment.SetEnvironmentVariable("Kasa__EditorKullanici", "editor");
        Environment.SetEnvironmentVariable("Kasa__EditorSifre", "kasa123");
        Environment.SetEnvironmentVariable("Kasa__JwtKey", "test-jwt-anahtari-en-az-32-bayt-olmali!!");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kasa:EditorKullanici"] = "editor",
                ["Kasa:EditorSifre"] = "kasa123",
                ["Kasa:JwtKey"] = "test-jwt-anahtari-en-az-32-bayt-olmali!!",
                ["Kasa:GirisLimiti"] = GirisLimiti.ToString(),
                ["Kasa:GirisGlobalLimiti"] = GirisGlobalLimiti.ToString(),
                // Saatlik güvenlik bakımı testlerin paylaşılan in-memory bağlantısına arka planda dokunmasın.
                ["Kasa:GuvenlikBakimi"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<KasaDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<KasaDbContext>(VeritabaniAyarla);
            services.AddTransient<IStartupFilter, TestIpFiltresi>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        foreach (var ad in TestCarileri)
            if (!db.Cariler.Any(c => c.Ad == ad)) db.Cariler.Add(new CariEntity { Ad = ad });
        db.SaveChanges();
        return host;
    }

    private sealed class TestIpFiltresi : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext ctx, Func<Task> sonraki) =>
            {
                if (ctx.Request.Headers.TryGetValue(TestIpBasligi, out var ip) && IPAddress.TryParse(ip, out var adres))
                    ctx.Connection.RemoteIpAddress = adres;
                return sonraki();
            });
            next(app);
        };
    }

    /// <summary>Editör olarak login olmuş bir HttpClient döner (auth cookie set).</summary>
    public async Task<HttpClient> EditorClientAsync()
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Takip başlangıcını geçmişe alır (gelen yalnız takvim içindeki dönemlere yazılabilir).</summary>
    public static async Task TakipBaslangiciAyarla(HttpClient c, DateOnly tarih, decimal kasaAcilis = 0m)
    {
        var r = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = tarih.ToString("yyyy-MM-dd"), kasaAcilisDevri = kasaAcilis });
        r.EnsureSuccessStatusCode();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }
}
