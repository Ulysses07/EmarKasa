using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// 11 · Ay kilidi ve yayını. Durum okuma her iki rol; kilitle / kilidi aç / yayınla yalnız editör
/// (ve geçmişe "Ay kilidi eklendi/silindi", "Ay yayını eklendi/güncellendi" olarak yazılır).
/// Kasa aydan aya devrettiği için kilit geriye doğru kapsar: bir ayı kilitlemek takip başlangıcından
/// o aya kadar kilitsiz ayları da kilitler, bir ayın kilidini açmak sonraki ayların kilidini de açar.
/// </summary>
public static class AyKapanisiEndpoints
{
    public static RouteGroupBuilder MapAyKapanisi(this RouteGroupBuilder api)
    {
        api.MapGet("/ay-kapanisi", (int yil, int ay, RaporServisi rapor) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string h) return UcNokta.Hata(h);
            return Results.Ok(rapor.AyKapanisi(yil, ay));
        });
        api.MapGet("/ay-kapanisi/kilitler", (KasaDbContext db) =>
            db.AyKilitleri.AsNoTracking().OrderByDescending(k => k.Ay).ToList()
                .Select(k => new AyKilidiDto(k.Ay.Year, k.Ay.Month, k.Etiket, DateTime.SpecifyKind(k.KilitZamaniUtc, DateTimeKind.Utc)))
                .ToList());

        api.MapPost("/ay-kapanisi/kilitle", (AyIstekDto dto, KasaDbContext db, RaporServisi rapor, TimeProvider saat) =>
        {
            if (UcNokta.AyHatasi(dto.Yil, dto.Ay) is string h) return UcNokta.Hata(h);
            var ay = new DateOnly(dto.Yil, dto.Ay, 1);
            var etiket = AyBicimi.Etiket(ay);
            if (!rapor.Kilitlenebilir(ay))
                return UcNokta.Hata($"{etiket} henüz bitmedi; yalnız bitmiş bir ay kilitlenebilir.");
            return UcNokta.Yaz(db, $"{etiket} zaten kilitli.", () =>
            {
                if (db.AyKilitleri.Any(k => k.Ay == ay)) return Results.Conflict(new { hata = $"{etiket} zaten kilitli." });
                // Önceki kilitsiz aylar da kilitlenir (takip başlangıcından bu yana): yoksa onlardaki bir düzeltme
                // bu ayın açılış ve kapanış kasasını değiştirirdi.
                var zaman = saat.GetUtcNow().UtcDateTime;
                var mevcut = db.AyKilitleri.Select(k => k.Ay).ToHashSet();
                var ilk = AyKilidiKurali.TakipAyi(db);
                for (var a = ilk < ay ? ilk : ay; a <= ay; a = a.AddMonths(1))
                    if (!mevcut.Contains(a))
                        db.AyKilitleri.Add(new AyKilidiEntity { Ay = a, Etiket = AyBicimi.Etiket(a), KilitZamaniUtc = zaman });
                db.SaveChanges();
                return Results.Ok(rapor.AyKapanisi(dto.Yil, dto.Ay));
            });
        }).RequireAuthorization("Editor");

        api.MapPost("/ay-kapanisi/kilit-ac", (AyIstekDto dto, KasaDbContext db, RaporServisi rapor) =>
        {
            if (UcNokta.AyHatasi(dto.Yil, dto.Ay) is string h) return UcNokta.Hata(h);
            var ay = new DateOnly(dto.Yil, dto.Ay, 1);
            return UcNokta.Yaz(db, "Kilit açılamadı; tekrar deneyin.", () =>
            {
                // Bu ay ve sonraki kilitli aylar birlikte açılır (onların kasası bu aydan devreder).
                if (!AyKilidiKurali.KilitliMi(db, ay)) return Results.Conflict(new { hata = $"{AyBicimi.Etiket(ay)} kilitli değil." });
                db.AyKilitleri.RemoveRange(db.AyKilitleri.Where(x => x.Ay >= ay).ToList());
                db.SaveChanges();
                return Results.Ok(rapor.AyKapanisi(dto.Yil, dto.Ay));
            });
        }).RequireAuthorization("Editor");

        // Yayınla: ayın bugünkü rakamlarının anlık görüntüsü. Yeniden yayınlamak görüntüyü yeniler. Yalnız
        // bitmiş ay: içinde bulunulan ayın rakamları kayıt değişmeden de değişir (ileri tarihli kayıtlar
        // tarihi gelince sayılır, geçen ayın kartsız K.K'sı ayın son döneminde düşülür).
        api.MapPost("/ay-kapanisi/yayinla", (AyIstekDto dto, KasaDbContext db, RaporServisi rapor, TimeProvider saat) =>
        {
            if (UcNokta.AyHatasi(dto.Yil, dto.Ay) is string h) return UcNokta.Hata(h);
            var ay = new DateOnly(dto.Yil, dto.Ay, 1);
            if (!rapor.Kilitlenebilir(ay))
                return UcNokta.Hata($"{AyBicimi.Etiket(ay)} henüz bitmedi; yalnız bitmiş bir ay yayınlanabilir.");
            return UcNokta.Yaz(db, "Ay yayınlanamadı; tekrar deneyin.", () =>
            {
                var anlik = JsonSerializer.Serialize(rapor.Anlik(dto.Yil, dto.Ay), RaporServisi.AnlikJson);
                var sonId = db.Degisiklikler.Max(d => (int?)d.Id) ?? 0;
                var zaman = saat.GetUtcNow().UtcDateTime;
                var e = db.AyYayinlari.FirstOrDefault(y => y.Ay == ay);
                if (e is null)
                    db.AyYayinlari.Add(new AyYayinEntity
                    {
                        Ay = ay, Etiket = AyBicimi.Etiket(ay), YayinZamaniUtc = zaman, SonDegisiklikId = sonId, AnlikJson = anlik,
                    });
                else
                {
                    e.YayinZamaniUtc = zaman; e.SonDegisiklikId = sonId; e.AnlikJson = anlik;
                }
                db.SaveChanges();
                return Results.Ok(rapor.AyKapanisi(dto.Yil, dto.Ay));
            });
        }).RequireAuthorization("Editor");
        return api;
    }
}
