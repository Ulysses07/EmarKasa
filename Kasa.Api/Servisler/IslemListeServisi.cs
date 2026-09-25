using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

public record IslemOkuDto(
    int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, int? KanalId,
    GiderTipi Tip, string? Not, int? KrediKartiId, int? AlisId = null, bool DagilimBekliyor = false, int? AylikGiderOdemeId = null, int? EkstreKayitId = null);

/// <summary>Gerçek giderleri tek satır olarak, alışın güncel ödeme dağılımıyla gösterir.</summary>
public class IslemListeServisi
{
    private readonly KasaDbContext _db;

    public IslemListeServisi(KasaDbContext db) => _db = db;

    public IReadOnlyList<IslemOkuDto> Liste(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari)
    {
        // Başlık, kalem ve ödeme sorguları aynı onay/iadeyi görmeli.
        using var snapshot = _db.Database.BeginTransaction();
        var query = _db.Islemler.AsNoTracking();
        if (baslangic is { } ilk) query = query.Where(i => i.Tarih >= ilk);
        if (bitis is { } son) query = query.Where(i => i.Tarih <= son);
        if (!string.IsNullOrWhiteSpace(cari)) query = query.Where(i => i.Cari.Contains(cari));
        var kayitlar = query.OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
        var secilenIdler = query.Select(i => i.Id);
        var monthly = _db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null && secilenIdler.Contains(p.IslemId.Value)).ToDictionary(p => p.IslemId!.Value);
        var revisions = _db.AylikGiderRevizyonlar.AsNoTracking().ToDictionary(r => r.Id);
        var imports = _db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.IslemId != null && secilenIdler.Contains(k.IslemId.Value)).ToDictionary(k => k.IslemId!.Value);

        // Filtre dışındaki eski ödemeler de kümülatif kuruş hesabına katılır.
        var alislar = _db.Alislar.AsNoTracking()
            .Where(a => a.Odemeler.Any(o => secilenIdler.Contains(o.IslemId)))
            .Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).ThenInclude(d => d.KanalKaydi)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem)
            .AsSplitQuery().ToList();

        var eslemeler = new Dictionary<int, Esleme>();
        foreach (var alis in alislar)
        {
            bool bekliyor = alis.Durum != AlisDurumlari.Onaylandi;
            var dagilimlar = AlisHesaplari.OdemeDagilimlari(alis);
            var adlar = alis.Kalemler.SelectMany(k => k.Dagilimlar)
                .GroupBy(d => d.KanalId).ToDictionary(g => g.Key, g => g.First().KanalKaydi.Ad);
            foreach (var odeme in alis.Odemeler)
            {
                if (bekliyor)
                {
                    eslemeler.Add(odeme.IslemId, new Esleme(alis.Id, true, null, [Kanallar.DagilimBekliyor]));
                    continue;
                }
                var paylar = dagilimlar[odeme.IslemId].Where(p => p.Tutar > 0).OrderBy(p => p.KanalId).ToArray();
                eslemeler.Add(odeme.IslemId, new Esleme(alis.Id, false,
                    paylar.Length == 1 ? paylar[0].KanalId : null,
                    paylar.Select(p => adlar[p.KanalId]).ToArray()));
            }
        }

        var sonuc = new List<IslemOkuDto>();
        foreach (var kayit in kayitlar)
        {
            if (imports.TryGetValue(kayit.Id, out var imported))
            {
                var shares = FinansTakipServisi.Adlandir(_db, FinansTakipServisi.Read<TakipKanalPayi>(imported.DagilimJson)
                    .Select(p => new KanalPayYaz(p.KanalId!.Value, p.Tutar)));
                if (!string.IsNullOrWhiteSpace(kanal) && !shares.Any(s => s.Kanal == kanal)) continue;
                sonuc.Add(new(kayit.Id, kayit.Tarih, kayit.Cari, kayit.TutarTl, shares.Count == 0 ? "Genel kasa" : string.Join(" / ", shares.Select(s => s.Kanal)),
                    shares.Count == 1 ? shares[0].KanalId : null, kayit.Tip, kayit.Not, null, EkstreKayitId: imported.Id));
                continue;
            }
            if (monthly.TryGetValue(kayit.Id, out var monthlyPayment))
            {
                var revision = revisions[monthlyPayment.RevizyonId];
                var shares = FinansTakipServisi.Adlandir(_db, FinansTakipServisi.Read<KanalPayYaz>(revision.DagilimJson));
                if (!string.IsNullOrWhiteSpace(kanal) && !shares.Any(s => s.Kanal == kanal)) continue;
                sonuc.Add(new(kayit.Id, kayit.Tarih, kayit.Cari, kayit.TutarTl, shares.Count == 0 ? "Genel kasa" : string.Join(" / ", shares.Select(s => s.Kanal)),
                    shares.Count == 1 ? shares[0].KanalId : null, kayit.Tip, kayit.Not, null, AylikGiderOdemeId: monthlyPayment.Id));
                continue;
            }
            eslemeler.TryGetValue(kayit.Id, out var esleme);
            var adlar = esleme?.KanalAdlari ?? [kayit.Kanal];
            if (!string.IsNullOrWhiteSpace(kanal) && !adlar.Contains(kanal, StringComparer.Ordinal)) continue;
            sonuc.Add(new IslemOkuDto(kayit.Id, kayit.Tarih, kayit.Cari, kayit.TutarTl,
                string.Join(" / ", adlar), esleme is null ? kayit.KanalId : esleme.KanalId,
                kayit.Tip, kayit.Not, kayit.KrediKartiId, esleme?.AlisId, esleme?.Bekliyor ?? false));
        }

        snapshot.Commit();
        return sonuc;
    }

    private sealed record Esleme(int AlisId, bool Bekliyor, int? KanalId, string[] KanalAdlari);
}
