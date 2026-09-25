using System.Security.Claims;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi);

public static class BelgeEndpoints
{
    public const int AzamiBoyut = 10 * 1024 * 1024;

    public static WebApplication MapBelgeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("Alis");
        api.MapGet("/alis/{id:int}/belgeler", (int id, ClaimsPrincipal user, KasaDbContext db) =>
        {
            if (!Sahibi(db, id, user)) return Results.NotFound();
            return Results.Ok(db.Belgeler.AsNoTracking().Where(b => b.AlisId == id)
                .OrderBy(b => b.Id).Select(b => new BelgeDto(b.Id, b.AlisId, b.OdemeId, b.DosyaAdi, b.IcerikTuru, b.Boyut, b.Yuklendi)).ToList());
        });
        api.MapPost("/alis/{id:int}/belgeler", async (int id, HttpRequest request, ClaimsPrincipal user, KasaDbContext db) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { hata = "Dosyayı form olarak gönderin." });
            if (request.ContentLength is > AzamiBoyut + 64 * 1024) return Results.StatusCode(413);
            if (!Sahibi(db, id, user)) return Results.NotFound();
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            var file = form.Files.GetFile("dosya");
            if (file is null || form.Files.Count != 1 || file.Length <= 0 || file.Length > AzamiBoyut)
                return Results.BadRequest(new { hata = "En fazla 10 MB büyüklüğünde tek PNG, JPEG veya PDF seçin." });
            int? odemeId = null;
            if (form.ContainsKey("odemeId") && !string.IsNullOrWhiteSpace(form["odemeId"]))
            {
                if (!int.TryParse(form["odemeId"], out var value) || value <= 0) return Results.BadRequest(new { hata = "Geçerli bir ödeme seçin." });
                odemeId = value;
            }
            using var content = new MemoryStream();
            await file.CopyToAsync(content, request.HttpContext.RequestAborted);
            var bytes = content.ToArray();
            var type = Tur(bytes);
            if (type is null) return Results.BadRequest(new { hata = "Yalnız PNG, JPEG veya PDF belgeleri kabul edilir." });
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).Take(180).ToArray());
            if (string.IsNullOrWhiteSpace(name)) name = "belge";
            using var tx = db.Database.BeginTransaction();
            // Yetki ve durum dosya okunurken değişmiş olabilir; yazma kilidi altında tekrar kontrol et.
            if (!Sahibi(db, id, user)) return Results.NotFound();
            if (!user.IsInRole("editor") && (odemeId is not null || !db.Alislar.Any(a => a.Id == id && a.Durum == AlisDurumlari.Taslak)))
                return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağına alış belgesi ekleyebilir." });
            if (odemeId is not null && !db.AlisOdemeler.Any(o => o.Id == odemeId && o.AlisId == id))
                return Results.BadRequest(new { hata = "Ödeme bu alışa ait değil." });
            if (db.Belgeler.Count(b => b.AlisId == id) >= 30) return Results.Conflict(new { hata = "Bir alışa en fazla 30 belge eklenebilir." });
            var belge = new BelgeEntity { AlisId = id, OdemeId = odemeId, DosyaAdi = name, IcerikTuru = type, Boyut = bytes.Length, Yuklendi = DateTimeOffset.UtcNow, Icerik = bytes };
            db.Belgeler.Add(belge); db.SaveChanges(); tx.Commit();
            return Results.Created($"/api/belgeler/{belge.Id}", new BelgeDto(belge.Id, id, odemeId, name, type, bytes.Length, belge.Yuklendi));
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AzamiBoyut + 64 * 1024));

        api.MapGet("/belgeler/{id:int}", (int id, ClaimsPrincipal user, KasaDbContext db) =>
        {
            var parent = db.Belgeler.Where(b => b.Id == id).Select(b => (int?)b.AlisId).FirstOrDefault();
            if (parent is null || !Sahibi(db, parent.Value, user)) return Results.NotFound();
            var belge = db.Belgeler.AsNoTracking().Single(b => b.Id == id);
            // Her zaman indirme; kullanıcı belgesi aynı origin'de çalıştırılamaz.
            return Results.File(belge.Icerik, belge.IcerikTuru, belge.DosyaAdi);
        });
        api.MapDelete("/belgeler/{id:int}", (int id, ClaimsPrincipal user, KasaDbContext db) =>
        {
            using var tx = db.Database.BeginTransaction();
            var b = db.Belgeler.Find(id);
            if (b is null || !Sahibi(db, b.AlisId, user)) return Results.NotFound();
            if (!user.IsInRole("editor") && (b.OdemeId is not null || !db.Alislar.Any(a => a.Id == b.AlisId && a.Durum == AlisDurumlari.Taslak)))
                return Results.Conflict(new { hata = "Alıcı yalnız kendi taslağındaki alış belgesini kaldırabilir." });
            db.Belgeler.Remove(b); db.SaveChanges(); tx.Commit();
            return Results.NoContent();
        });
        return app;
    }

    private static bool Sahibi(KasaDbContext db, int id, ClaimsPrincipal user)
    {
        if (user.IsInRole("editor")) return db.Alislar.Any(a => a.Id == id);
        return int.TryParse(user.FindFirstValue("alici_id"), out var aliciId)
            && db.Alislar.Any(a => a.Id == id && a.AliciId == aliciId);
    }
    private static string? Tur(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255) return "image/jpeg";
        if (b.Length >= 5 && b.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "application/pdf";
        return null;
    }
}
