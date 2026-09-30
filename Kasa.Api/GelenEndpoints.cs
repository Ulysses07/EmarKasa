using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class GelenEndpoints
{
    public static WebApplication MapGelenEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Yeni gelirlerde dönem+kanal başına tek satır; eski yinelenen gruplar salt okunur.
        api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
        {
            var q = db.Gelenler.AsQueryable();
            if (donemStart is { } d)
                q = q.Where(g => g.DonemStart == d);
            return q.ToList();
        });
        api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db) =>
        {
            var v = new GirdiDogrulama();
            v.Tarih(dto.DonemStart, "donemStart");
            v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true);
            var kanal = v.Kanal(db, dto.Kanal, ortakOlabilir: false);
            var takipBaslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
            v.Kontrol(dto.DonemStart >= takipBaslangic
                      && (dto.DonemStart == takipBaslangic || dto.DonemStart.Day == 1 || dto.DonemStart.DayOfWeek == DayOfWeek.Monday),
                "donemStart", "Gelir için takip başlangıcından itibaren geçerli bir dönem başlangıcı seçin.");
            if (v.Sonuc() is { } hata)
                return hata;
            if (db.Gelenler.Any(g => g.EskiYinelenenGrup && g.DonemStart == dto.DonemStart
                && (g.KanalId == kanal!.Id || g.Kanal == kanal.Ad)))
                return Results.Conflict(new { hata = "Bu dönem ve kanalda birden fazla eski gelir kaydı var. Bütün kayıtlar tutarlarıyla korunur; bu eski grup salt okunurdur. Yeni dönemlere gelir girebilirsiniz." });
            // Tek SQL ifadesi: eşzamanlı ilk girişler çift gelir kaydı üretemez. Ham SQL SaveChanges kancasından geçmez: denetim
            // olayı açıkça ve upsert'le aynı (ertelenmiş) transaction'da yazılır; yazma kilidini autocommit'teki gibi upsert alır.
            // contract-6: sürüm de burada artar. Yeni satır 1 ile eklenir: satırı görmeden (0) kaydeden istemci, arada eklenmiş satırın
            // üzerine yazamaz. Sürüm gönderilmediyse (eski istemci) koşul yoktur; son yazan kazanır.
            var onceki = db.Gelenler.AsNoTracking().SingleOrDefault(g => g.DonemStart == dto.DonemStart && g.KanalId == kanal!.Id && !g.EskiYinelenenGrup);
            using var transaction = KancaDisiOlaylar.ErteliTransaction(db);
            var affected = db.Database.ExecuteSqlInterpolated($"""
                INSERT INTO "Gelenler" ("DonemStart", "Kanal", "KanalId", "TutarTl", "Surum")
                VALUES ({dto.DonemStart}, {kanal!.Ad}, {kanal.Id}, {dto.TutarTl}, 1)
                ON CONFLICT ("DonemStart", "KanalId") WHERE "EskiYinelenenGrup" = 0
                DO UPDATE SET "TutarTl" = excluded."TutarTl", "Kanal" = excluded."Kanal", "Surum" = "Gelenler"."Surum" + 1
                WHERE NOT EXISTS (SELECT 1 FROM "HesapHareketler" h WHERE h."GelenId" = "Gelenler"."Id")
                  AND ({dto.Surum} IS NULL OR "Gelenler"."Surum" = {dto.Surum})
                """);
            var e = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == dto.DonemStart && g.KanalId == kanal.Id);
            if (affected == 0)
            {
                if (!db.HesapHareketler.Any(h => h.GelenId == e.Id))
                    return Results.Conflict(new { hata = CekirdekSurum.GelenIletisi });
                if (e.TutarTl != dto.TutarTl)
                    return Results.Conflict(new { hata = "Hesaba bağlı gelir tutarı buradan değiştirilemez." });
            }
            if (affected > 0)
                KancaDisiOlaylar.GelenUpsert(db, onceki, e);
            transaction.Commit();
            return Results.Ok(e);
        }).RequireAuthorization("Editor");
        return app;
    }
}
