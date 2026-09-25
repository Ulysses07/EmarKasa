using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

public record YedekDurumu(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata);

public sealed class YedekServisi(IConfiguration cfg, IWebHostEnvironment env, PushKimligi push)
{
    private readonly SemaphoreSlim kilit = new(1, 1);
    private DateTimeOffset? sonYedek;
    private DateTimeOffset? sonDogrulama;
    private string? hata;
    public bool Etkin => cfg.GetValue("Yedek:Etkin", !env.IsDevelopment());
    public string Dizin => Path.GetFullPath(cfg["Yedek:Dizin"] ?? Path.Combine(env.ContentRootPath, "yedekler"));
    public YedekDurumu Durum() => new(Etkin, sonYedek ?? SonDosya(), sonDogrulama, hata);
    private DateTimeOffset? SonDosya() => Directory.Exists(Dizin)
        ? Directory.EnumerateFiles(Dizin, "kasa-*.zip").Select(File.GetLastWriteTimeUtc).OrderDescending().Select(d => (DateTimeOffset?)d).FirstOrDefault() : null;

    public async Task<string> Olustur(KasaDbContext db, CancellationToken ct)
    {
        await kilit.WaitAsync(ct);
        string? temporary = null;
        string? zipTemporary = null;
        try
        {
            Directory.CreateDirectory(Dizin);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Dizin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var now = DateTimeOffset.UtcNow;
            var suffix = Guid.NewGuid().ToString("N");
            temporary = Path.Combine(Dizin, $".{suffix}.db");
            var path = Path.Combine(Dizin, $"kasa-{now:yyyyMMdd-HHmmss}-{suffix[..8]}.zip");
            zipTemporary = path + ".part";
            var source = (SqliteConnection)db.Database.GetDbConnection();
            bool close = source.State != System.Data.ConnectionState.Open;
            if (close) await source.OpenAsync(ct);
            try
            {
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
                target.Open();
                source.BackupDatabase(target);
            }
            finally { if (close) source.Close(); }
            // Yedek, özgün bağlantıdan bağımsız açılıp bütünlük ve ilişkiler sınanır.
            Dogrula(temporary);
            byte[] checksum;
            using (var stream = File.OpenRead(temporary)) checksum = await SHA256.HashDataAsync(stream, ct);
            using (var zip = ZipFile.Open(zipTemporary, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temporary, "kasa.db", CompressionLevel.Fastest);
                var keys = push.Get();
                string? keyHash = null;
                if (keys is not null)
                {
                    var keyBytes = JsonSerializer.SerializeToUtf8Bytes(keys);
                    keyHash = Convert.ToHexString(SHA256.HashData(keyBytes));
                    using var keyStream = zip.CreateEntry(".kasa-push-keys.json").Open();
                    await keyStream.WriteAsync(keyBytes, ct);
                }
                var manifest = zip.CreateEntry("manifest.json");
                using var stream = manifest.Open();
                JsonSerializer.Serialize(stream, new { surum = "2.1.0", olusturuldu = now, sha256 = Convert.ToHexString(checksum),
                    belgelerDahil = true, bildirimAnahtariDahil = keys is not null, bildirimAnahtariSha256 = keyHash });
            }
            File.Move(zipTemporary, path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            sonYedek = now; sonDogrulama = now; hata = null;
            // Yalnız bu servisin ürettiği yedekleri tutma süresine göre temizle.
            foreach (var old in Directory.EnumerateFiles(Dizin, "kasa-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(30)) File.Delete(old);
            return path;
        }
        catch
        {
            hata = "Son yedekleme tamamlanamadı. Sunucu kayıtlarını kontrol edin.";
            throw;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            if (zipTemporary is not null && File.Exists(zipTemporary)) File.Delete(zipTemporary);
            kilit.Release();
        }
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

public sealed class OtomatikYedek(IServiceScopeFactory scopes, YedekServisi yedek, ILogger<OtomatikYedek> logger) : BackgroundService
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
                if (yedek.Durum().SonYedek is not { } son || DateTimeOffset.UtcNow - son >= TimeSpan.FromDays(1))
                {
                    using var scope = scopes.CreateScope();
                    await yedek.Olustur(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Otomatik Kasa yedeği oluşturulamadı."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
