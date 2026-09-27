using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api;

public static class FinansTakipServisi
{
    /// <summary>İstanbul'a göre bugün (<see cref="KasaSaati.Bugun"/>). Bağlamı olan kod <c>db.Bugunu()</c>
    /// kullanır; o, istek dışında da uygulamanın saatini verir.</summary>
    public static DateOnly Bugun => KasaSaati.Bugun;
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);
    internal static List<T> Read<T>(string value) => JsonSerializer.Deserialize<List<T>>(value) ?? [];
    internal static DateOnly Gun(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
    internal static DateOnly Kesim(DateOnly date, int day) { var cut = Gun(date, day); return cut < date ? Gun(date.AddMonths(1), day) : cut; }
    internal static DateOnly Vade(DateOnly cut, int day) { var due = Gun(cut, day); return due <= cut ? Gun(cut.AddMonths(1), day) : due; }

    internal static List<KanalPayYaz> EsitPaylar(IReadOnlyList<int> ids, decimal amount, decimal onceki = 0)
    {
        var sorted = ids.Order().ToArray();
        var before = decimal.ToInt64(onceki * 100); var after = decimal.ToInt64((onceki + amount) * 100);
        return sorted.Select((id, i) => new KanalPayYaz(id,
            (after / sorted.Length + (i < after % sorted.Length ? 1 : 0) - before / sorted.Length - (i < before % sorted.Length ? 1 : 0)) / 100m)).ToList();
    }
    internal static List<KanalPayYaz> Oranla(IReadOnlyList<KanalPayYaz> weights, decimal amount, decimal onceki = 0)
    {
        var positive = weights.Where(w => w.Tutar > 0).ToList();
        if (positive.Count == 0 || amount <= 0) return [];
        return AlisDagitici.Dagit(positive.Select(w => new AlisKanalPayi(w.KanalId, w.Tutar)).ToList(), onceki, amount)
            .Select(p => new KanalPayYaz(p.KanalId, p.Tutar)).Where(p => p.Tutar > 0).ToList();
    }
    /// <summary>Oranla'nın taşmaya dayanıklı biçimi. onceki+tutar pozitif ağırlık toplamını
    /// aşmıyorsa sonuç Oranla ile birebir aynıdır ve Tasan 0'dır. Aşıyorsa (Oranla'nın istisna
    /// fırlattığı bozuk/eski veri) yalnız sığan kısım dağıtılır, fazlası Tasan olarak döner.</summary>
    public static (List<KanalPayYaz> Paylar, decimal Tasan) KirparakOranla(IReadOnlyList<KanalPayYaz> weights, decimal amount, decimal onceki = 0)
    {
        var positive = weights.Where(w => w.Tutar > 0).ToList();
        if (positive.Count == 0 || amount <= 0) return ([], 0);
        var dagitilan = Math.Min(amount, Math.Max(0, positive.Sum(w => w.Tutar) - onceki));
        return (Oranla(positive, dagitilan, onceki), amount - dagitilan);
    }
    internal static List<TakipKanalPayi> Adlandir(KasaDbContext db, IEnumerable<KanalPayYaz> paylar)
    {
        var names = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        return paylar.Select(p => new TakipKanalPayi(p.KanalId, names.GetValueOrDefault(p.KanalId, "Silinmiş kanal"), p.Tutar)).ToList();
    }
    internal static List<KanalPayYaz> KaynakPaylari(KasaDbContext db, TakipHarcamaEntity charge)
    {
        if (charge.IslemId is not { } id) return Read<KanalPayYaz>(charge.DagilimJson);
        var purchaseId = db.AlisOdemeler.Where(o => o.IslemId == id).Select(o => (int?)o.AlisId).FirstOrDefault();
        if (purchaseId is { } alisId)
        {
            var purchase = AlisEndpoints.Query(db).AsNoTracking().Single(a => a.Id == alisId);
            return AlisHesaplari.OdemeDagilimlari(purchase).TryGetValue(id, out var shares)
                ? shares.Select(s => new KanalPayYaz(s.KanalId, s.Tutar)).ToList() : [];
        }
        return Read<KanalPayYaz>(charge.DagilimJson);
    }
    internal static TakipEkstreEntity Ekstre(KasaDbContext db, KrediKartiEntity card, DateOnly cut)
    {
        var existing = db.TakipEkstreler.Local.FirstOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut)
            ?? db.TakipEkstreler.SingleOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut);
        if (existing is not null) return existing;
        existing = new() { KrediKartiId = card.Id, KesimTarihi = cut, SonOdemeTarihi = Vade(cut, card.SonOdemeTarihi.Day) };
        db.TakipEkstreler.Add(existing); db.SaveChanges(); return existing;
    }
    internal static List<KanalPayYaz> IadeSonrasiPaylar(KasaDbContext db, TakipHarcamaEntity charge)
    {
        var source = KaynakPaylari(db, charge).ToDictionary(p => p.KanalId, p => p.Tutar);
        foreach (var refund in db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == charge.Id && !h.Iptal).ToList())
            foreach (var share in Read<KanalPayYaz>(refund.DagilimJson))
                if (source.ContainsKey(share.KanalId)) source[share.KanalId] -= share.Tutar;
        return source.Where(p => p.Value > 0).Select(p => new KanalPayYaz(p.Key, p.Value)).ToList();
    }
    internal static void HarcamaEkle(KasaDbContext db, KrediKartiEntity card, TakipHarcamaEntity charge, DateOnly? firstCut = null)
    {
        db.TakipHarcamalar.Add(charge); db.SaveChanges();
        var cut = firstCut ?? Kesim(charge.Tarih, card.KesimTarihi.Day);
        var cents = decimal.ToInt64(Math.Abs(charge.Tutar) * 100); var sign = Math.Sign(charge.Tutar);
        for (var i = 0; i < charge.TaksitSayisi; i++)
        {
            var statement = Ekstre(db, card, Gun(cut.AddMonths(i), firstCut?.Day ?? card.KesimTarihi.Day));
            db.TakipKartTaksitler.Add(new() { HarcamaId = charge.Id, EkstreId = statement.Id,
                Tutar = sign * (cents / charge.TaksitSayisi + (i < cents % charge.TaksitSayisi ? 1 : 0)) / 100m });
        }
        db.SaveChanges();
    }
    /// <summary>Eski kartı yeni takibe alır; doğrulama (KartGecisHesabi ile önizleme) çağırandadır. Yeni
    /// geçişler işlem tarihi kuralıyla yazılır: başlangıçtan önceki eski giderler eski ay sonu kuralıyla
    /// bir kez düşer, devir borcunun kasada önceden sayılan kısmı ödemede ikinci kez düşmez. Mali sonucu
    /// belirleyen karar denetim izi olarak saklanır: açıklama ve onay anındaki önizleme özeti.</summary>
    public static void KartGecisiYaz(KasaDbContext db, int kartId, DateOnly baslangic, decimal kalanBorc, decimal kasadaOncedenSayilan, IReadOnlyList<KanalPayYaz> dagilimlar, string aciklama)
    {
        var s = KartGecisHesabi.Hesapla(db, kartId, baslangic, kalanBorc);
        var kayit = new KartGecisKaydi(db.Bugunu(), kalanBorc, kasadaOncedenSayilan, s.SistemKartBorcu, s.EskiKuraldaIslenenTutar, s.BekleyenEskiDusumTutari, s.SonBekleyenDusumTarihi, s.OnerilenKasadaSayilanTutar);
        db.TakipKartlar.Add(new() { KrediKartiId = kartId, Baslangic = baslangic, EskiKayit = true, EskiDusumKurali = EskiDusumKurali.IslemTarihi,
            GecisAciklamasi = aciklama.Trim(), GecisOzetiJson = JsonSerializer.Serialize(kayit) }); db.SaveChanges();
        if (kalanBorc != 0) HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == kartId), new() { KrediKartiId = kartId, Tarih = baslangic, Aciklama = "Onaylanan eski borç devri",
            Tutar = kalanBorc, KasadaOncedenSayilanTutar = kasadaOncedenSayilan, DagilimJson = Json(dagilimlar) });
    }
    // Çağıran transaction açar. Kalıcı kaynak bağı aynı alışın iki kez borç olmasını engeller.
    internal static void Sync(KasaDbContext db)
    {
        var today = db.Bugunu();
        foreach (var tracking in db.TakipKartlar.ToList())
        {
            var card = db.KrediKartlari.Single(k => k.Id == tracking.KrediKartiId);
            var known = db.TakipHarcamalar.Where(h => h.KrediKartiId == card.Id && h.IslemId != null).Select(h => h.IslemId!.Value).ToHashSet();
            foreach (var expense in db.Islemler.Where(i => i.KrediKartiId == card.Id && i.Tarih >= tracking.Baslangic).ToList().Where(i => !known.Contains(i.Id)))
            {
                var channelIds = db.Kanallar.Where(k => k.Aktif).Select(k => k.Id).ToList();
                List<KanalPayYaz> frozen = expense.KanalId is { } channel ? [new(channel, Math.Abs(expense.TutarTl))]
                    : expense.Kanal == Kanallar.Ortak && channelIds.Count > 0 ? EsitPaylar(channelIds, Math.Abs(expense.TutarTl)) : [];
                HarcamaEkle(db, card, new() { KrediKartiId = card.Id, IslemId = expense.Id, Tarih = expense.Tarih,
                    Aciklama = expense.Cari, Tutar = expense.TutarTl, TaksitSayisi = 1, DagilimJson = Json(frozen) });
                tracking.Surum++;
            }
            if (tracking.Aktif)
            {
                // Sıfır borçlu aktif kart için de aylık kesim olayı vardır.
                var cut = Kesim(today, card.KesimTarihi.Day);
                if (cut >= tracking.Baslangic) Ekstre(db, card, cut);
                var last = Gun(cut.AddMonths(-1), card.KesimTarihi.Day);
                if (last >= tracking.Baslangic) Ekstre(db, card, last);
            }
            // Borçtan önce yatırılmış avans yeni kaynak harcamaya bağlanır. Ödeme
            // tarihi/tutarı değişmez; dağılım bekleyen payın kanalı belli olur.
            foreach (var payment in db.TakipKartOdemeler.Where(p => p.KrediKartiId == card.Id && !p.Iptal).OrderBy(p => p.Id).ToList())
            {
                var pays = Read<KartTaksitPayi>(payment.PaylarJson);
                var advance = pays.Where(p => p.TaksitId == 0).Sum(p => p.Tutar);
                if (advance <= 0) continue;
                var allocated = OdemePaylari(db, card.Id, advance, null, ignoreAdvances: true);
                if (allocated.Any(p => p.TaksitId != 0))
                    payment.PaylarJson = Json(pays.Where(p => p.TaksitId != 0).Concat(allocated));
            }
        }
        db.SaveChanges();
    }
    internal static Dictionary<int, decimal> KalanTaksitler(KasaDbContext db, int cardId, bool ignoreAdvances = false)
    {
        var charges = db.TakipHarcamalar.Where(h => h.KrediKartiId == cardId && !h.Iptal).ToList();
        var ids = charges.Select(h => h.Id).ToArray();
        var taxes = db.TakipKartTaksitler.Where(t => ids.Contains(t.HarcamaId)).ToList();
        var result = taxes.Where(t => t.Tutar > 0).ToDictionary(t => t.Id, t => t.Tutar);
        decimal credit = 0;
        foreach (var payment in db.TakipKartOdemeler.Where(p => p.KrediKartiId == cardId && !p.Iptal).ToList())
            foreach (var pay in Read<KartTaksitPayi>(payment.PaylarJson))
                if (result.ContainsKey(pay.TaksitId)) result[pay.TaksitId] -= pay.Tutar;
                else if (pay.TaksitId == 0 && !ignoreAdvances) credit += pay.Tutar;
        foreach (var refund in charges.Where(h => h.Tutar < 0))
        {
            var amount = -refund.Tutar;
            if (refund.KaynakHarcamaId is { } sourceId)
            {
                foreach (var sourceTax in taxes.Where(t => t.HarcamaId == sourceId).OrderBy(t => t.Id))
                {
                    var applied = Math.Min(amount, Math.Max(0, result.GetValueOrDefault(sourceTax.Id)));
                    result[sourceTax.Id] -= applied; amount -= applied;
                }
                // Ödenmiş kaynak için kalan iade alacak bakiyesidir; başka kanalın
                // harcamasına kendiliğinden atanmaz. Banka ödeme toplamı değişmez.
            }
            else credit += amount; // Açılış alacak bakiyesi.
        }
        foreach (var id in result.Keys.ToArray())
        {
            if (result[id] < 0) { credit -= result[id]; result[id] = 0; }
            var applied = Math.Min(credit, result[id]); result[id] -= applied; credit -= applied;
        }
        return result;
    }
    internal static List<KartTaksitPayi> OdemePaylari(KasaDbContext db, int cardId, decimal amount, int? statementId, bool ignoreAdvances = false)
    {
        var remaining = KalanTaksitler(db, cardId, ignoreAdvances);
        var statements = db.TakipEkstreler.Where(e => e.KrediKartiId == cardId).OrderBy(e => e.SonOdemeTarihi).ThenBy(e => e.Id).ToList();
        if (statementId is { } selected) statements = statements.OrderBy(e => e.Id == selected ? 0 : 1).ToList();
        var result = new List<KartTaksitPayi>(); var left = amount;
        foreach (var statement in statements)
        {
            var taxes = db.TakipKartTaksitler.Where(t => t.EkstreId == statement.Id).ToList();
            var weights = taxes.Where(t => remaining.GetValueOrDefault(t.Id) > 0).Select(t => new KanalPayYaz(t.Id, remaining[t.Id])).ToList();
            var allocated = Math.Min(left, weights.Sum(t => t.Tutar));
            if (allocated <= 0) continue;
            result.AddRange(Oranla(weights, allocated).Select(p => new KartTaksitPayi(p.KanalId, p.Tutar)));
            left -= allocated;
        }
        if (left > 0) result.Add(new(0, left));
        return result;
    }
    // Aynı bozuk pay her rapor isteğinde ve dakikalık bildirim işçisinde yeniden hesaplanır; log taşmasın diye
    // aynı (kart, ödeme, harcama, taşan) uyarısı uygulama başına bir kez yazılır. Üretimde süreçte tek uygulama
    // (tek kök ILoggerFactory) vardır; testlerde her uygulama örneği ayrı sayılır. Önbellek sınırlıdır: dolunca
    // boşaltılır ve uyarılar yeniden birer kez yazılır.
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<(int Kart, int? Odeme, int Harcama, decimal Tasan), byte>> KirpmaUyarilari = new();
    private static readonly object KirpmaVarsayilanKapsam = new();
    private const int KirpmaUyarisiSiniri = 1024;
    private static bool KirpmaIlkKezMi(KasaDbContext db, (int, int?, int, decimal) anahtar)
    {
        var kapsam = (object?)db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?.GetService<ILoggerFactory>() ?? KirpmaVarsayilanKapsam;
        var gorulen = KirpmaUyarilari.GetValue(kapsam, _ => new());
        if (gorulen.Count >= KirpmaUyarisiSiniri) gorulen.Clear();
        return gorulen.TryAdd(anahtar, 0);
    }
    internal static KartOdemeOnizlemeDto OdemeEtkisi(KasaDbContext db, int cardId, IReadOnlyList<KartTaksitPayi> pays, int? beforePaymentId = null)
    {
        var oldPayments = db.TakipKartOdemeler.Where(p => p.KrediKartiId == cardId && !p.Iptal).ToList()
            .Where(p => beforePaymentId is null || p.Id < beforePaymentId).ToList();
        var taxes = db.TakipKartTaksitler.ToDictionary(t => t.Id);
        var charges = db.TakipHarcamalar.Where(h => h.KrediKartiId == cardId).ToDictionary(h => h.Id);
        var previousByCharge = oldPayments.SelectMany(p => Read<KartTaksitPayi>(p.PaylarJson)).Where(p => taxes.ContainsKey(p.TaksitId))
            .GroupBy(p => taxes[p.TaksitId].HarcamaId).ToDictionary(g => g.Key, g => g.Sum(p => p.Tutar));
        var shares = new List<TakipKanalPayi>(); var statementShares = new List<KartEkstreOdemePayi>(); decimal cash = 0;
        foreach (var group in pays.GroupBy(p => taxes.TryGetValue(p.TaksitId, out var t) ? t.HarcamaId : 0))
        {
            var amount = group.Sum(p => p.Tutar);
            if (group.Key == 0) { cash += amount; shares.Add(new(null, Kanallar.DagilimBekliyor, amount)); continue; }
            var charge = charges[group.Key]; var previous = previousByCharge.GetValueOrDefault(charge.Id);
            var credit = Math.Min(amount, Math.Max(0, charge.KasadaOncedenSayilanTutar - previous));
            var effect = amount - credit; cash += effect;
            var source = IadeSonrasiPaylar(db, charge);
            if (source.Count == 0) shares.Add(new(null, Kanallar.DagilimBekliyor, effect));
            else
            {
                var (paylar, tasan) = KirparakOranla(source, effect, Math.Max(0, previous - charge.KasadaOncedenSayilanTutar));
                shares.AddRange(Adlandir(db, paylar));
                if (tasan > 0)
                {
                    // Bozuk/eski ödeme payı bütün raporları 500'e düşürmesin: sığmayan kısım
                    // görünür "Dağılım bekliyor" payı olur ve incelenmek üzere loglanır.
                    shares.Add(new(null, Kanallar.DagilimBekliyor, tasan));
                    if (KirpmaIlkKezMi(db, (cardId, beforePaymentId, charge.Id, tasan)))
                        db.GetService<ILoggerFactory>().CreateLogger(typeof(FinansTakipServisi)).LogWarning(
                            "Kart {KartId} ödeme {OdemeId}: {Tasan} TL kaynak harcama {HarcamaId} kalan ağırlığını aşıyor; fazlası 'Dağılım bekliyor' yazıldı. Ödeme paylarını (PaylarJson) inceleyin.",
                            cardId, beforePaymentId?.ToString() ?? "önizleme", tasan, charge.Id);
                }
            }
            statementShares.AddRange(group.Select(p => new KartEkstreOdemePayi(taxes[p.TaksitId].EkstreId, p.Tutar)));
        }
        return new(pays.Sum(p => p.Tutar), cash,
            shares.GroupBy(p => p.KanalId).Select(g => new TakipKanalPayi(g.Key, g.First().Kanal, g.Sum(p => p.Tutar))).Where(p => p.Tutar != 0).ToList(),
            statementShares.GroupBy(p => p.EkstreId).Select(g => new KartEkstreOdemePayi(g.Key, g.Sum(p => p.Tutar))).ToList());
    }
    public static KartTakipDto Kart(KasaDbContext db, int id)
    {
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == id);
        var track = db.TakipKartlar.AsNoTracking().SingleOrDefault(k => k.KrediKartiId == id);
        if (track is null)
        {
            var debt = card.Borc + db.Islemler.Where(i => i.KrediKartiId == id).ToList().Sum(i => i.TutarTl) - db.KartOdemeler.Where(o => o.KrediKartiId == id).ToList().Sum(o => o.Tutar);
            return new(id, 0, card.Ad, false, true, null, card.KesimTarihi.Day, card.SonOdemeTarihi.Day, card.Limit, debt, debt, [], [], [],
                debt > 0 ? [new(null, Kanallar.DagilimBekliyor, debt)] : []);
        }
        var today = db.Bugunu();
        var charges = db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == id).ToList();
        var taxes = db.TakipKartTaksitler.AsNoTracking().Where(t => charges.Select(h => h.Id).Contains(t.HarcamaId)).ToList();
        var payments = db.TakipKartOdemeler.AsNoTracking().Where(p => p.KrediKartiId == id).OrderBy(p => p.Id).ToList();
        var activeIds = charges.Where(h => !h.Iptal).Select(h => h.Id).ToHashSet();
        var remaining = KalanTaksitler(db, id);
        var paid = payments.Where(p => !p.Iptal).SelectMany(p => Read<KartTaksitPayi>(p.PaylarJson)).GroupBy(p => p.TaksitId).ToDictionary(g => g.Key, g => g.Sum(p => p.Tutar));
        var statements = db.TakipEkstreler.AsNoTracking().Where(s => s.KrediKartiId == id).OrderBy(s => s.KesimTarihi).ToList().Select(s =>
        {
            var rows = taxes.Where(t => t.EkstreId == s.Id && activeIds.Contains(t.HarcamaId)).ToList();
            var statementPaid = rows.Sum(t => paid.GetValueOrDefault(t.Id));
            var statementRemaining = rows.Sum(t => remaining.GetValueOrDefault(t.Id));
            // İade banka ödemesi değildir; asgari hedefinden ödeme düşer, sonuç
            // iade/alacak sonrasındaki gerçek ekstre borcunu aşamaz.
            decimal? minimumRemaining = s.AsgariOdeme is { } minimum
                ? Math.Min(statementRemaining, Math.Max(0, minimum - statementPaid)) : null;
            return new KartEkstreDto(s.Id, s.KesimTarihi, s.SonOdemeTarihi, rows.Sum(t => t.Tutar), statementPaid, statementRemaining, s.AsgariOdeme, minimumRemaining);
        }).ToList();
        var imported = db.EkstreKayitlar.AsNoTracking().Where(k => k.KrediKartiId == id).ToList();
        return new(id, track.Surum, card.Ad, true, track.Aktif, track.Baslangic, card.KesimTarihi.Day, card.SonOdemeTarihi.Day, card.Limit,
            charges.Where(h => !h.Iptal).Sum(h => h.Tutar) - payments.Where(p => !p.Iptal).Sum(p => p.Tutar), statements.Where(s => s.KesimTarihi <= today).Sum(s => s.Kalan), statements,
            charges.Select(h => new KartHarcamaDto(h.Id, h.IslemId, h.Tarih, h.Aciklama, h.Tutar, h.TaksitSayisi, h.Iptal, KaynakPaylari(db, h).Count > 0 ? Adlandir(db, KaynakPaylari(db, h)) : [new(null, Kanallar.DagilimBekliyor, Math.Abs(h.Tutar))], imported.SingleOrDefault(k => k.KartHarcamaId == h.Id)?.Id)).ToList(),
            // İptal edilmiş ödemenin kasa/kanal etkisi yoktur. Payları sonradan girilen iadeyle
            // kaynak ağırlığını aşabileceğinden etkisi hiç hesaplanmaz.
            payments.Select(p =>
            {
                var importId = imported.SingleOrDefault(k => k.KartOdemeId == p.Id)?.Id;
                if (p.Iptal) return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, 0, p.Not, true, [], importId);
                var effect = OdemeEtkisi(db, id, Read<KartTaksitPayi>(p.PaylarJson), p.Id);
                return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, effect.KasaEtkisi, p.Not, false, effect.Dagilimlar, importId);
            }).ToList(),
            KalanKartBorcPaylari(db, charges, taxes, remaining), KartGecisHesabi.Gecis(db, track));
    }

    private static List<TakipKanalPayi> KalanKartBorcPaylari(KasaDbContext db, IReadOnlyList<TakipHarcamaEntity> charges,
        IReadOnlyList<TakipKartTaksitEntity> installments, IReadOnlyDictionary<int, decimal> remaining)
    {
        var shares = new List<KanalPayYaz>();
        decimal unknown = 0;
        foreach (var charge in charges.Where(h => !h.Iptal && h.Tutar > 0))
        {
            var debt = installments.Where(t => t.HarcamaId == charge.Id).Sum(t => remaining.GetValueOrDefault(t.Id));
            if (debt <= 0) continue;
            var weights = IadeSonrasiPaylar(db, charge);
            var total = weights.Sum(p => p.Tutar);
            if (weights.Count == 0 || total < debt) { unknown += debt; continue; }
            // Brüt borcun ödenmemiş son kısmı: kasada daha önce sayılmış devir
            // borcu da ödeme ile kapanır. Nakit paylarını kullanmak bu kısmı
            // yanlış açık bırakır; kalan tutarı sıfırdan oranlamak da kuruşu taşır.
            shares.AddRange(Oranla(weights, debt, total - debt));
        }
        var result = Adlandir(db, shares.GroupBy(p => p.KanalId).OrderBy(g => g.Key)
            .Select(g => new KanalPayYaz(g.Key, g.Sum(p => p.Tutar))));
        if (unknown > 0) result.Add(new(null, Kanallar.DagilimBekliyor, unknown));
        return result;
    }
    public static KrediTakipDto Kredi(KasaDbContext db, int id)
    {
        var today = db.Bugunu();
        var loan = db.Krediler.AsNoTracking().Single(k => k.Id == id);
        var tracking = db.TakipKrediler.AsNoTracking().SingleOrDefault(k => k.KrediId == id);
        if (tracking is null)
        {
            var old = KrediTuretici.TaksitGiderleri(loan.ToCore()).Select((t, i) => new KrediPlanTaksitDto(0, i + 1, t.Tarih, t.TutarTl, t.Tarih <= today ? "KasayaIslendi" : "Bekliyor", null, [])).ToList();
            return new(id, 0, loan.Ad, false, true, null, loan.CekilenTutar, loan.CekimTarihi, old.Where(t => t.Tarih > today).Sum(t => t.Tutar), [], old);
        }
        var installments = db.TakipKrediTaksitler.AsNoTracking().Where(t => t.KrediId == id).OrderBy(t => t.No).ToList();
        return new(id, tracking.Surum, loan.Ad, true, tracking.Aktif, tracking.Baslangic, loan.CekilenTutar, loan.CekimTarihi,
            installments.Where(t => !t.Iptal && t.Tarih > today).Sum(t => t.Tutar), Adlandir(db, Read<KanalPayYaz>(tracking.CekimPaylariJson)),
            installments.Select(t => new KrediPlanTaksitDto(t.Id, t.No, t.Tarih, t.Tutar, t.Iptal ? "Iptal" : t.Tarih <= today ? "KasayaIslendi" : "Bekliyor", t.Not, Adlandir(db, Read<KanalPayYaz>(t.DagilimJson)))).ToList());
    }
    public static IReadOnlyList<TakipOlayDto> GetNotificationEvents(KasaDbContext db, DateOnly today)
    {
        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        Sync(db);
        var result = new List<TakipOlayDto>();
        foreach (var card in db.TakipKartlar.AsNoTracking().ToList())
        {
            var dto = Kart(db, card.KrediKartiId);
            foreach (var s in dto.Ekstreler)
            {
                if (card.Aktif) result.Add(new("Kart", dto.Id, s.Id, dto.Ad, s.KesimTarihi, s.Borc, "Kesim", false));
                if (s.Kalan > 0) result.Add(new("Kart", dto.Id, s.Id, dto.Ad, s.SonOdemeTarihi, s.Kalan, "SonOdeme", false));
            }
        }
        foreach (var loan in db.TakipKrediler.AsNoTracking().ToList())
        {
            var dto = Kredi(db, loan.KrediId);
            result.AddRange(dto.Taksitler.Where(t => t.Durum != "Iptal").Select(t => new TakipOlayDto("Kredi", dto.Id, t.Id, dto.Ad + " / " + t.No + ". taksit", t.Tarih, t.Tutar, "Taksit", true)));
        }
        transaction?.Commit(); return result;
    }
    public static bool KanalKullaniliyor(KasaDbContext db, int id) => db.TakipKrediler.AsNoTracking().AsEnumerable().Any(k => Read<int>(k.KanalIdleriJson).Contains(id))
        || db.TakipHarcamalar.AsNoTracking().AsEnumerable().Any(h => Read<KanalPayYaz>(h.DagilimJson).Any(p => p.KanalId == id));
    internal static bool IslemYonetiliyor(KasaDbContext db, IslemEntity expense) => db.TakipHarcamalar.Any(h => h.IslemId == expense.Id)
        || (expense.KrediKartiId is { } id && db.TakipKartlar.Any(t => t.KrediKartiId == id));
}
