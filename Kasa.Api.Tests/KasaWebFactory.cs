using System.Collections.Generic;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Testler için uygulamayı açık tutulan bir SQLite in-memory bağlantısıyla
/// (kalıcı şema) ve sabit editör/JWT config'iyle ayağa kaldırır.
/// </summary>
public class KasaWebFactory : WebApplicationFactory<Program>
{
    /// <summary>Takvime bağlı testlerin varsayılan "bugün"ü (paket bu gün yeşil doğrulandı). Yıl başı,
    /// artık yılın Şubat sonu ve kırpılan ay sonu, ilgili test sınıflarının iç sınıflarında ayrıca koşar.</summary>
    public static readonly DateOnly VarsayilanBugun = new(2026, 9, 25);

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    // Yedekler (ör. dosya veritabanında açılıştaki göç öncesi yedek) kaynak ağacına değil geçici dizine yazılır; testler kendi
    // dizinini verebilir.
    private readonly string _yedekDizini = Path.Combine(Path.GetTempPath(), "kasa-test-yedek-" + Guid.NewGuid().ToString("N"));

    /// <summary>Sunucunun saati; null ise sistem saati (üretimdeki gibi). Uygulama kurulmadan, nesne
    /// başlatıcısında verilir. Her fabrikanın kendi saati vardır; paralel fabrikalar birbirini etkilemez.</summary>
    public TimeProvider? Saat { get; init; }

    /// <summary>Sunucunun gördüğü İstanbul günü.</summary>
    public DateOnly Bugun => (Saat ?? TimeProvider.System).IstanbulBugun();

    /// <summary>Sunucunun "bugün"ünü verilen İstanbul gününe sabitleyen fabrika.</summary>
    public static KasaWebFactory Sabit(DateOnly bugun) => new() { Saat = new SabitSaat(bugun) };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conn.Open(); // bağlantı açık kaldıkça in-memory DB yaşar
        if (Saat is not null) IlkAcilisAyari(Bugun);
        // Testlerin Windows Event Log yazma iznine bağımlı olmasını engelle.
        builder.ConfigureLogging(logging => logging.ClearProviders());

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
                ["Yedek:Dizin"] = _yedekDizini,
            });
        });

        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<KasaDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_conn));
            services.AddKasaSaati();
            if (Saat is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Saat);
            }
        });
    }

    // Program.cs ilk açılışta takip başlangıcını makine tarihiyle (DateTime.Today) tohumlar; ayar satırı
    // varsa tohumlamaz. Sabit saatli sunucuda aynı tohum sabit güne göre önceden yazılır.
    private void IlkAcilisAyari(DateOnly bugun)
    {
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(_conn).Options);
        KasaDatabaseInitializer.Initialize(db);
        db.Ayarlar.Add(new AyarEntity { TakipBaslangic = bugun, KasaAcilisDevri = 0m });
        db.SaveChanges();
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
        if (!disposing) return;
        _conn.Dispose();
        try { if (Directory.Exists(_yedekDizini)) Directory.Delete(_yedekDizini, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Sınıf fikstürü (IClassFixture) olarak sunucunun "bugün"ünü <see cref="KasaWebFactory.VarsayilanBugun"/>'e sabitler.</summary>
public class SabitSaatliKasaWebFactory : KasaWebFactory
{
    public SabitSaatliKasaWebFactory() => Saat = new SabitSaat(VarsayilanBugun);
}

/// <summary>Durmuş test saati: verilen anı ya da İstanbul'da verilen günün 12:00'sini döndürür; yalnız
/// testin kendisi <see cref="Ayarla"/> ile ilerletir. Zamanlayıcılar sistem saatiyle çalışır.</summary>
public sealed class SabitSaat(DateTimeOffset an) : TimeProvider
{
    private long _utcTicks = an.UtcTicks;

    public SabitSaat(DateOnly gun) : this(Oglen(gun)) { }

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    public void Ayarla(DateOnly gun) => Interlocked.Exchange(ref _utcTicks, Oglen(gun).UtcTicks);

    private static DateTimeOffset Oglen(DateOnly gun) =>
        new(TimeZoneInfo.ConvertTimeToUtc(gun.ToDateTime(new TimeOnly(12, 0)), KasaSaati.Istanbul), TimeSpan.Zero);
}
