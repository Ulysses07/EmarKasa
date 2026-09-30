using Kasa.Api.Data;

namespace Kasa.Api;

public static class KartOdemeEndpoints
{
    public static WebApplication MapKartOdemeEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Kart ödemeleri (borç-only; kasa motoruna girmez)
        api.MapGet("/kartodemeler", (int? krediKartiId, KasaDbContext db) =>
        {
            var q = db.KartOdemeler.AsQueryable();
            if (krediKartiId is { } id)
                q = q.Where(o => o.KrediKartiId == id);
            return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
        });
        api.MapPost("/kartodemeler", (KartOdemeYazDto dto, KasaDbContext db) =>
        {
            if (db.TakipKartlar.Any(k => k.KrediKartiId == dto.KrediKartiId))
                return Results.Conflict(new { hata = "Yeni takipteki kartın ödemesini Kredi Kartları ekranından kaydedin." });
            var (e, hata) = KayitGirdileri.KartOdeme(dto, db);
            if (hata is not null)
                return hata;
            db.KartOdemeler.Add(e);
            db.SaveChanges();
            return Results.Created($"/api/kartodemeler/{e.Id}", e);
        }).RequireAuthorization("Editor");
        api.MapDelete("/kartodemeler/{id:int}", (int id, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            var e = db.KartOdemeler.Find(id);
            if (e is null)
                return Results.NotFound();
            if (db.TakipKartlar.Any(k => k.KrediKartiId == e.KrediKartiId))
                return Results.Conflict(new { hata = "Geçişi yapılmış kartın eski ödemeleri korunur." });
            if (db.HesapHareketler.Any(h => h.KartOdemeId == id))
                return Results.Conflict(new { hata = "Hesaba bağlı kart ödemesi silinemez." });
            db.KartOdemeler.Remove(e);
            db.SaveChanges();
            transaction.Commit();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return app;
    }
}
