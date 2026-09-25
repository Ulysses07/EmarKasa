using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır.</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    public HesapServisi(KasaDbContext db) => _db = db;

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis,
        IReadOnlyDictionary<string, int?> KanalIdleri);

    private Yuk Yukle(DateOnly? raporBitis = null)
    {
        // Alış onayı/ödeme eşleştirmesi rapor okunurken yarım görünmesin.
        using var snapshot = _db.Database.CurrentTransaction is null ? _db.Database.BeginTransaction() : null;
        FinansTakipServisi.Sync(_db);
        var kartTakip = _db.TakipKartlar.AsNoTracking().ToDictionary(t => t.KrediKartiId);
        var kanallar = _db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList().Select(e => e.ToCore()).ToList();
        var kayitlar = _db.Islemler.AsNoTracking().Include(i => i.KanalKaydi).ToList();
        var alislar = _db.Alislar.AsNoTracking()
            .Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).ThenInclude(d => d.KanalKaydi)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem).ToList();
        var eslemeler = alislar.SelectMany(a => a.Odemeler.Select(o => (o.IslemId, Alis: a)))
            .ToDictionary(o => o.IslemId, o => o.Alis);
        var dagilimlar = alislar.ToDictionary(a => a.Id, AlisHesaplari.OdemeDagilimlari);
        var kanalAdlari = _db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        var aylikOdemeler = _db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null).ToDictionary(p => p.IslemId!.Value);
        var aylikRevizyonlar = _db.AylikGiderRevizyonlar.AsNoTracking().ToDictionary(r => r.Id);
        var imported = _db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal).ToList();
        var importedExpenses = imported.Where(k => k.IslemId != null).ToDictionary(k => k.IslemId!.Value);
        var dbIslemler = new List<Islem>();
        foreach (var kayit in kayitlar)
        {
            if (importedExpenses.TryGetValue(kayit.Id, out var importedExpense))
            {
                var source = kayit.ToCore();
                if (importedExpense.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else foreach (var share in FinansTakipServisi.Read<TakipKanalPayi>(importedExpense.DagilimJson))
                    dbIslemler.Add(source with { Kanal = kanalAdlari[share.KanalId!.Value], TutarTl = share.Tutar });
                continue;
            }
            if (aylikOdemeler.TryGetValue(kayit.Id, out var aylikOdeme))
            {
                var revision = aylikRevizyonlar[aylikOdeme.RevizyonId];
                var source = kayit.ToCore() with { AylikGider = true };
                if (revision.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(revision.DagilimJson))
                    dbIslemler.Add(source with { Kanal = kanalAdlari[share.KanalId], TutarTl = share.Tutar });
                continue;
            }
            if (kayit.KrediKartiId is { } cardId && kartTakip.TryGetValue(cardId, out var tracking))
            {
                var oldEffectMonth = kayit.Tarih.AddMonths(1);
                var oldEffect = new DateOnly(oldEffectMonth.Year, oldEffectMonth.Month, DateTime.DaysInMonth(oldEffectMonth.Year, oldEffectMonth.Month));
                if (!tracking.EskiKayit || oldEffect >= tracking.Baslangic) continue;
            }
            var islem = kayit.ToCore();
            if (!eslemeler.TryGetValue(kayit.Id, out var alis))
                dbIslemler.Add(islem);
            else if (alis.Durum != "Onaylandi")
                dbIslemler.Add(islem with { Kanal = Kanallar.DagilimBekliyor, DagilimBekliyor = true });
            else
                foreach (var pay in dagilimlar[alis.Id][kayit.Id].Where(p => p.Tutar > 0))
                    dbIslemler.Add(islem with { Kanal = kanalAdlari[pay.KanalId], TutarTl = pay.Tutar });
        }
        var dbGelenler = _db.Gelenler.AsNoTracking().Include(g => g.KanalKaydi).ToList().Select(e => e.ToCore()).ToList();
        var krediKayitlari = _db.Krediler.AsNoTracking().Include(k => k.KanalKaydi).ToList();
        var krediTakip = _db.TakipKrediler.AsNoTracking().ToDictionary(t => t.KrediId);
        var ayar = _db.Ayarlar.AsNoTracking().First();

        var baslangic = ayar.TakipBaslangic;
        var bugun = FinansTakipServisi.Bugun;
        // Dönem ufku yalnız GERÇEKLEŞEN veriye göre (DB işlemleri + bugün). Gelecek kredi
        // taksitleri ufku ileri ÇEKMEZ — aksi halde henüz ödenmemiş taksitler güncel kasadan
        // erken düşerdi (spec: gelecek taksit güncel kasayı etkilemez; ileri aylar o ayın
        // raporu sorulunca yansır).
        var ekGelirTarihleri = _db.HesapHareketler.AsNoTracking().Where(h => h.IslemId == null && h.GelenId == null && h.KartOdemeId == null && h.KrediId == null).Select(h => h.Tarih).ToList();
        var enGecIslem = dbIslemler.Select(i => i.Tarih).Concat(ekGelirTarihleri).Concat(imported.Where(k => k.IslemTuru == "Gelir").Select(k => k.Tarih)).DefaultIfEmpty(bugun).Max();
        var bitis = raporBitis ?? new[] { bugun, enGecIslem, baslangic }.Max();
        var donemler = DonemUretici.Uret(baslangic, bitis);

        // Krediyi sentetik kayıtlara türet (DB'ye yazılmaz, yalnız motora beslenir):
        // çekim → genel kasa geliri, taksitler → seçilen kanal/Ortak gideri.
        var taksitler = krediKayitlari.Where(k => !k.GerceklesmeTakibi).SelectMany(k =>
            KrediTuretici.TaksitGiderleri(k.ToCore()).Where(t => !krediTakip.TryGetValue(k.Id, out var tracking) || (tracking.EskiKayit && t.Tarih < tracking.Baslangic)));
        var islemler = dbIslemler.Concat(taksitler).ToList();
        var cekimGelenleri = krediKayitlari.Where(k => !krediTakip.TryGetValue(k.Id, out var tracking) || tracking.EskiKayit)
            .Select(k => KrediTuretici.CekimGeleni(k.ToCore(), donemler))
            .Where(g => g is not null)
            .Select(g => g!);
        var gelenler = dbGelenler.Concat(cekimGelenleri).Concat(FinansHesaplari.EkGelirler(_db, donemler)).ToList();
        foreach (var income in imported.Where(k => k.IslemTuru == "Gelir"))
            if (donemler.FirstOrDefault(d => d.Icerir(income.Tarih)) is { } period)
            {
                if (income.DagilimTuru == "Genel") gelenler.Add(new(period.Start, "Genel kasa", income.Tutar, GenelGelir: true));
                else foreach (var share in FinansTakipServisi.Read<TakipKanalPayi>(income.DagilimJson))
                    gelenler.Add(new(period.Start, kanalAdlari[share.KanalId!.Value], share.Tutar));
            }
        foreach (var loan in krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)))
        {
            var tracking = krediTakip[loan.Id];
            if (!tracking.MevcutKredi && donemler.FirstOrDefault(d => d.Icerir(loan.CekimTarihi)) is { } period)
                foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(tracking.CekimPaylariJson))
                    gelenler.Add(new(period.Start, kanalAdlari[share.KanalId], share.Tutar, KrediGirisi: true));
            foreach (var installment in _db.TakipKrediTaksitler.AsNoTracking().Where(t => t.KrediId == loan.Id && !t.Iptal).ToList())
                foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(installment.DagilimJson))
                    islemler.Add(new(installment.Tarih, loan.Ad + " / " + installment.No + ". taksit", share.Tutar, kanalAdlari[share.KanalId], GiderTipi.Cari));
        }
        foreach (var cardId in kartTakip.Keys)
            foreach (var payment in FinansTakipServisi.Kart(_db, cardId).Odemeler.Where(p => !p.Iptal))
                foreach (var share in payment.Dagilimlar)
                    islemler.Add(new(payment.Tarih, "Kart ödemesi", share.Tutar, share.Kanal, GiderTipi.KrediKarti, payment.Not,
                        DagilimBekliyor: share.KanalId is null, NakitKartOdemesi: true));

        snapshot?.Commit();
        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri,
            kanalAdlari.ToDictionary(k => k.Value, k => (int?)k.Key));
    }

    public IReadOnlyList<HaftalikOzet> Haftalik()
    {
        var y = Yukle();
        return HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public AylikRapor Aylik(int yil, int ay)
    {
        var y = Yukle(new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay)));
        return HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public IReadOnlyList<Donem> Donemler() => Yukle().Donemler;

    public PanelDto Panel()
    {
        // Gelecek tarihli manuel kayıtlar paneli gelecek bir döneme taşıyamaz.
        var bugun = FinansTakipServisi.Bugun;
        var y = Yukle(bugun);
        var haftalik = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var buHafta = son?.KasaSonucu ?? 0m;
        var kanalIdleri = y.KanalIdleri;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir, kanalIdleri.GetValueOrDefault(k.Kanal))).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri, kanalIdleri.GetValueOrDefault(k.Ad))).ToList();

        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu) - buAyRapor.DagilimBekleyenTutar - buAyRapor.GenelGider + buAyRapor.GenelGelir;

        return new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy,
            haftalik.Sum(h => h.DagilimBekleyenTutar));
    }
}
