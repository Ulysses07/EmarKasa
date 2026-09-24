using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// 05 · Kanal gelir hedefleri ve sabit gider kalemi bütçeleri. Hedef/bütçe planlama verisidir, para
/// kuralı değildir: hiçbir rapor rakamını değiştirmez ve ay kilidine tabi değildir.
/// </summary>
public static class HedefButceEndpoints
{
    public static RouteGroupBuilder MapHedefButce(this RouteGroupBuilder api)
    {
        api.MapGet("/hedef-butce", (int yil, int ay, RaporServisi rapor) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string h) return UcNokta.Hata(h);
            return Results.Ok(rapor.HedefButce(yil, ay));
        });

        // Ayın hedef/bütçelerini yazar: Tutar dolu satır eklenir/güncellenir, null satır silinir; gönderilmeyen satıra dokunulmaz.
        api.MapPut("/hedef-butce", (HedefButceYazDto dto, KasaDbContext db, RaporServisi rapor) =>
        {
            if (UcNokta.TarihHatasi(dto.Ay) is string th) return UcNokta.Hata(th);
            var hedefler = dto.Kanallar ?? [];
            var butceler = dto.Kalemler ?? [];
            if (hedefler.GroupBy(h => h.KanalId).Any(g => g.Count() > 1)) return UcNokta.Hata("Aynı kanal birden çok kez gönderildi.");
            if (butceler.GroupBy(b => b.GiderKalemiId).Any(g => g.Count() > 1)) return UcNokta.Hata("Aynı kalem birden çok kez gönderildi.");
            foreach (var h in hedefler)
                if (h.Tutar is { } t && UcNokta.TutarHatasi(t, "Gelir hedefi") is string e1) return UcNokta.Hata(e1);
            foreach (var b in butceler)
                if (b.Tutar is { } t && UcNokta.TutarHatasi(t, "Bütçe") is string e2) return UcNokta.Hata(e2);
            var ay = AyBicimi.AyBasi(dto.Ay);
            return UcNokta.Yaz(db, "Hedef/bütçe aynı anda başka bir yerden kaydedildi; tekrar deneyin.", () =>
            {
                var kanalIdleri = db.Kanallar.Select(k => k.Id).ToHashSet();
                var kalemIdleri = db.GiderKalemleri.Select(k => k.Id).ToHashSet();
                if (hedefler.FirstOrDefault(h => !kanalIdleri.Contains(h.KanalId)) is { } yokK) return UcNokta.Hata($"Kanal bulunamadı (#{yokK.KanalId}).");
                if (butceler.FirstOrDefault(b => !kalemIdleri.Contains(b.GiderKalemiId)) is { } yokB) return UcNokta.Hata($"Gider kalemi bulunamadı (#{yokB.GiderKalemiId}).");

                var mevcutH = db.KanalHedefleri.Where(h => h.Ay == ay).ToList().ToDictionary(h => h.KanalId);
                foreach (var h in hedefler)
                {
                    mevcutH.TryGetValue(h.KanalId, out var e);
                    if (h.Tutar is not { } t) { if (e is not null) db.KanalHedefleri.Remove(e); }
                    else if (e is null) db.KanalHedefleri.Add(new KanalHedefEntity { Ay = ay, KanalId = h.KanalId, GelirHedefi = t });
                    else e.GelirHedefi = t;
                }
                var mevcutB = db.GiderButceleri.Where(b => b.Ay == ay).ToList().ToDictionary(b => b.GiderKalemiId);
                foreach (var b in butceler)
                {
                    mevcutB.TryGetValue(b.GiderKalemiId, out var e);
                    if (b.Tutar is not { } t) { if (e is not null) db.GiderButceleri.Remove(e); }
                    else if (e is null) db.GiderButceleri.Add(new GiderButceEntity { Ay = ay, GiderKalemiId = b.GiderKalemiId, Tutar = t });
                    else e.Tutar = t;
                }
                db.SaveChanges();
                return Results.Ok(rapor.HedefButce(ay.Year, ay.Month));
            });
        }).RequireAuthorization("Editor");

        // Geçen ayın hedef ve bütçelerini bu aya kopyalar; bu ay zaten değeri olan satırlar korunur.
        api.MapPost("/hedef-butce/kopyala", (HedefKopyalaDto dto, KasaDbContext db) =>
        {
            if (UcNokta.TarihHatasi(dto.Ay) is string th) return UcNokta.Hata(th);
            var ay = AyBicimi.AyBasi(dto.Ay);
            var onceki = ay.AddMonths(-1);
            return UcNokta.Yaz(db, "Kopyalanamadı: bu ayın hedefleri aynı anda değişti; tekrar deneyin.", () =>
            {
                var eskiH = db.KanalHedefleri.AsNoTracking().Where(h => h.Ay == onceki).ToList();
                var eskiB = db.GiderButceleri.AsNoTracking().Where(b => b.Ay == onceki).ToList();
                if (eskiH.Count == 0 && eskiB.Count == 0)
                    return UcNokta.Hata($"{AyBicimi.Etiket(onceki)} için hedef ya da bütçe yok; kopyalanacak bir şey yok.");
                var varH = db.KanalHedefleri.Where(h => h.Ay == ay).Select(h => h.KanalId).ToHashSet();
                var varB = db.GiderButceleri.Where(b => b.Ay == ay).Select(b => b.GiderKalemiId).ToHashSet();
                int kopya = 0, atlanan = 0;
                foreach (var h in eskiH)
                {
                    if (varH.Contains(h.KanalId)) { atlanan++; continue; }
                    db.KanalHedefleri.Add(new KanalHedefEntity { Ay = ay, KanalId = h.KanalId, GelirHedefi = h.GelirHedefi });
                    kopya++;
                }
                foreach (var b in eskiB)
                {
                    if (varB.Contains(b.GiderKalemiId)) { atlanan++; continue; }
                    db.GiderButceleri.Add(new GiderButceEntity { Ay = ay, GiderKalemiId = b.GiderKalemiId, Tutar = b.Tutar });
                    kopya++;
                }
                db.SaveChanges();
                return Results.Ok(new KopyalaSonucDto(kopya, atlanan));
            });
        }).RequireAuthorization("Editor");
        return api;
    }
}
