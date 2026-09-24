using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket D — kart ekstresi mutabakatı (özellik 34). Bankanın ekstre tutarı, uygulamanın kesim günündeki
/// kart borcuyla (<see cref="KartHesap.Durum"/>, kart sayfasındaki hesabın aynısı) karşılaştırılır.
/// Mutabakat yalnız kayıt tutar: kart borcunu ve kasayı değiştirmez. Okuma her iki rol, yazma editör.
/// </summary>
public static class KartMutabakatEndpoints
{
    /// <summary>Dönem listesinin varsayılan ve en büyük uzunluğu.</summary>
    public const int VarsayilanDonem = 12, EnFazlaDonem = 36;

    public static RouteGroupBuilder MapKartMutabakat(this RouteGroupBuilder api, YazIslemi yaz)
    {
        api.MapGet("/kartmutabakat/donemler", (int krediKartiId, int? adet, KasaDbContext db, TimeProvider saat) =>
        {
            if (adet is < 1 or > EnFazlaDonem) return Yanit.Hata($"adet 1 ile {EnFazlaDonem} arasında olmalı.");
            var k = db.KrediKartlari.AsNoTracking().FirstOrDefault(x => x.Id == krediKartiId);
            if (k is null) return Results.NotFound();
            var (harcamalar, odemeler) = Hareketler(db, k.Id);
            var kayitlar = db.KartMutabakatlari.AsNoTracking().Where(m => m.KrediKartiId == k.Id).ToList()
                .ToDictionary(m => m.DonemBitis);
            var kh = harcamalar.Select(h => new KartHarcama(h.Tarih, h.TutarTl)).ToList();
            var ko = odemeler.Select(o => new KartOdeme(o.Tarih, o.Tutar)).ToList();
            var sonuc = KartMutabakat.KapanmisDonemler(k.KesimTarihi.Day, Saat.Bugun(saat), adet ?? VarsayilanDonem)
                .Select(d =>
                {
                    var borc = KartHesap.Durum(k.Borc, kh, ko, k.KesimTarihi.Day, d.Kesim).GuncelBorc;
                    kayitlar.TryGetValue(d.Kesim, out var m);
                    decimal? fark = m is null ? null : m.EkstreTutari - borc;
                    return new KartDonemDto(d.Baslangic, d.Kesim, KartDonem.SonOdeme(d.Kesim, k.SonOdemeTarihi.Day), borc,
                        m?.Id, m?.EkstreTutari, fark, m is null ? null : CanliDurum(m, fark!.Value));
                })
                .ToList();
            return Results.Ok(sonuc);
        });

        api.MapGet("/kartmutabakat", (int krediKartiId, DateOnly kesim, KasaDbContext db, TimeProvider saat) =>
        {
            var k = db.KrediKartlari.AsNoTracking().FirstOrDefault(x => x.Id == krediKartiId);
            if (k is null) return Results.NotFound();
            if (DonemHatasi(k, kesim, Saat.Bugun(saat), out var donem) is string h) return Yanit.Hata(h);
            var m = db.KartMutabakatlari.AsNoTracking().FirstOrDefault(x => x.KrediKartiId == k.Id && x.DonemBitis == kesim);
            return Results.Ok(Detay(db, k, donem, m));
        });

        // Kart + kesim başına tek kayıt: varsa güncellenir.
        api.MapPut("/kartmutabakat", (KartMutabakatYazDto dto, KasaDbContext db, TimeProvider saat) =>
            yaz(db, "Mutabakat aynı anda başka bir yerden kaydedildi; tekrar deneyin.", () =>
        {
            var k = db.KrediKartlari.AsNoTracking().FirstOrDefault(x => x.Id == dto.KrediKartiId);
            if (k is null) return Yanit.Hata("Kredi kartı bulunamadı.");
            if (DonemHatasi(k, dto.Kesim, Saat.Bugun(saat), out var donem) is string h) return Yanit.Hata(h);
            if (Math.Abs(dto.EkstreTutari) > 100_000_000_000m) return Yanit.Hata("Ekstre tutarı çok büyük (en fazla 100.000.000.000).");
            if (decimal.Round(dto.EkstreTutari, 2) != dto.EkstreTutari) return Yanit.Hata("Ekstre tutarı en fazla 2 ondalık basamak içerebilir.");
            var not = string.IsNullOrWhiteSpace(dto.Not) ? null : dto.Not.Trim();
            if (not is { Length: > 1000 }) return Yanit.Hata("Not en fazla 1000 karakter olabilir.");

            var (harcamalar, odemeler) = Hareketler(db, k.Id);
            var donemIdleri = harcamalar.Where(x => x.Tarih >= donem.Baslangic && x.Tarih <= donem.Kesim).Select(x => x.Id).ToHashSet();
            var tikli = (dto.TikliIslemIdleri ?? []).Distinct().ToList();
            if (tikli.Any(id => !donemIdleri.Contains(id)))
                return Yanit.Hata("İşaretlenen işlemlerden biri bu dönemin kart harcamalarından değil; sayfayı yenileyin.");
            var borc = KartHesap.Durum(k.Borc, harcamalar.Select(x => new KartHarcama(x.Tarih, x.TutarTl)).ToList(),
                odemeler.Select(o => new KartOdeme(o.Tarih, o.Tutar)).ToList(), k.KesimTarihi.Day, donem.Kesim).GuncelBorc;
            var fark = dto.EkstreTutari - borc;

            var m = db.KartMutabakatlari.FirstOrDefault(x => x.KrediKartiId == k.Id && x.DonemBitis == dto.Kesim);
            if (m is null)
            {
                m = new KartMutabakatEntity { KrediKartiId = k.Id, DonemBitis = donem.Kesim };
                db.KartMutabakatlari.Add(m);
            }
            m.DonemBaslangic = donem.Baslangic;
            m.EkstreTutari = dto.EkstreTutari;
            m.HesaplananBorc = borc;
            m.TikliIslemIdleri = tikli.Count == 0 ? null : string.Join(",", tikli.Order());
            m.Not = not;
            m.Durum = fark == 0 ? KartMutabakatDurumu.Mutabik : dto.FarkKabul ? KartMutabakatDurumu.FarkKabul : KartMutabakatDurumu.Acik;
            m.KayitZamaniUtc = saat.GetUtcNow().UtcDateTime;
            db.SaveChanges();
            return Results.Ok(Detay(db, k, donem, m));
        })).RequireAuthorization("Editor");

        api.MapDelete("/kartmutabakat/{id:int}", (int id, KasaDbContext db) =>
        {
            var m = db.KartMutabakatlari.Find(id);
            if (m is null) return Results.NotFound();
            db.KartMutabakatlari.Remove(m); db.SaveChanges();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return api;
    }

    private sealed record Harcama(int Id, DateOnly Tarih, decimal TutarTl, string Cari, string? Not);

    private static (List<Harcama> Harcamalar, List<KartOdemeEntity> Odemeler) Hareketler(KasaDbContext db, int kartId)
        => (db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == kartId)
                .Select(i => new Harcama(i.Id, i.Tarih, i.TutarTl, i.Cari, i.Not)).ToList(),
            db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == kartId).ToList());

    /// <summary>Kesim kartın kesim gününe denk gelmeli ve dönem kapanmış (kesim ≤ bugün) olmalı.</summary>
    private static string? DonemHatasi(KrediKartiEntity k, DateOnly kesim, DateOnly bugun, out KartEkstreDonemi donem)
    {
        donem = null!;
        if (kesim > bugun) return "Bu ekstre dönemi henüz kapanmadı.";
        if (kesim < new DateOnly(2000, 1, 1)) return "Kesim tarihi 2000'den önce olamaz.";
        if (KartMutabakat.Donem(k.KesimTarihi.Day, kesim) is not { } d)
            return $"Kesim tarihi kartın kesim gününe ({k.KesimTarihi.Day}) denk gelmiyor.";
        donem = d;
        return null;
    }

    /// <summary>Bugünkü farka göre durum: fark sıfırsa mutabık; değilse kayıttaki "fark kabul" korunur.</summary>
    private static KartMutabakatDurumu CanliDurum(KartMutabakatEntity m, decimal fark)
        => fark == 0 ? KartMutabakatDurumu.Mutabik
            : m.Durum == KartMutabakatDurumu.FarkKabul ? KartMutabakatDurumu.FarkKabul : KartMutabakatDurumu.Acik;

    private static KartMutabakatDetayDto Detay(KasaDbContext db, KrediKartiEntity k, KartEkstreDonemi donem, KartMutabakatEntity? m)
    {
        var (harcamalar, odemeler) = Hareketler(db, k.Id);
        var ozet = KartMutabakat.Ozet(k.Borc,
            harcamalar.Select(h => new KartHarcama(h.Tarih, h.TutarTl)).ToList(),
            odemeler.Select(o => new KartOdeme(o.Tarih, o.Tutar)).ToList(), k.KesimTarihi.Day, donem);
        var tikli = (m?.TikliIslemIdleri ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var i) ? i : 0).ToHashSet();
        var islemler = harcamalar.Where(h => h.Tarih >= donem.Baslangic && h.Tarih <= donem.Kesim)
            .OrderBy(h => h.Tarih).ThenBy(h => h.Id)
            .Select(h => new KartMutabakatIslemDto(h.Id, h.Tarih, h.Cari, h.TutarTl, h.Not, tikli.Contains(h.Id)))
            .ToList();
        var donemOdemeleri = odemeler.Where(o => o.Tarih >= donem.Baslangic && o.Tarih <= donem.Kesim)
            .OrderBy(o => o.Tarih).ThenBy(o => o.Id)
            .Select(o => new KartMutabakatOdemeDto(o.Id, o.Tarih, o.Tutar, o.Not)).ToList();
        decimal? fark = m is null ? null : m.EkstreTutari - ozet.DonemSonuBorc;
        return new KartMutabakatDetayDto(k.Id, k.Ad, donem.Baslangic, donem.Kesim,
            KartDonem.SonOdeme(donem.Kesim, k.SonOdemeTarihi.Day),
            ozet.DevredenBorc, ozet.DonemHarcama, ozet.DonemOdeme, ozet.DonemSonuBorc,
            islemler, donemOdemeleri, m?.Id, m?.EkstreTutari, fark,
            m is null ? null : islemler.Where(i => !i.Tikli).Sum(i => i.Tutar),
            m?.Not, m is null ? null : CanliDurum(m, fark!.Value), m?.HesaplananBorc);
    }
}
