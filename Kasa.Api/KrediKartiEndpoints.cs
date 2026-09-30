using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class KrediKartiEndpoints
{
    public static WebApplication MapKrediKartiEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Kredi kartları (güncel borç türetilir: açılış + harcama − ödeme)
        api.MapGet("/kredikartlari", (KasaDbContext db) =>
        {
            var bugun = db.Bugunu();
            var kartlar = db.KrediKartlari.OrderBy(k => k.Ad).ToList();
            var harcamaKayit = db.Islemler.Where(i => i.KrediKartiId != null)
                .Select(i => new { Id = i.KrediKartiId!.Value, i.Tarih, i.TutarTl })
                .ToList()
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.ToList());
            var odeme = db.KartOdemeler
                .GroupBy(o => o.KrediKartiId)
                .ToDictionary(g => g.Key, g => g.Sum(o => o.Tutar));
            // K3: gider formu yeni kredi kartı gideri için yalnız yeni takipteki ve yeni kullanıma açık kartları listeler.
            var takip = db.TakipKartlar.AsNoTracking().ToDictionary(t => t.KrediKartiId, t => t.Aktif);
            return kartlar.Select(k =>
            {
                var kh = harcamaKayit.GetValueOrDefault(k.Id);
                var h = kh?.Sum(x => x.TutarTl) ?? 0m;
                var o = odeme.GetValueOrDefault(k.Id, 0m);
                var sonKesim = KartDonem.SonKesim(k.KesimTarihi.Day, bugun);
                var guncel = k.Borc + h - o;
                // Ekstre borcu takipsiz kartın hatırlatmasıyla aynı hesaptır (EskiModelOlaylari).
                return new KrediKartiTuretilmisDto(
                    k.Id, k.Ad, k.KesimTarihi, k.SonOdemeTarihi, k.Limit,
                    Borc: k.Borc, GuncelBorc: guncel, AcilisBorc: k.Borc,
                    HarcamaToplam: h, OdemeToplam: o, EkstreBorc: EskiModelOlaylari.EkstreBorc(guncel, kh?.Select(x => (x.Tarih, x.TutarTl)) ?? [], sonKesim),
                    YeniTakip: takip.ContainsKey(k.Id), Aktif: takip.GetValueOrDefault(k.Id, true));
            }).ToList();
        });
        // Eski istemcinin kart ekleme ucu: her durumda aynı iletili 409. Gövde bağlanmaz (okunmaz): bozuk JSON ya da başka içerik
        // türü de 400/415 yerine aynı 409'u alır.
        api.MapPost("/kredikartlari", () =>
            Results.Conflict(new { hata = "Yeni kartı güncel uygulamanın Kredi Kartları ekranından oluşturun." })).RequireAuthorization("Editor");
        api.MapPut("/kredikartlari/{id:int}", (int id, KrediKartiYazDto dto, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.TakipKartlar.Any(k => k.KrediKartiId == id))
                return Results.Conflict(new { hata = "Bu kart yeni takipte; Kredi Kartları ekranından düzenleyin." });
            var e = db.KrediKartlari.Find(id);
            if (e is null)
                return Results.NotFound();
            var (gelen, hata) = KayitGirdileri.Kart(dto);
            if (hata is not null)
                return hata;
            gelen.Id = id;
            db.Entry(e).CurrentValues.SetValues(gelen);
            db.SaveChanges();
            transaction.Commit();
            return Results.Ok(e);
        }).RequireAuthorization("Editor");
        api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.TakipKartlar.Any(k => k.KrediKartiId == id))
                return Results.Conflict(new { hata = "Takip edilen kart silinemez; yeni kullanıma kapatın." });
            if (db.HesapHareketler.Any(h => h.KartOdeme != null && h.KartOdeme.KrediKartiId == id))
                return Results.Conflict(new { hata = "Hesaba bağlı ödemesi bulunan kart silinemez." });
            if (db.AlisOdemeler.Any(o => o.Islem.KrediKartiId == id))
                return Results.Conflict(new { hata = "Bu kart alış ödemelerine bağlı; ödeme bağlantısı korunmalıdır." });
            var e = db.KrediKartlari.Find(id);
            if (e is null)
                return Results.NotFound();
            // Harcama işlemlerinin bağını kopar (işlem kalır), ödemeleri sil.
            foreach (var i in db.Islemler.Where(i => i.KrediKartiId == id))
                i.KrediKartiId = null;
            db.KartOdemeler.RemoveRange(db.KartOdemeler.Where(o => o.KrediKartiId == id));
            db.KrediKartlari.Remove(e);
            db.SaveChanges();
            transaction.Commit();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return app;
    }
}
