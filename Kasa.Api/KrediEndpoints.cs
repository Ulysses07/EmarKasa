using Kasa.Api.Data;

namespace Kasa.Api;

public static class KrediEndpoints
{
    public static WebApplication MapKrediEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Krediler (banka kredileri)
        api.MapGet("/krediler", (KasaDbContext db) => db.Krediler.ToList());
        // Eski istemcinin kredi ekleme ucu: her durumda aynı iletili 409. Gövde bağlanmaz (okunmaz): bozuk JSON ya da başka içerik
        // türü de 400/415 yerine aynı 409'u alır.
        api.MapPost("/krediler", () =>
            Results.Conflict(new { hata = "Yeni krediyi güncel uygulamanın Krediler ekranından oluşturun." })).RequireAuthorization("Editor");
        api.MapPut("/krediler/{id:int}", (int id, KrediYazDto dto, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.TakipKrediler.Any(k => k.KrediId == id))
                return Results.Conflict(new { hata = "Bu kredi yeni takipte; Krediler ekranından düzenleyin." });
            var e = db.Krediler.Find(id);
            if (e is null)
                return Results.NotFound();
            if (db.KrediTaksitOdemeler.Any(o => o.KrediId == id) || db.HesapHareketler.Any(h => h.KrediId == id))
                return Results.Conflict(new { hata = "Ödemesi veya hesap bağlantısı bulunan kredi değiştirilemez." });
            var (gelen, hata) = KayitGirdileri.Kredi(dto, db);
            if (hata is not null)
                return hata;
            // gT6: geçmiş kasa etkisi olan eski kredinin yalnız adı düzeltilir; etkisi olmayan kredi geçmişe taşınamaz.
            if (FinansHesaplari.EskiKrediDuzeltmeHatasi(db, e, gelen, db.Bugunu()) is { } koruma)
                return Results.Conflict(new { hata = koruma });
            gelen.Id = id;
            gelen.GerceklesmeTakibi = e.GerceklesmeTakibi;
            db.Entry(e).CurrentValues.SetValues(gelen);
            db.SaveChanges();
            transaction.Commit();
            return Results.Ok(e);
        }).RequireAuthorization("Editor");
        api.MapDelete("/krediler/{id:int}", (int id, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.TakipKrediler.Any(k => k.KrediId == id))
                return Results.Conflict(new { hata = "Takip edilen kredi silinemez; arşivleyin." });
            var e = db.Krediler.Find(id);
            if (e is null)
                return Results.NotFound();
            if (db.KrediTaksitOdemeler.Any(o => o.KrediId == id) || db.HesapHareketler.Any(h => h.KrediId == id))
                return Results.Conflict(new { hata = "Ödemesi veya hesap bağlantısı bulunan kredi silinemez." });
            // gT6: çekimi ve taksitleri bellekte türetildiğinden silme bütün geçmiş raporları yeniden yazardı.
            if (FinansHesaplari.EskiKrediGecmisEtkili(e, db.Bugunu()))
                return Results.Conflict(new { hata = "Geçmiş kasa etkisi olan eski kredi silinemez; geçmiş raporlar korunur. Krediyi Krediler ekranında yeni takibe geçirip arşivleyebilirsiniz." });
            db.Krediler.Remove(e);
            db.SaveChanges();
            transaction.Commit();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return app;
    }
}
