using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class KasaKontrolEndpoints
{
    public static WebApplication MapKasaKontrolEndpoints(this WebApplication app)
    {
        app.MapGet("/api/kasa-esikleri", (KasaDbContext db, HesapServisi hesap) => AlisEndpoints.Mutate(db, () =>
        {
            var balances = hesap.Panel().Kanallar.ToDictionary(k => k.KanalId ?? 0, k => k.Bakiye);
            var limits = db.KasaEsikleri.AsNoTracking().ToDictionary(x => x.KanalId);
            return Results.Ok(db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList().Select(k =>
            {
                limits.TryGetValue(k.Id, out var limit);
                var balance = balances.GetValueOrDefault(k.Id);
                return new KasaEsikDto(k.Id, k.Ad, limit?.Surum ?? 0, limit?.Tutar ?? 0, limit?.Etkin ?? false, balance,
                    limit is { Etkin: true } && balance < limit.Tutar);
            }).ToList());
        })).RequireAuthorization("Finans");

        app.MapPut("/api/kasa-esikleri/{kanalId:int}", (int kanalId, KasaEsikYaz dto, KasaDbContext db, HesapServisi hesap) => AlisEndpoints.Mutate(db, () =>
        {
            var channel = db.Kanallar.Find(kanalId);
            if (channel is null) return Results.NotFound();
            var v = new GirdiDogrulama(); v.Para(dto.Tutar, "tutar");
            if (v.Sonuc() is { } error) return error;
            var limit = db.KasaEsikleri.SingleOrDefault(x => x.KanalId == kanalId);
            if (dto.Surum != (limit?.Surum ?? 0)) return AlisEndpoints.Conflict("Alt sınır değişmiş. Yenileyip tekrar deneyin.");
            if (limit is null) { limit = new() { KanalId = kanalId, Surum = 0 }; db.KasaEsikleri.Add(limit); }
            // Bir eşik değişikliği mevcut düşük bakiye olayını tekrar tekrar üretmez.
            if (!dto.Etkin) { limit.AlarmAcik = false; limit.UyariTarihi = null; }
            limit.Tutar = dto.Tutar; limit.Etkin = dto.Etkin; limit.Surum++;
            db.SaveChanges();
            var balance = hesap.Panel().Kanallar.Single(k => k.KanalId == kanalId).Bakiye;
            return Results.Ok(new KasaEsikDto(kanalId, channel.Ad, limit.Surum, limit.Tutar, limit.Etkin, balance, limit.Etkin && balance < limit.Tutar));
        })).RequireAuthorization("Editor");

        app.MapGet("/api/kasa-kontrol", (KasaDbContext db) => db.KasaKontrolleri.AsNoTracking().OrderByDescending(x => x.Id).Take(50).ToList().Select(ToDto))
            .RequireAuthorization("Finans");
        app.MapPost("/api/kasa-kontrol/onizleme", (KasaKontrolOnizle dto, KasaDbContext db, HesapServisi hesap) => AlisEndpoints.Mutate(db, () =>
        {
            if (Validate(dto.GercekBakiye, dto.Not) is { } error) return error;
            return Results.Ok(Preview(hesap.Panel().GuncelKasa, dto.GercekBakiye, dto.Not));
        })).RequireAuthorization("Editor");
        app.MapPost("/api/kasa-kontrol", (KasaKontrolYaz dto, KasaDbContext db, HesapServisi hesap) => AlisEndpoints.Mutate(db, () =>
        {
            if (Validate(dto.GercekBakiye, dto.Not) is { } error) return error;
            var digest = FinansHesaplari.Ozet(dto with { IstekId = Guid.Empty });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "KasaKontrol", digest, id => Results.Ok(ToDto(db.KasaKontrolleri.Single(x => x.Id == id)))) is { } replay) return replay;
            var preview = Preview(hesap.Panel().GuncelKasa, dto.GercekBakiye, dto.Not);
            if (dto.KontrolOzeti != preview.KontrolOzeti) return AlisEndpoints.Conflict("Kasa bakiyesi veya karşılaştırma bilgileri değişti. Yeniden karşılaştırın.");
            var row = new KasaKontrolEntity { Kaydedildi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SistemBakiye = preview.SistemBakiye, GercekBakiye = dto.GercekBakiye, Fark = preview.Fark, Not = dto.Not?.Trim() };
            db.KasaKontrolleri.Add(row); db.SaveChanges();
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "KasaKontrol", digest, row.Id); db.SaveChanges();
            return Results.Ok(ToDto(row));
        })).RequireAuthorization("Editor");
        return app;
    }

    private static IResult? Validate(decimal actual, string? note)
    { var v = new GirdiDogrulama(); v.Para(actual, "gercekBakiye", negatifOlabilir: true); v.Metin(note, "not", 2000, zorunlu: false); return v.Sonuc(); }
    private static KasaKontrolOnizlemeDto Preview(decimal system, decimal actual, string? note) => new(system, actual, actual - system,
        FinansHesaplari.Ozet(new { system, actual, note = note?.Trim() }));
    private static KasaKontrolDto ToDto(KasaKontrolEntity row) => new(row.Id, DateTimeOffset.FromUnixTimeMilliseconds(row.Kaydedildi), row.SistemBakiye, row.GercekBakiye, row.Fark, row.Not);
}
