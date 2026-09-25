using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static partial class FinansTakipEndpoints
{
    private static void MapKartMasrafEndpoints(RouteGroupBuilder api)
    {
        api.MapPost("/kartlar/{id:int}/masraf-onizleme", (int id, KartMasrafYaz dto, KasaDbContext db) =>
            View(db, () => MasrafOnizle(db, id, dto))).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/masraflar", (int id, KartMasrafYaz dto, KasaDbContext db) =>
            Change(db, true, id, dto.Surum, dto.IstekId, "KartMasraf", dto, () =>
            {
                var preview = MasrafOnizle(db, id, dto);
                Require(dto.DagilimOzeti == preview.DagilimOzeti, "Borç dağılımı veya masraf bilgisi değişti. Yeniden önizleyin.", 409);
                var card = db.KrediKartlari.Single(k => k.Id == id);
                HarcamaEkle(db, card, new TakipHarcamaEntity { KrediKartiId = id, Tarih = dto.Tarih,
                    Aciklama = "Faiz / masraf · " + dto.Aciklama.Trim(), Tutar = dto.Tutar, TaksitSayisi = 1,
                    DagilimJson = Json(preview.Dagilimlar.Select(p => new KanalPayYaz(p.KanalId!.Value, p.Tutar)).ToList()) });
                return id;
            })).RequireAuthorization("Editor");
    }

    private static KartMasrafOnizlemeDto MasrafOnizle(KasaDbContext db, int id, KartMasrafYaz dto)
    {
        var tracking = ManagedCard(db, id);
        Require(tracking.Aktif, "Bu kart yeni harekete kapalı.", 409);
        Require(dto.Surum == tracking.Surum, "Kart değişmiş. Güncel bilgileri yükleyip yeniden önizleyin.", 409);
        Money(dto.Tutar); Require(dto.Tutar > 0, "Bankanın faiz/masraf tutarı sıfırdan büyük olmalı."); Text(dto.Aciklama); Date(dto.Tarih);
        var statement = db.TakipEkstreler.AsNoTracking().SingleOrDefault(e => e.Id == dto.EkstreId && e.KrediKartiId == id);
        Require(statement is not null && statement.KesimTarihi <= Bugun, "Bu karta ait kesilmiş bir ekstre seçin.");
        Require(dto.Tarih >= statement.KesimTarihi && dto.Tarih >= tracking.Baslangic && dto.Tarih <= Bugun,
            "Masraf tarihi seçilen hesap kesimi ile bugün arasında olmalı.");
        Require(!db.TakipKartOdemeler.Any(p => p.KrediKartiId == id && !p.Iptal && p.Tarih > dto.Tarih)
            && !db.TakipHarcamalar.Any(h => h.KrediKartiId == id && !h.Iptal && h.Tutar < 0 && h.Tarih > dto.Tarih),
            "Bu tarihten sonra ödeme veya iade var. Güncel kalan borca dağıtmak için güncel tarihi kullanın.", 409);
        var statements = db.TakipEkstreler.Where(e => e.KrediKartiId == id && e.KesimTarihi <= statement.KesimTarihi).Select(e => e.Id).ToHashSet();
        var charges = db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == id && !h.Iptal && h.Tutar > 0).ToList();
        var ids = charges.Select(h => h.Id).ToArray();
        var installments = db.TakipKartTaksitler.AsNoTracking().Where(t => ids.Contains(t.HarcamaId)).ToList();
        var remaining = KalanTaksitler(db, id);
        var eligibleWeights = new List<KanalPayYaz>();
        decimal debt = 0;
        foreach (var charge in charges)
        {
            var parts = installments.Where(t => t.HarcamaId == charge.Id).ToList();
            var eligible = parts.Where(t => statements.Contains(t.EkstreId)).Sum(t => remaining.GetValueOrDefault(t.Id));
            if (eligible <= 0) continue;
            var source = IadeSonrasiPaylar(db, charge);
            var outstanding = parts.Sum(t => remaining.GetValueOrDefault(t.Id));
            var sourceTotal = source.Sum(p => p.Tutar);
            Require(source.Count > 0 && sourceTotal >= outstanding,
                "Devreden borcun bir bölümünde kanal dağılımı bekliyor. Önce ilgili alışın dağılımını kesinleştirin.", 409);
            // Ödenmiş kuruşlar yeniden dağıtılmaz; ileri taksitler bu masrafın ağırlığına girmez.
            eligibleWeights.AddRange(Oranla(source, eligible, sourceTotal - outstanding));
            debt += eligible;
        }
        Require(debt > 0, "Seçilen ve önceki ekstrelerde dağıtılacak kalan borç yok.");
        var weights = eligibleWeights.GroupBy(p => p.KanalId).OrderBy(g => g.Key).Select(g => new KanalPayYaz(g.Key, g.Sum(p => p.Tutar))).ToList();
        // Masraf kalan anaparadan yüksek olabilir. Ortak çarpan oranı korur ve
        // ödeme dağıtıcısının kaynak tutar üst sınırına takılmadan yeni tutarı böler.
        var scale = Math.Max(1m, decimal.Ceiling(dto.Tutar / debt));
        var shares = Adlandir(db, Oranla(weights.Select(p => new KanalPayYaz(p.KanalId, p.Tutar * scale)).ToList(), dto.Tutar));
        var digest = FinansHesaplari.Ozet(new { id, dto.EkstreId, dto.Tarih, dto.Tutar, Aciklama = dto.Aciklama.Trim(), debt, weights });
        return new(id, dto.EkstreId, dto.Tarih, dto.Tutar, debt, shares, digest);
    }
}
