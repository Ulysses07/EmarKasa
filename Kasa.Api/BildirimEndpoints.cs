using System.Security.Claims;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api;

public record BildirimAyarYaz(bool Etkin, int Saat, int Dakika, int Surum);
public record PushKeysYaz(string P256dh, string Auth);
public record PushAbonelikYaz(string Endpoint, PushKeysYaz Keys, string? CihazAdi, Guid CihazId);
public record PushEndpointYaz(string Endpoint);

public static class BildirimEndpoints
{
    public static IServiceCollection AddKasaBildirimleri(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PushKimligi>();
        services.AddSingleton<IPushGonderici, WebPushGonderici>();
        services.AddSingleton<IBildirimKaynaklari, FinansBildirimKaynaklari>();
        services.AddScoped<BildirimServisi>();
        services.AddHostedService<BildirimWorker>();
        return services;
    }

    public static WebApplication MapBildirimEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bildirimler").RequireAuthorization("Editor");
        group.MapGet("", async (KasaDbContext db, BildirimServisi service, CancellationToken ct) =>
        {
            await service.Yenile(ct);
            return Results.Ok(await db.Set<BildirimEntity>().AsNoTracking().Where(x => !x.Iptal || x.Okundu
                || db.Set<BildirimTeslimEntity>().Any(t => t.BildirimId == x.Id && t.Gonderildi != null))
                .OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Id).Take(200)
                .Select(x => new { x.Id, x.Baslik, x.Mesaj, x.Tarih, x.Okundu, x.Hedef, x.Tur, x.KaynakId }).ToListAsync(ct));
        });
        group.MapGet("/ayarlar", (BildirimServisi service) => Results.Ok(AyarDto(service.Ayarlar())));
        group.MapPut("/ayarlar", async (BildirimAyarYaz input, KasaDbContext db, BildirimServisi service, CancellationToken ct) =>
        {
            if (input.Saat is < 0 or > 23 || input.Dakika is < 0 or > 59 || input.Surum < 1)
                return Results.BadRequest(new { hata = "Geçerli saat ve dakika seçin." });
            _ = service.Ayarlar();
            var changed = await db.Set<BildirimAyarEntity>().Where(x => x.Id == 1 && x.Surum == input.Surum)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, input.Etkin).SetProperty(x => x.Saat, input.Saat)
                    .SetProperty(x => x.Dakika, input.Dakika).SetProperty(x => x.Surum, x => x.Surum + 1), ct);
            return changed == 1 ? Results.Ok(AyarDto(service.Ayarlar()))
                : Results.Conflict(new { hata = "Bildirim ayarları değişti. Yenileyip tekrar deneyin." });
        });
        group.MapPost("/{id:int}/okundu", async (int id, KasaDbContext db, CancellationToken ct) =>
        {
            var changed = await db.Set<BildirimEntity>().Where(x => x.Id == id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Okundu, true), ct);
            return changed == 1 ? Results.NoContent() : Results.NotFound();
        });
        group.MapGet("/push/anahtar", (PushKimligi identity) => Results.Ok(new { etkin = identity.Etkin, publicKey = identity.Get()?.PublicKey }));
        group.MapPost("/push/abonelik", async (PushAbonelikYaz input, KasaDbContext db, HttpContext context,
            TimeProvider clock, PushKimligi identity, CancellationToken ct) =>
        {
            if (!identity.Etkin) return Results.BadRequest(new { hata = "Cihaz bildirimleri bu ortamda açık değil." });
            if (!PushDogrulama.Endpoint(input.Endpoint) || input.Keys is null
                || !PushDogrulama.Anahtarlar(input.Keys.P256dh, input.Keys.Auth) || input.CihazId == Guid.Empty
                || (input.CihazAdi?.Length ?? 0) > 100)
                return Results.BadRequest(new { hata = "Geçerli bir cihaz aboneliği gönderin." });
            var stamp = context.User.FindFirstValue(OturumDamgasi.ClaimAdi);
            if (string.IsNullOrEmpty(stamp)) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var deviceId = input.CihazId.ToString("D");
            await db.Set<PushAbonelikEntity>().Where(x => x.CihazId == deviceId && x.Endpoint != input.Endpoint)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            var row = await db.Set<PushAbonelikEntity>().SingleOrDefaultAsync(x => x.Endpoint == input.Endpoint, ct);
            if ((row is null || !row.Etkin) && await db.Set<PushAbonelikEntity>().CountAsync(x => x.Etkin, ct) >= 20)
                return Results.BadRequest(new { hata = "En fazla 20 cihaz etkin olabilir. Kullanmadığın bir cihazı kaldır." });
            if (row is null)
            {
                row = new PushAbonelikEntity { Endpoint = input.Endpoint, Olusturuldu = clock.GetUtcNow().ToUnixTimeSeconds() };
                db.Add(row);
            }
            row.P256dh = input.Keys.P256dh; row.Auth = input.Keys.Auth; row.CihazId = deviceId;
            row.CihazAdi = string.IsNullOrWhiteSpace(input.CihazAdi) ? "Tarayıcı" : input.CihazAdi.Trim();
            row.OturumDamgasi = stamp; row.Etkin = true;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { row.Id, row.CihazAdi });
        }).RequireRateLimiting("guvenlik");
        group.MapDelete("/push/abonelik", async ([Microsoft.AspNetCore.Mvc.FromBody] PushEndpointYaz input, KasaDbContext db, CancellationToken ct) =>
        {
            if (!PushDogrulama.Endpoint(input.Endpoint)) return Results.BadRequest(new { hata = "Geçersiz abonelik." });
            await db.Set<PushAbonelikEntity>().Where(x => x.Endpoint == input.Endpoint)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            return Results.NoContent();
        });
        group.MapGet("/push/abonelikler", async (KasaDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Set<PushAbonelikEntity>().AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(x => new { x.Id, x.CihazAdi,
                Olusturuldu = DateTimeOffset.FromUnixTimeSeconds(x.Olusturuldu),
                SonBasarili = x.SonBasarili is { } last ? (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(last) : null, x.Etkin }));
        });
        group.MapDelete("/push/abonelikler/{id:int}", async (int id, KasaDbContext db, CancellationToken ct) =>
        {
            var changed = await db.Set<PushAbonelikEntity>().Where(x => x.Id == id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            return changed == 1 ? Results.NoContent() : Results.NotFound();
        });
        group.MapPost("/test", async (PushEndpointYaz input, KasaDbContext db, IPushGonderici sender,
            IConfiguration cfg, TimeProvider clock, CancellationToken ct) =>
        {
            if (!PushDogrulama.Endpoint(input.Endpoint)) return Results.BadRequest(new { hata = "Önce bu cihazda bildirimleri aç." });
            var row = await db.Set<PushAbonelikEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Endpoint == input.Endpoint && x.Etkin, ct);
            if (row is null || !OturumDamgasi.Esit(row.OturumDamgasi, OturumDamgasi.Uret("editor", cfg, db)))
                return Results.BadRequest(new { hata = "Önce bu cihazda bildirimleri yeniden aç." });
            var result = await sender.Gonder(row, new(0, "Emar Kasa", "Bu cihazın bildirim denemesi. Kart ve kredi hatırlatmaları burada görünecek.", "/#notifications", "kasa-test"), 60, ct);
            if (result == PushSonuc.Basarili)
                await db.Set<PushAbonelikEntity>().Where(x => x.Id == row.Id)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.SonBasarili, (long?)clock.GetUtcNow().ToUnixTimeSeconds()), ct);
            if (result == PushSonuc.AbonelikBitti)
                await db.Set<PushAbonelikEntity>().Where(x => x.Id == row.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            return Results.Ok(new { basarili = result == PushSonuc.Basarili, mesaj = result == PushSonuc.Basarili
                ? "Bildirim hizmetine iletildi. Cihazında görünüp görünmediğini kontrol et."
                : "Bildirim iletilemedi. Cihaz iznini kontrol edip yeniden dene." });
        }).RequireRateLimiting("guvenlik");
        return app;
    }

    private static object AyarDto(BildirimAyarEntity x) => new { x.Etkin, x.Saat, x.Dakika, saatDilimi = "Europe/Istanbul", x.Surum };
}
