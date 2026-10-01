using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kasa.Api;

/// <summary>SaveChanges öncesinde, aynı SQLite yazma transaction'ında geçmiş mali etkileri korur.</summary>
public static class AyKilidiKurallari
{
    public static void TarihAcik(KasaDbContext db, DateOnly date)
    {
        var end = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single();
        if (end is { } locked && date <= locked)
            Fail(locked);
    }
    internal static void Dogrula(KasaDbContext db)
    {
        // Dondurulmuş eski şema testleri/bridge aşaması kilit tablosundan öncedir.
        if (db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='AyKilidi'").Single() == 0)
            return;
        // Kanal adı değişikliğinin etiket senkronu (yalnız Kanal metni kanalın güncel adına eşitlenen gider/kredi) mali değişiklik
        // değildir: kaynak kurallarına ve dönem kilidine girmez (KanalKurallari).
        var entries = KanalKurallari.EtiketSenkronuHaric(db,
            db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList());
        if (db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='EkstreKayitlar'").Single() != 0)
            EkstreKaynakKurallari.Dogrula(db, entries);
        // Tamamlanmış ayın kanal kümesi (kilit olmasa da) değişmez ve kümedeki kanal silinmez (AyKanalKumesi).
        AyKanalKumesi.Dogrula(db, entries);
        foreach (var e in entries)
        {
            if (e.Entity is KanalEntity channel && e.State == EntityState.Deleted && db.AylikGiderRevizyonlar.AsNoTracking().AsEnumerable()
                .Any(r => FinansTakipServisi.Read<KanalPayYaz>(r.DagilimJson).Any(p => p.KanalId == channel.Id)))
                throw new KilitliDonemException("Aylık gider şablonunda kullanılan kanal silinemez; pasife alınabilir.");
            if (!db.AylikGiderDegisikligi && e.Entity is IslemEntity i && e.State != EntityState.Added
                && db.AylikGiderOdemeler.Any(p => p.IslemId == i.Id))
                throw new KilitliDonemException("Aylık gider ödemesini Aylık Giderler bölümünden iptal edip yeniden kaydedin.");
            if (e.Entity is AlisOdemeEntity linked && db.AylikGiderOdemeler.Any(p => p.IslemId == linked.IslemId))
                throw new KilitliDonemException("Aylık gider ödemesi başka bir alışa bağlanamaz.");
        }
        var until = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single();
        // Kilitli ay rapor görüntüsü (K4) değiştirilemez: hiç güncellenmez, yalnız kilitli ay için eklenir ve kilitli ayınki
        // silinmez (önce kilit açılır). Ay kapatılırken/açılırken görüntü, kilit sınırı kaydedildikten sonra yazılır/silinir.
        foreach (var e in entries)
            if (e.Entity is AyRaporAnlikGoruntuEntity g)
            {
                var kilitli = until is { } son && AyRaporAnlikGoruntusu.AySonu(g.Yil, g.Ay) <= son;
                if (e.State == EntityState.Modified || e.State == EntityState.Added && !kilitli || e.State == EntityState.Deleted && kilitli)
                    throw new KilitliDonemException("Kilitli ayın dondurulmuş raporu değiştirilemez; yalnız ay kapatılırken yazılır ve ay açılınca silinir.");
            }
        if (until is not { } end)
            return;

        bool DateLocked(EntityEntry e, string property) => e.CurrentValues[property] is DateOnly date && date <= end
            || e.State != EntityState.Added && e.OriginalValues[property] is DateOnly old && old <= end;
        bool Changed(EntityEntry e, params string[] properties) => e.State != EntityState.Modified || properties.Any(p => e.Property(p).IsModified);
        bool ChargePaidBefore(int charge)
        {
            var taxes = db.TakipKartTaksitler.Where(t => t.HarcamaId == charge).Select(t => t.Id).ToHashSet();
            return db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal && p.Tarih <= end).AsEnumerable()
                .Any(p => FinansTakipServisi.Read<KartTaksitPayi>(p.PaylarJson).Any(x => taxes.Contains(x.TaksitId)));
        }
        bool PurchaseLocked(int id)
        {
            var expenseIds = db.AlisOdemeler.AsNoTracking().Where(p => p.AlisId == id).Select(p => p.IslemId).ToList();
            if (db.Islemler.Any(i => expenseIds.Contains(i.Id) && i.Tarih <= end))
                return true;
            return db.TakipHarcamalar.Where(h => h.IslemId != null && expenseIds.Contains(h.IslemId.Value)).Select(h => h.Id).ToList().Any(ChargePaidBefore);
        }
        bool ExpenseChangesLaterClosedPurchasePayment(IslemEntity expense)
        {
            var link = db.AlisOdemeler.AsNoTracking().SingleOrDefault(p => p.IslemId == expense.Id);
            return link is not null && db.AlisOdemeler.Any(p => p.AlisId == link.AlisId && p.Id > link.Id && p.Islem.Tarih <= end);
        }
        // Çekin kasayı etkileyen (türetilmiş satır üreten) hareketi kilitli dönemde mi (docs/specs/2026-10-01-cekler.md "Ay kilidi").
        bool CekKilitli(int cekId) => db.CekHareketler.AsNoTracking().Where(h => h.CekId == cekId && h.Tarih <= end).Select(h => h.Tur)
            .AsEnumerable().Any(CekKurallari.KasaEtkili);

        // Kanal: tamamlanmış ayların kanal kümesi değişiklikten önce dondurulduğundan (AyKanalKumesi) kilitli dönemi yalnız açılış
        // devri etkiler; ekleme, ad, aktiflik ve sıra serbesttir (KanalKurallari).
        if (KanalKurallari.KilitIhlali(entries.Where(e => e.Entity is KanalEntity).ToList(), end) is { } kanalIletisi)
            throw new KilitliDonemException(kanalIletisi);
        foreach (var e in entries)
        {
            bool blocked = e.Entity switch
            {
                // Kilitli döneme düşen kart avansı yeni harcamayı engellemez: avans kilit sonrası tarihli ayrı dağıtım
                // kaydıyla bağlanır, kilitli ödemenin payları değişmez (finance-8, FinansTakipServisi.AvanslariDagit).
                IslemEntity i => DateLocked(e, nameof(i.Tarih))
                    || e.State != EntityState.Added && Changed(e, "TutarTl", "Tarih", "Kanal", "KanalId", "Tip", "KrediKartiId") && ExpenseChangesLaterClosedPurchasePayment(i),
                GelenEntity g => DateLocked(e, nameof(g.DonemStart)),
                KartOdemeEntity p => DateLocked(e, nameof(p.Tarih)),
                AylikGiderOdemeEntity p => DateLocked(e, nameof(p.Tarih)) || DateLocked(e, nameof(p.Ay)),
                AylikGiderRevizyonEntity r => e.State != EntityState.Added || DateLocked(e, nameof(r.GecerliAy)),
                AyarEntity => Changed(e, "TakipBaslangic", "KasaAcilisDevri"),
                KrediEntity k => !(e.State == EntityState.Added && db.GecmisEtkisizKrediOlusturma) && DateLocked(e, nameof(k.CekimTarihi)) && Changed(e, "CekilenTutar", "CekimTarihi", "TaksitSayisi", "AylikOdeme", "OdemeGunu", "Kanal", "KanalId", "GerceklesmeTakibi"),
                KrediKartiEntity => e.State == EntityState.Deleted || e.State == EntityState.Modified && Changed(e, "Borc"),
                TakipKartOdemeEntity p => DateLocked(e, nameof(p.Tarih)) || e.State != EntityState.Added
                    && db.TakipKartOdemeler.Any(later => later.KrediKartiId == p.KrediKartiId && later.Id > p.Id && !later.Iptal && later.Tarih <= end),
                TakipHarcamaEntity h => DateLocked(e, nameof(h.Tarih))
                    || h.KaynakHarcamaId is { } source && ChargePaidBefore(source)
                    || e.State != EntityState.Added && ChargePaidBefore(h.Id),
                // İade hesabı ve avans dağıtımı bağı yalnız eklenir (iade/dağıtım kaydıyla birlikte, kilit sonrası tarihte).
                TakipIadeHesabiEntity or TakipAvansTahsisEntity => e.State != EntityState.Added,
                TakipKartTaksitEntity t => e.State != EntityState.Added && db.TakipHarcamalar.Any(h => h.Id == t.HarcamaId && h.Tarih <= end),
                TakipKartEntity t => Changed(e, "Baslangic", "EskiKayit", "EskiDusumKurali") && DateLocked(e, nameof(t.Baslangic)),
                TakipKrediEntity t => Changed(e, "Baslangic", "MevcutKredi", "EskiKayit", "KanalIdleriJson", "CekimPaylariJson") && DateLocked(e, nameof(t.Baslangic)),
                TakipKrediTaksitEntity t => DateLocked(e, nameof(t.Tarih)) && Changed(e, "Tarih", "Tutar", "Iptal", "DagilimJson", "KrediId"),
                KrediTaksitOdemeEntity p => db.Islemler.Any(i => i.Id == p.IslemId && i.Tarih <= end),
                // Çek hareketi kilitli dönemde eklenemez ve silinemez (geri alma dahil). Kasayı etkileyen hareketi kilitli dönemde olan
                // çekin tutarı, kasası, yönü ve türü değişmez, çek silinmez; vade, konum, not, kişi, banka ve no her zaman değişir.
                CekHareketEntity h => DateLocked(e, nameof(h.Tarih)),
                CekEntity c => Changed(e, "Tutar", "KanalId", "Yon", "Tur") && CekKilitli(c.Id),
                HesapEntity => true,
                HesapHareketEntity h => DateLocked(e, nameof(h.Tarih)),
                HesapTransferEntity h => DateLocked(e, nameof(h.Tarih)),
                EkstreKayitEntity k => DateLocked(e, nameof(k.Tarih)),
                AlisEntity a => Changed(e, "Durum", "Tarih") && PurchaseLocked(a.Id),
                AlisKalemEntity k => PurchaseLocked(k.AlisId),
                AlisDagilimEntity d => PurchaseLocked(db.AlisKalemler.Where(k => k.Id == d.AlisKalemId).Select(k => k.AlisId).FirstOrDefault()),
                // Başka alışa taşınan kart ödemesinin (gap-coklu-giris-cift-sayim-mutabakat-5) harcaması kilitli dönemde bir kart
                // ödemesiyle ödendiyse o ödemenin kanal payı yeni alışın dağılımına geçerdi. Kaynağın sonraki ödemeleri taşıma kilidinde
                // (AlisOdemeIslemleri.TasimaKilidi), hedefinki burada (PurchaseLocked) denetlenir.
                AlisOdemeEntity p => e.State != EntityState.Added && PurchaseLocked(p.AlisId) || db.Islemler.Any(i => i.Id == p.IslemId && i.Tarih <= end)
                    || e.State == EntityState.Modified && db.TakipHarcamalar.Where(h => h.IslemId == p.IslemId).Select(h => h.Id).ToList().Any(ChargePaidBefore),
                // Kilitli dönem alışının belgesi (kanıt) kaldırılamaz, ödemeye bağlı olmayan belgesi başka alışa ya da ödemeye taşınamaz;
                // yeni belge eklenebilir. Ödemeye bağlı belge ödemesiyle birlikte taşınır: o taşımanın kilidi ödeme kuralındadır
                // (AlisOdemeIslemleri.TasimaKilidi).
                BelgeEntity b => e.State == EntityState.Deleted && PurchaseLocked((int)e.OriginalValues[nameof(b.AlisId)]!)
                    || e.State == EntityState.Modified
                        && (e.Property(nameof(b.Silindi)).IsModified && PurchaseLocked((int)e.OriginalValues[nameof(b.AlisId)]!)
                            || (e.Property(nameof(b.AlisId)).IsModified || e.Property(nameof(b.OdemeId)).IsModified) && e.OriginalValues[nameof(b.OdemeId)] is null
                                && (PurchaseLocked((int)e.OriginalValues[nameof(b.AlisId)]!) || PurchaseLocked(b.AlisId))),
                _ => false
            };
            if (blocked)
                Fail(end);
        }
    }
    private static void Fail(DateOnly end) => throw new KilitliDonemException($"{end:yyyy-MM-dd} tarihine kadar dönem kilitli. Geçmişi etkileyen bu işlem için ilgili ayı gerekçeyle açın.");
}
