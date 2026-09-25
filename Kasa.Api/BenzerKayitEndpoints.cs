using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public record BenzerKayitSorgu(string Tur, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, string? Kanal = null, int? AlisId = null);
public record BenzerKayitDto(string Kaynak, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, int? AlisId = null);

/// <summary>Benzerlik bir uyarıdır; kayıt oluşturmaz ve meşru ikinci işlemi yasaklamaz.</summary>
public static class BenzerKayitEndpoints
{
    public static WebApplication MapBenzerKayitEndpoints(this WebApplication app)
    {
        // Tutar ve açıklamalar URL/erişim günlüğüne girmesin. POST yalnız okur.
        app.MapPost("/api/islemler/benzerlik", (BenzerKayitSorgu dto, KasaDbContext db) =>
        {
            var v = new GirdiDogrulama();
            v.Kontrol(dto.Tur is "Gider" or "AlisOdeme" or "KartHarcama" or "KartOdeme", "tur", "Geçerli bir işlem türü seçin.");
            v.Tarih(dto.Tarih, "tarih");
            v.Para(dto.Tutar, "tutar", negatifOlabilir: true);
            v.Kontrol(dto.KrediKartiId is null || db.KrediKartlari.Any(k => k.Id == dto.KrediKartiId), "krediKartiId", "Kart bulunamadı.");
            v.Kontrol(dto.Tur is not ("KartHarcama" or "KartOdeme") || dto.KrediKartiId is > 0, "krediKartiId", "Kart seçin.");
            v.Metin(dto.Kanal, "kanal", zorunlu: dto.Tur == "Gider" && dto.KrediKartiId is null);
            v.Kontrol(dto.Tur != "AlisOdeme" || dto.AlisId is > 0, "alisId", "Alış seçin.");
            if (v.Sonuc() is { } error) return error;

            using var snapshot = db.Database.BeginTransaction();
            var purchase = dto.Tur == "AlisOdeme" ? AlisEndpoints.Query(db).AsNoTracking().SingleOrDefault(a => a.Id == dto.AlisId) : null;
            if (dto.Tur == "AlisOdeme" && purchase is null) return Results.NotFound();
            if (dto.Tur == "Gider" && dto.KrediKartiId is null && dto.Kanal != Kanallar.Ortak && !db.Kanallar.Any(k => k.Ad == dto.Kanal))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kanal"] = ["Kayıtlı bir kanal seçin."] });

            var result = dto.Tur == "KartOdeme" ? KartOdemeleri(db, dto) : Harcamalar(db, dto, purchase);
            snapshot.Commit();
            return Results.Ok(result);
        }).RequireAuthorization("Editor");
        return app;
    }

    private static List<BenzerKayitDto> KartOdemeleri(KasaDbContext db, BenzerKayitSorgu dto)
    {
        var result = db.TakipKartOdemeler.AsNoTracking()
            .Where(p => p.KrediKartiId == dto.KrediKartiId && p.Tarih == dto.Tarih && p.Tutar == dto.Tutar && !p.Iptal)
            .OrderByDescending(p => p.Id).Take(10).ToList()
            .Select(p => new BenzerKayitDto("KartOdeme", p.Id, p.Tarih, p.Tutar, p.Not ?? "Kart ödemesi", p.KrediKartiId)).ToList();
        if (result.Count < 10)
            result.AddRange(db.KartOdemeler.AsNoTracking()
                .Where(p => p.KrediKartiId == dto.KrediKartiId && p.Tarih == dto.Tarih && p.Tutar == dto.Tutar)
                .OrderByDescending(p => p.Id).Take(10 - result.Count).ToList()
                .Select(p => new BenzerKayitDto("EskiKartOdeme", p.Id, p.Tarih, p.Tutar, p.Not ?? "Eski kart ödemesi", p.KrediKartiId)));
        return result;
    }

    private static List<BenzerKayitDto> Harcamalar(KasaDbContext db, BenzerKayitSorgu dto, AlisEntity? purchase)
    {
        var expenses = db.Islemler.AsNoTracking()
            .Where(i => i.Tarih == dto.Tarih && i.TutarTl == dto.Tutar && i.KrediKartiId == dto.KrediKartiId)
            .OrderByDescending(i => i.Id).ToList();
        var ids = expenses.Select(i => i.Id).ToArray();
        var purchases = AlisEndpoints.Query(db).AsNoTracking().Where(a => a.Odemeler.Any(o => ids.Contains(o.IslemId))).ToList();
        var source = purchases.SelectMany(a => a.Odemeler.Select(o => (o.IslemId, Alis: a))).ToDictionary(x => x.IslemId, x => x.Alis);
        var shares = purchases.ToDictionary(a => a.Id, AlisHesaplari.OdemeDagilimlari);
        var selectedId = dto.Kanal is null ? null : db.Kanallar.Where(k => k.Ad == dto.Kanal).Select(k => (int?)k.Id).SingleOrDefault();
        var purchaseChannels = purchase?.Durum == AlisDurumlari.Onaylandi ? AlisHesaplari.KanalPaylari(purchase).Select(p => p.KanalId).ToHashSet() : [];

        bool Matches(IslemEntity item)
        {
            if (dto.KrediKartiId is not null) return true;
            source.TryGetValue(item.Id, out var linked);
            if (purchase is not null && linked?.Id == purchase.Id) return true;
            var channelIds = linked is null
                ? item.KanalId is { } id ? new HashSet<int> { id } : []
                : shares[linked.Id].TryGetValue(item.Id, out var paylar) ? paylar.Where(p => p.Tutar > 0).Select(p => p.KanalId).ToHashSet() : [];
            if (purchase is not null) return purchaseChannels.Overlaps(channelIds);
            if (selectedId is { } channel) return channelIds.Contains(channel) || linked is null && item.KanalId is null && item.Kanal == dto.Kanal;
            return linked is null && item.Kanal == Kanallar.Ortak;
        }

        var result = expenses.Where(Matches).Take(10).Select(i => new BenzerKayitDto("Islem", i.Id, i.Tarih, i.TutarTl,
            i.Cari, i.KrediKartiId, source.GetValueOrDefault(i.Id)?.Id)).ToList();
        if (dto.KrediKartiId is not null && result.Count < 10)
        {
            // Kaynak gider zaten yukarıda vardır; aynı kaydı takip tablosundan ikinci kez göstermeyiz.
            result.AddRange(db.TakipHarcamalar.AsNoTracking()
                .Where(h => h.KrediKartiId == dto.KrediKartiId && h.Tarih == dto.Tarih && h.Tutar == dto.Tutar && !h.Iptal && h.IslemId == null)
                .OrderByDescending(h => h.Id).Take(10 - result.Count).ToList()
                .Select(h => new BenzerKayitDto("KartHarcama", h.Id, h.Tarih, h.Tutar, h.Aciklama, h.KrediKartiId)));
        }
        return result;
    }
}
