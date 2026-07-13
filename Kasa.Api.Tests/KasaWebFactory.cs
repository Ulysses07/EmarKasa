using System.Collections.Generic;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Testler için uygulamayı açık tutulan bir SQLite in-memory bağlantısıyla
/// (kalıcı şema) ve sabit editör/JWT config'iyle ayağa kaldırır.
/// </summary>
public class KasaWebFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

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
            });
        });

        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<KasaDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_conn));
        });
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }
}
