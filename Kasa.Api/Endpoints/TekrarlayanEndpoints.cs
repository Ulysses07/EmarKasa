using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket D — tekrarlayan giderlerin ikinci adımı (özellik 33): "Bu ay atla" kararını geri alma,
/// atlanan ayların listesi ve hazır vergi/prim şablonları. Onay/atla/şablon uçları Program.cs'tedir.
/// </summary>
public static class TekrarlayanEndpoints
{
    public static RouteGroupBuilder MapTekrarlayanEkleri(this RouteGroupBuilder api, YazIslemi yaz,
        TekrarlayanAyDogrulama ayHatasi, KalemBulucu kayitliKalem)
    {
        // Bekleyen listesinin penceresindeki (bu ay + önceki 2 ay) atlanan aylar, en yeni önce.
        api.MapGet("/tekrarlayangiderler/atlananlar", (KasaDbContext db, TimeProvider saat) =>
        {
            var enErken = TekrarlayanTakvim.AyBasi(Saat.Bugun(saat)).AddMonths(-TekrarlayanTakvim.GeriyeAy);
            var sablonlar = db.TekrarlayanGiderler.AsNoTracking().ToDictionary(t => t.Id);
            return db.TekrarlayanGirisler.AsNoTracking()
                .Where(g => g.Durum == TekrarlayanDurum.Atlandi && g.Ay >= enErken)
                .ToList()
                .Where(g => sablonlar.ContainsKey(g.TekrarlayanGiderId))
                .Select(g =>
                {
                    var t = sablonlar[g.TekrarlayanGiderId];
                    return new TekrarlayanAtlananDto(t.Id, t.Kalem, t.Kanal, g.Ay, TekrarlayanTakvim.Vade(g.Ay, t.AyinGunu));
                })
                .OrderByDescending(a => a.Ay).ThenBy(a => a.Kalem, Metin.Sirala).ThenBy(a => a.TekrarlayanGiderId)
                .ToList();
        });

        // "Bu ay atla"yı geri alır: karar silinir (geçmişe yazılır), ay yeniden bekleyene döner.
        api.MapPost("/tekrarlayangiderler/{id:int}/atlamayi-geri-al", (int id, TekrarlayanAtlaDto dto, KasaDbContext db, TimeProvider saat) =>
            yaz(db, "Karar aynı anda değişti; tekrar deneyin.", () =>
        {
            var t = db.TekrarlayanGiderler.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (t is null) return Results.NotFound();
            var bugun = Saat.Bugun(saat);
            if (ayHatasi(t, dto.Ay, bugun) is string ah) return Yanit.Hata(ah);
            var ay = TekrarlayanTakvim.AyBasi(dto.Ay);
            if (ay < TekrarlayanTakvim.AyBasi(bugun).AddMonths(-TekrarlayanTakvim.GeriyeAy))
                return Yanit.Hata("Yalnız bu ay ve önceki 2 ayın atlama kararları geri alınabilir.");
            var g = db.TekrarlayanGirisler.FirstOrDefault(x => x.TekrarlayanGiderId == id && x.Ay == ay);
            if (g is null) return Results.NotFound(new { hata = "Bu ay için atlama kararı yok." });
            if (g.Durum != TekrarlayanDurum.Atlandi)
                return Yanit.Cakisma("Bu ay girildi, atlanmadı; geri almak için oluşan işlemi silin.");
            db.TekrarlayanGirisler.Remove(g);
            db.SaveChanges();
            return Results.NoContent();
        })).RequireAuthorization("Editor");

        // Hazır vergi/prim şablonları (tarihler genel takvimdir; muhasebeciyle doğrulanmalı).
        api.MapGet("/tekrarlayangiderler/hazir", (KasaDbContext db) =>
        {
            var kalemler = db.TekrarlayanGiderler.AsNoTracking().Where(t => t.KrediKartiId == null).Select(t => t.Kalem).ToList();
            return TekrarlayanHazirlar.Liste
                .Select(h => new TekrarlayanHazirDto(h.Kod, h.Ad, h.Aciklama,
                    kalemler.Any(k => Metin.EsitBuyukKucukDuyarsiz.Equals(k, h.Ad))))
                .ToList();
        });
        api.MapPost("/tekrarlayangiderler/hazir", (TekrarlayanHazirYazDto dto, KasaDbContext db, TimeProvider saat) =>
            yaz(db, "Hazır şablon eklenemedi; tekrar deneyin.", () =>
        {
            var h = TekrarlayanHazirlar.Liste.FirstOrDefault(x => string.Equals(x.Kod, dto.Kod?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (h is null) return Yanit.Hata("Böyle bir hazır şablon yok.");
            var kalemler = db.TekrarlayanGiderler.AsNoTracking().Where(t => t.KrediKartiId == null).Select(t => t.Kalem).ToList();
            if (kalemler.Any(k => Metin.EsitBuyukKucukDuyarsiz.Equals(k, h.Ad)))
                return Yanit.Cakisma($"'{h.Ad}' için zaten bir tekrarlayan gider var.");
            // Gider kalemi yoksa eklenir (sabit gider işleminin adı bu listeden gelir).
            var kalem = kayitliKalem(db, h.Ad);
            if (kalem is null)
            {
                db.GiderKalemleri.Add(new GiderKalemiEntity { Ad = h.Ad });
                db.SaveChanges();
                kalem = h.Ad;
            }
            var bugun = Saat.Bugun(saat);
            var eklenen = h.Kalemler.Select(k => new TekrarlayanGiderEntity
            {
                Kalem = kalem, Kanal = Kasa.Core.Kanallar.Ortak, Tutar = 0m, AyinGunu = k.Gun, Aktif = true,
                Siklik = k.Siklik, TutarDegisken = true,
                BaslangicAyi = TekrarlayanHazirlar.IlkAy(k, bugun),
            }).ToList();
            db.TekrarlayanGiderler.AddRange(eklenen);
            db.SaveChanges();
            return Results.Created("/api/tekrarlayangiderler", eklenen);
        })).RequireAuthorization("Editor");
        return api;
    }
}

/// <summary>
/// Hazır vergi/prim şablonları. Tutar boş bırakılır (her seferinde girilir); kanal Ortak.
/// Tarihler genel beyan/ödeme takvimidir; yasal değişiklikler ve tatil ötelemeleri için
/// kullanıcıya "Tarihleri muhasebecinizle doğrulayın" gösterilir.
/// </summary>
public static class TekrarlayanHazirlar
{
    /// <summary>Tek şablon: sıklık, evre ayı (1–12; yıllık/6 aylıkta ilk ay) ve ayın günü (31 = ay sonu).</summary>
    public sealed record Kalem(TekrarSikligi Siklik, int EvreAyi, int Gun);

    public sealed record Hazir(string Kod, string Ad, string Aciklama, IReadOnlyList<Kalem> Kalemler);

    public static readonly IReadOnlyList<Hazir> Liste =
    [
        new("kdv", "KDV", "Her ay 28'i", [new(TekrarSikligi.Aylik, 1, 28)]),
        new("muhtasar", "Muhtasar ve prim hizmet", "Her ay 26'sı", [new(TekrarSikligi.Aylik, 1, 26)]),
        new("sgk", "SGK primi", "Her ayın son günü", [new(TekrarSikligi.Aylik, 1, 31)]),
        new("gecici-vergi", "Geçici vergi", "17 Mayıs, 17 Ağustos, 17 Kasım",
            [new(TekrarSikligi.Yillik, 5, 17), new(TekrarSikligi.Yillik, 8, 17), new(TekrarSikligi.Yillik, 11, 17)]),
        new("mtv", "MTV", "31 Ocak, 31 Temmuz", [new(TekrarSikligi.AltiAylik, 1, 31)]),
        new("emlak-vergisi", "Emlak vergisi", "31 Mayıs, 30 Kasım", [new(TekrarSikligi.AltiAylik, 5, 31)]),
    ];

    /// <summary>
    /// Şablonun başlangıç ayı: bu aydan itibaren evreye uyan ve vadesi bugünden önce olmayan ilk ay
    /// (eklendiği anda geçmiş bir ay "bekleyen" olarak düşmesin).
    /// </summary>
    public static DateOnly IlkAy(Kalem k, DateOnly bugun)
    {
        var p = TekrarlayanTakvim.Periyot(k.Siklik);
        var ay = TekrarlayanTakvim.AyBasi(bugun);
        for (int i = 0; i < 24; i++, ay = ay.AddMonths(1))
        {
            var evreUygun = ((ay.Month - k.EvreAyi) % p + p) % p == 0;
            if (evreUygun && TekrarlayanTakvim.Vade(ay, k.Gun) >= bugun) return ay;
        }
        return TekrarlayanTakvim.AyBasi(bugun);
    }
}
