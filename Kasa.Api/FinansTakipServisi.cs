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
    internal static List<TakipKanalPayi> Adlandir(KasaDbContext db, IEnumerable<KanalPayYaz> paylar) =>
        Adlandir(db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad), paylar);
    /// <summary>Kanal adlarını bir kez okunmuş sözlükten verir (istek içinde her pay için Kanallar sorgusu atılmaz).</summary>
    internal static List<TakipKanalPayi> Adlandir(IReadOnlyDictionary<int, string> names, IEnumerable<KanalPayYaz> paylar) =>
        paylar.Select(p => new TakipKanalPayi(p.KanalId, names.GetValueOrDefault(p.KanalId, "Silinmiş kanal"), p.Tutar)).ToList();
    internal static List<KanalPayYaz> KaynakPaylari(KasaDbContext db, TakipHarcamaEntity charge)
    {
        if (charge.IslemId is not { } id) return Read<KanalPayYaz>(charge.DagilimJson);
        var purchaseId = db.AlisOdemeler.Where(o => o.IslemId == id).Select(o => (int?)o.AlisId).FirstOrDefault();
        if (purchaseId is { } alisId) return AlisOdemePayi(AlisEndpoints.Query(db).AsNoTracking().Single(a => a.Id == alisId), id);
        return Read<KanalPayYaz>(charge.DagilimJson);
    }
    /// <summary>Alışa bağlı giderin kanal payı: onaylı alışta ödemenin D'Hondt payı, onaysızda boş (dağılım bekliyor).</summary>
    internal static List<KanalPayYaz> AlisOdemePayi(AlisEntity purchase, int islemId) =>
        AlisHesaplari.OdemeDagilimlari(purchase).TryGetValue(islemId, out var shares)
            ? shares.Select(s => new KanalPayYaz(s.KanalId, s.Tutar)).ToList() : [];
    /// <summary><see cref="KaynakPaylari(KasaDbContext, TakipHarcamaEntity)"/> ile aynı kural; alış bağları kart başına
    /// toplu okunur, sonuç harcama başına bir kez hesaplanır.</summary>
    internal static List<KanalPayYaz> KaynakPaylari(TakipHesapBaglami b, TakipHarcamaEntity charge)
    {
        if (b.KaynakPaylariOnbellegi.TryGetValue(charge.Id, out var memo)) return memo;
        var result = charge.IslemId is { } id && b.KartVerisi(charge.KrediKartiId).AlisPaylari(b).TryGetValue(id, out var shares)
            ? shares : Read<KanalPayYaz>(charge.DagilimJson);
        return b.KaynakPaylariOnbellegi[charge.Id] = result;
    }
    internal static TakipEkstreEntity Ekstre(KasaDbContext db, KrediKartiEntity card, DateOnly cut)
    {
        var existing = db.TakipEkstreler.Local.FirstOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut)
            ?? db.TakipEkstreler.SingleOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut);
        if (existing is not null) return existing;
        existing = new() { KrediKartiId = card.Id, KesimTarihi = cut, SonOdemeTarihi = Vade(cut, card.SonOdemeTarihi.Day) };
        db.TakipEkstreler.Add(existing); db.SaveChanges(); return existing;
    }
    internal static List<KanalPayYaz> IadeSonrasiPaylar(KasaDbContext db, TakipHarcamaEntity charge) =>
        IadeSonrasi(KaynakPaylari(db, charge), db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == charge.Id && !h.Iptal).ToList());
    /// <summary>Aynı kural; kartın iadeleri toplu okunur, sonuç harcama başına bir kez hesaplanır.</summary>
    internal static List<KanalPayYaz> IadeSonrasiPaylar(TakipHesapBaglami b, TakipHarcamaEntity charge)
    {
        if (b.IadeSonrasiOnbellegi.TryGetValue(charge.Id, out var memo)) return memo;
        return b.IadeSonrasiOnbellegi[charge.Id] = IadeSonrasi(KaynakPaylari(b, charge), b.KartVerisi(charge.KrediKartiId).Iadeler(b)[charge.Id]);
    }
    private static List<KanalPayYaz> IadeSonrasi(IReadOnlyList<KanalPayYaz> kaynak, IEnumerable<TakipHarcamaEntity> iadeler)
    {
        var source = kaynak.ToDictionary(p => p.KanalId, p => p.Tutar);
        foreach (var refund in iadeler)
            foreach (var share in Read<KanalPayYaz>(refund.DagilimJson))
                if (source.ContainsKey(share.KanalId)) source[share.KanalId] -= share.Tutar;
        return source.Where(p => p.Value > 0).Select(p => new KanalPayYaz(p.Key, p.Value)).ToList();
    }
    /// <summary>İlk kesim tarihinin kartın düzenli kesiminden en çok uzaklığı (gün): banka kesimi tatil nedeniyle birkaç gün
    /// kayabilir; daha uzak tarih (ör. formdaki varsayılan "bugün") kartın döngüsünde değildir.</summary>
    internal const int IlkKesimToleransi = 7;
    /// <summary><paramref name="date"/>'e en yakın düzenli kesim: kartın <paramref name="day"/> gününe (kısa ayda ay sonuna)
    /// düşen, <paramref name="date"/>'ten önceki ya da o gün/sonraki ilk kesim. Eşit uzaklıkta önceki seçilir.</summary>
    internal static DateOnly EnYakinDuzenliKesim(DateOnly date, int day)
    {
        var next = Kesim(date, day); var previous = Gun(next.AddMonths(-1), day);
        return date.DayNumber - previous.DayNumber <= next.DayNumber - date.DayNumber ? previous : next;
    }
    /// <summary>Harcamayı ve taksitlerini yazar. Her taksit kartın düzenli kesimine (kısa ayda ay sonuna) bağlanır;
    /// <paramref name="firstCut"/> (ilk kesim) yalnız ilk taksidin girdiği döngüyü seçer (finance-3). Bankanın tatil
    /// nedeniyle kaydırdığı kesim (ör. 5 yerine 6'sı) aynı döngünün ekstresidir: o güne ayrı ekstre açılsaydı sonraki
    /// taksitler de o güne sabitlenir, kartın döngüsüne paralel ekstreler ve aynı ay ikinci kesim bildirimi oluşurdu.
    /// İlk kesimsiz harcamanın (gider, açılış, geçiş, masraf, içe aktarma) ataması önceki kuralla aynıdır.
    /// Bilinçli kural: ilk taksidin ekstresi harcamadan önce kesilmiş görünebilir. Banka kesimi ileri kaydırdıysa (ör. 5
    /// Ekim yerine 12'si) aradaki harcama o döngünün ekstresindedir; ekstre kartın düzenli günüyle tutulur, vadesi de ondan
    /// hesaplanır (banka vadeyi de kaydırdıysa hatırlatma erken gelir, geç kalmaz). Pencere dardır: ilk kesim harcamadan
    /// önce olamaz (çağıran denetler) ve düzenli kesimden en çok <see cref="IlkKesimToleransi"/> gün uzaktır. Yuvarlanan
    /// kesim kartın takip başlangıcından önce olamaz: takipten önceki ekstre izlenmez, bakım adımı da açmaz.</summary>
    internal static void HarcamaEkle(KasaDbContext db, KrediKartiEntity card, TakipHarcamaEntity charge, DateOnly? firstCut = null)
    {
        var day = card.KesimTarihi.Day;
        var cut = Kesim(charge.Tarih, day);
        if (firstCut is { } ilk)
        {
            cut = EnYakinDuzenliKesim(ilk, day);
            FinansTakipEndpoints.Require(Math.Abs(ilk.DayNumber - cut.DayNumber) <= IlkKesimToleransi,
                $"İlk kesim tarihi kartın hesap kesim gününe ({day}) en fazla {IlkKesimToleransi} gün uzak olabilir; bankanın kaydırdığı kesimi ya da harcamanın düştüğü sonraki kesimi girin.");
            var baslangic = db.TakipKartlar.Where(t => t.KrediKartiId == card.Id).Select(t => t.Baslangic).Single();
            FinansTakipEndpoints.Require(cut >= baslangic,
                $"İlk kesim tarihi kartın takip başlangıcından ({KartGecisHesabi.Tarih(baslangic)}) önceki {KartGecisHesabi.Tarih(cut)} kesimine denk geliyor; takipten önceki ekstreler izlenmez. Harcamanın düştüğü sonraki kesimi girin ya da ilk kesimi boş bırakın.");
        }
        db.TakipHarcamalar.Add(charge); db.SaveChanges();
        var cents = decimal.ToInt64(Math.Abs(charge.Tutar) * 100); var sign = Math.Sign(charge.Tutar);
        for (var i = 0; i < charge.TaksitSayisi; i++)
        {
            var statement = Ekstre(db, card, Gun(cut.AddMonths(i), day));
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
    /// <summary>Bakım adımı: tarihe bağlı türetmeyi (aktif kartın kesim ekstreleri) ve yazma yollarının türettiği
    /// takip kayıtlarını kalıcı yazar. Kendi kısa yazma transaction'ını açar; okumalar Sync yapmaz ve bunu beklemez.</summary>
    public static void Bakim(KasaDbContext db)
    {
        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        Sync(db);
        transaction?.Commit();
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
    // Yazma yolları (Sync döngüsü, ödeme/iade doğrulaması) izlenen sorgularla okur: bellekte değiştirilmiş
    // ama henüz kaydedilmemiş ödeme payları da görülür.
    internal static Dictionary<int, decimal> KalanTaksitler(KasaDbContext db, int cardId, bool ignoreAdvances = false)
    {
        var charges = db.TakipHarcamalar.Where(h => h.KrediKartiId == cardId && !h.Iptal).ToList();
        var ids = charges.Select(h => h.Id).ToArray();
        var taxes = db.TakipKartTaksitler.Where(t => ids.Contains(t.HarcamaId)).ToList();
        var payments = db.TakipKartOdemeler.Where(p => p.KrediKartiId == cardId && !p.Iptal).ToList().Select(p => Read<KartTaksitPayi>(p.PaylarJson));
        return KalanTaksitleriHesapla(charges, taxes, payments, ignoreAdvances);
    }
    /// <summary>Taksit başına kalan borç (saf hesap). <paramref name="charges"/> iptal edilmemiş harcamalar,
    /// <paramref name="taxes"/> onların taksitleri (sorgu sırasıyla: alacak bu sırayla uygulanır),
    /// <paramref name="paymentPays"/> iptal edilmemiş ödemelerin payları.</summary>
    internal static Dictionary<int, decimal> KalanTaksitleriHesapla(IReadOnlyList<TakipHarcamaEntity> charges, IReadOnlyList<TakipKartTaksitEntity> taxes,
        IEnumerable<IReadOnlyList<KartTaksitPayi>> paymentPays, bool ignoreAdvances)
    {
        var result = taxes.Where(t => t.Tutar > 0).ToDictionary(t => t.Id, t => t.Tutar);
        decimal credit = 0;
        foreach (var pays in paymentPays)
            foreach (var pay in pays)
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
    /// <summary>Ödeme önizlemesi/anlık görüntüsü için tek ödemenin etkisi (yazma yolları). Her çağrı taze, izlemeli
    /// bir bağlam kurar; taksitler yalnız bu kartın harcamalarınınkilerdir (bütün kartların tablosu okunmaz).</summary>
    internal static KartOdemeOnizlemeDto OdemeEtkisi(KasaDbContext db, int cardId, IReadOnlyList<KartTaksitPayi> pays, int? beforePaymentId = null)
    {
        var b = new TakipHesapBaglami(db, default, izle: true);
        var v = b.KartVerisi(cardId);
        var previousByCharge = v.Odemeler.Where(p => !p.Iptal && (beforePaymentId is null || p.Id < beforePaymentId))
            .SelectMany(p => v.OdemePaylari[p.Id]).Where(p => v.TaksitById.ContainsKey(p.TaksitId))
            .GroupBy(p => v.TaksitById[p.TaksitId].HarcamaId).ToDictionary(g => g.Key, g => g.Sum(p => p.Tutar));
        return OdemeEtkisi(b, cardId, pays, previousByCharge, beforePaymentId);
    }
    /// <summary>Ödemenin kasa ve kanal etkisi. <paramref name="previousByCharge"/>: bu ödemeden önceki (Id sırası)
    /// iptal edilmemiş ödemelerin harcama başına toplamı. Başka bir kartın taksidine işaret eden (bozuk) pay bu kartın
    /// harcamasına bağlanamadığından "Dağılım bekliyor" nakit payı olur; eskiden bütün hesabı 500'e düşürüyordu.</summary>
    private static KartOdemeOnizlemeDto OdemeEtkisi(TakipHesapBaglami b, int cardId, IReadOnlyList<KartTaksitPayi> pays,
        IReadOnlyDictionary<int, decimal> previousByCharge, int? beforePaymentId)
    {
        var v = b.KartVerisi(cardId); var taxes = v.TaksitById; var charges = v.HarcamaById; var db = b.Db;
        var shares = new List<TakipKanalPayi>(); var statementShares = new List<KartEkstreOdemePayi>(); decimal cash = 0;
        foreach (var group in pays.GroupBy(p => taxes.TryGetValue(p.TaksitId, out var t) ? t.HarcamaId : 0))
        {
            var amount = group.Sum(p => p.Tutar);
            if (group.Key == 0) { cash += amount; shares.Add(new(null, Kanallar.DagilimBekliyor, amount)); continue; }
            var charge = charges[group.Key]; var previous = previousByCharge.GetValueOrDefault(charge.Id);
            var credit = Math.Min(amount, Math.Max(0, charge.KasadaOncedenSayilanTutar - previous));
            var effect = amount - credit; cash += effect;
            var source = IadeSonrasiPaylar(b, charge);
            if (source.Count == 0) shares.Add(new(null, Kanallar.DagilimBekliyor, effect));
            else
            {
                var (paylar, tasan) = KirparakOranla(source, effect, Math.Max(0, previous - charge.KasadaOncedenSayilanTutar));
                shares.AddRange(Adlandir(b.KanalAdlari, paylar));
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
    /// <summary>Kartın iptal edilmemiş bütün ödemelerinin etkisi (ödeme Id → etki), bağlam içinde bir kez. Ödemeler Id
    /// sırasıyla işlenir; önceki ödemelerin harcama başına toplamı yürüyen toplamla tutulur (her ödemede yeniden okunmaz).</summary>
    private static IReadOnlyDictionary<int, KartOdemeOnizlemeDto> OdemeEtkileri(TakipHesapBaglami b, int cardId)
    {
        if (b.OdemeEtkileriOnbellegi.TryGetValue(cardId, out var memo)) return memo;
        var v = b.KartVerisi(cardId);
        var result = new Dictionary<int, KartOdemeOnizlemeDto>();
        var previousByCharge = new Dictionary<int, decimal>();
        foreach (var payment in v.Odemeler.Where(p => !p.Iptal))
        {
            b.Iptal.ThrowIfCancellationRequested();
            var pays = v.OdemePaylari[payment.Id];
            result[payment.Id] = OdemeEtkisi(b, cardId, pays, previousByCharge, payment.Id);
            foreach (var pay in pays)
                if (v.TaksitById.TryGetValue(pay.TaksitId, out var tax))
                    previousByCharge[tax.HarcamaId] = previousByCharge.GetValueOrDefault(tax.HarcamaId) + pay.Tutar;
        }
        return b.OdemeEtkileriOnbellegi[cardId] = result;
    }
    /// <summary>Kasa raporu için takipli kartın iptal edilmemiş ödemelerinin kanal payları (Id sırasıyla). Tam kart
    /// DTO'su (ekstre, harcama, kalan borç payları) hesaplanmaz.</summary>
    internal static IEnumerable<(DateOnly Tarih, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar)> KartOdemeDagilimlari(TakipHesapBaglami b, int cardId)
    {
        var v = b.KartVerisi(cardId); var effects = OdemeEtkileri(b, cardId);
        return v.Odemeler.Where(p => !p.Iptal).Select(p => (p.Tarih, p.Not, effects[p.Id].Dagilimlar)).ToList();
    }
    public static KartTakipDto Kart(KasaDbContext db, int id) => Kart(new TakipHesapBaglami(db, default, izle: true), id);
    /// <summary>Kart DTO'su; kart verisi bağlamdan (kart başına sabit sayıda sorgu) okunur.</summary>
    public static KartTakipDto Kart(TakipHesapBaglami b, int id)
    {
        b.Iptal.ThrowIfCancellationRequested();
        var db = b.Db;
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == id);
        var track = db.TakipKartlar.AsNoTracking().SingleOrDefault(k => k.KrediKartiId == id);
        if (track is null)
        {
            var debt = card.Borc + b.Sorgu(db.Islemler).Where(i => i.KrediKartiId == id).ToList().Sum(i => i.TutarTl) - b.Sorgu(db.KartOdemeler).Where(o => o.KrediKartiId == id).ToList().Sum(o => o.Tutar);
            return new(id, 0, card.Ad, false, true, null, card.KesimTarihi.Day, card.SonOdemeTarihi.Day, card.Limit, debt, debt, [], [], [],
                debt > 0 ? [new(null, Kanallar.DagilimBekliyor, debt)] : []);
        }
        var today = b.Bugun;
        var v = b.KartVerisi(id);
        var charges = v.Harcamalar; var taxes = v.Taksitler; var payments = v.Odemeler;
        var activeIds = charges.Where(h => !h.Iptal).Select(h => h.Id).ToHashSet();
        var activeTaxes = taxes.Where(t => activeIds.Contains(t.HarcamaId)).ToList();
        var remaining = KalanTaksitleriHesapla(charges.Where(h => !h.Iptal).ToList(), activeTaxes, payments.Where(p => !p.Iptal).Select(p => v.OdemePaylari[p.Id]), false);
        var paid = payments.Where(p => !p.Iptal).SelectMany(p => v.OdemePaylari[p.Id]).GroupBy(p => p.TaksitId).ToDictionary(g => g.Key, g => g.Sum(p => p.Tutar));
        var byStatement = activeTaxes.ToLookup(t => t.EkstreId);
        var statements = db.TakipEkstreler.AsNoTracking().Where(s => s.KrediKartiId == id).OrderBy(s => s.KesimTarihi).ToList().Select(s =>
        {
            var rows = byStatement[s.Id].ToList();
            var statementPaid = rows.Sum(t => paid.GetValueOrDefault(t.Id));
            var statementRemaining = rows.Sum(t => remaining.GetValueOrDefault(t.Id));
            // İade banka ödemesi değildir; asgari hedefinden ödeme düşer, sonuç
            // iade/alacak sonrasındaki gerçek ekstre borcunu aşamaz.
            decimal? minimumRemaining = s.AsgariOdeme is { } minimum
                ? Math.Min(statementRemaining, Math.Max(0, minimum - statementPaid)) : null;
            return new KartEkstreDto(s.Id, s.KesimTarihi, s.SonOdemeTarihi, rows.Sum(t => t.Tutar), statementPaid, statementRemaining, s.AsgariOdeme, minimumRemaining);
        }).ToList();
        statements = TarihTuretilmisEkstreler(statements, card, track, today);
        var imported = db.EkstreKayitlar.AsNoTracking().Where(k => k.KrediKartiId == id).ToList();
        var importedCharges = imported.Where(k => k.KartHarcamaId != null).ToLookup(k => k.KartHarcamaId!.Value);
        var importedPayments = imported.Where(k => k.KartOdemeId != null).ToLookup(k => k.KartOdemeId!.Value);
        var effects = OdemeEtkileri(b, id);
        return new(id, track.Surum, card.Ad, true, track.Aktif, track.Baslangic, card.KesimTarihi.Day, card.SonOdemeTarihi.Day, card.Limit,
            charges.Where(h => !h.Iptal).Sum(h => h.Tutar) - payments.Where(p => !p.Iptal).Sum(p => p.Tutar), statements.Where(s => s.KesimTarihi <= today).Sum(s => s.Kalan), statements,
            charges.Select(h => { var source = KaynakPaylari(b, h); return new KartHarcamaDto(h.Id, h.IslemId, h.Tarih, h.Aciklama, h.Tutar, h.TaksitSayisi, h.Iptal,
                source.Count > 0 ? Adlandir(b.KanalAdlari, source) : [new(null, Kanallar.DagilimBekliyor, Math.Abs(h.Tutar))], importedCharges[h.Id].SingleOrDefault()?.Id); }).ToList(),
            // İptal edilmiş ödemenin kasa/kanal etkisi yoktur. Payları sonradan girilen iadeyle
            // kaynak ağırlığını aşabileceğinden etkisi hiç hesaplanmaz.
            payments.Select(p =>
            {
                var importId = importedPayments[p.Id].SingleOrDefault()?.Id;
                if (p.Iptal) return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, 0, p.Not, true, [], importId);
                var effect = effects[p.Id];
                return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, effect.KasaEtkisi, p.Not, false, effect.Dagilimlar, importId);
            }).ToList(),
            KalanKartBorcPaylari(b, charges, taxes, remaining), KartGecisHesabi.Gecis(db, track));
    }
    /// <summary>Tarihe bağlı ekstreler okumada yazılmadan türetilir. Aktif kartın bugünkü ve önceki kesim ekstresini
    /// Sync (bakım adımı) yazar; henüz yazılmamışsa (gün dönümünden sonra bakım çalışmadan) aynı tarihli boş ekstre
    /// kimliksiz (Id 0) eklenir. Böyle bir ekstrede taksit olamaz: taksit yazan her yol ekstresini de yazar.</summary>
    private static List<KartEkstreDto> TarihTuretilmisEkstreler(List<KartEkstreDto> statements, KrediKartiEntity card, TakipKartEntity track, DateOnly today)
    {
        if (!track.Aktif) return statements;
        var cut = Kesim(today, card.KesimTarihi.Day);
        var missing = new[] { cut, Gun(cut.AddMonths(-1), card.KesimTarihi.Day) }
            .Where(d => d >= track.Baslangic && statements.All(s => s.KesimTarihi != d)).Distinct().ToList();
        if (missing.Count == 0) return statements;
        return statements.Concat(missing.Select(d => new KartEkstreDto(0, d, Vade(d, card.SonOdemeTarihi.Day), 0, 0, 0, null, null)))
            .OrderBy(s => s.KesimTarihi).ToList();
    }

    private static List<TakipKanalPayi> KalanKartBorcPaylari(TakipHesapBaglami b, IReadOnlyList<TakipHarcamaEntity> charges,
        IReadOnlyList<TakipKartTaksitEntity> installments, IReadOnlyDictionary<int, decimal> remaining)
    {
        var shares = new List<KanalPayYaz>();
        decimal unknown = 0;
        var byCharge = installments.ToLookup(t => t.HarcamaId);
        foreach (var charge in charges.Where(h => !h.Iptal && h.Tutar > 0))
        {
            var debt = byCharge[charge.Id].Sum(t => remaining.GetValueOrDefault(t.Id));
            if (debt <= 0) continue;
            var weights = IadeSonrasiPaylar(b, charge);
            var total = weights.Sum(p => p.Tutar);
            if (weights.Count == 0 || total < debt) { unknown += debt; continue; }
            // Brüt borcun ödenmemiş son kısmı: kasada daha önce sayılmış devir
            // borcu da ödeme ile kapanır. Nakit paylarını kullanmak bu kısmı
            // yanlış açık bırakır; kalan tutarı sıfırdan oranlamak da kuruşu taşır.
            shares.AddRange(Oranla(weights, debt, total - debt));
        }
        var result = Adlandir(b.KanalAdlari, shares.GroupBy(p => p.KanalId).OrderBy(g => g.Key)
            .Select(g => new KanalPayYaz(g.Key, g.Sum(p => p.Tutar))));
        if (unknown > 0) result.Add(new(null, Kanallar.DagilimBekliyor, unknown));
        return result;
    }
    public static KrediTakipDto Kredi(KasaDbContext db, int id) => Kredi(new TakipHesapBaglami(db, default, izle: true), id);
    /// <summary>Kredi DTO'su; taksit payları bağlamın bir kez okunmuş kanal adlarıyla adlandırılır.</summary>
    public static KrediTakipDto Kredi(TakipHesapBaglami b, int id)
    {
        b.Iptal.ThrowIfCancellationRequested();
        var db = b.Db; var today = b.Bugun;
        var loan = db.Krediler.AsNoTracking().Single(k => k.Id == id);
        var tracking = db.TakipKrediler.AsNoTracking().SingleOrDefault(k => k.KrediId == id);
        if (tracking is null)
        {
            var old = KrediTuretici.TaksitGiderleri(loan.ToCore()).Select((t, i) => new KrediPlanTaksitDto(0, i + 1, t.Tarih, t.TutarTl, t.Tarih <= today ? "KasayaIslendi" : "Bekliyor", null, [])).ToList();
            return new(id, 0, loan.Ad, false, true, null, loan.CekilenTutar, loan.CekimTarihi, old.Where(t => t.Tarih > today).Sum(t => t.Tutar), [], old);
        }
        var installments = db.TakipKrediTaksitler.AsNoTracking().Where(t => t.KrediId == id).OrderBy(t => t.No).ToList();
        return new(id, tracking.Surum, loan.Ad, true, tracking.Aktif, tracking.Baslangic, loan.CekilenTutar, loan.CekimTarihi,
            installments.Where(t => !t.Iptal && t.Tarih > today).Sum(t => t.Tutar), Adlandir(b.KanalAdlari, Read<KanalPayYaz>(tracking.CekimPaylariJson)),
            installments.Select(t => new KrediPlanTaksitDto(t.Id, t.No, t.Tarih, t.Tutar, t.Iptal ? "Iptal" : t.Tarih <= today ? "KasayaIslendi" : "Bekliyor", t.Not, Adlandir(b.KanalAdlari, Read<KanalPayYaz>(t.DagilimJson)))).ToList());
    }
    /// <summary>Bildirim işinin olayları: yazma yolu olarak önce Sync yapar (kendi transaction'ında), sonra olayları
    /// <see cref="TakipOlaylari"/> ile türetir.</summary>
    public static IReadOnlyList<TakipOlayDto> GetNotificationEvents(KasaDbContext db, DateOnly today)
    {
        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        Sync(db);
        var result = TakipOlaylari(new TakipHesapBaglami(db));
        transaction?.Commit(); return result;
    }
    /// <summary>Takipli kartların kesim/son ödeme ve takipli kredilerin taksit olayları (yazmaz). Aynı istekte hesaplanmış
    /// kart/kredi DTO'ları verilirse yeniden hesaplanmaz. Sıra: takip kayıtlarının sırası, ekstre/taksit sırası.</summary>
    internal static List<TakipOlayDto> TakipOlaylari(TakipHesapBaglami b, IReadOnlyDictionary<int, KartTakipDto>? kartlar = null,
        IReadOnlyDictionary<int, KrediTakipDto>? krediler = null)
    {
        var result = new List<TakipOlayDto>();
        foreach (var card in b.Db.TakipKartlar.AsNoTracking().ToList())
            result.AddRange(KartOlaylari(kartlar?.GetValueOrDefault(card.KrediKartiId) ?? Kart(b, card.KrediKartiId), card.Aktif));
        foreach (var loan in b.Db.TakipKrediler.AsNoTracking().ToList())
            result.AddRange(KrediOlaylari(krediler?.GetValueOrDefault(loan.KrediId) ?? Kredi(b, loan.KrediId)));
        return result;
    }
    /// <summary>Bir takipli kartın olayları; takip özeti ve bildirim işçisi (<see cref="Servisler.FinansBildirimKaynaklari"/>)
    /// için tek kural: aktif kartın her ekstresi için kesim, kalanı olan ekstre için son ödeme (pasif kartın borcu da hatırlatılır).</summary>
    internal static IEnumerable<TakipOlayDto> KartOlaylari(KartTakipDto dto, bool aktif)
    {
        foreach (var s in dto.Ekstreler)
        {
            if (aktif) yield return new("Kart", dto.Id, s.Id, dto.Ad, s.KesimTarihi, s.Borc, "Kesim", false);
            if (s.Kalan > 0) yield return new("Kart", dto.Id, s.Id, dto.Ad, s.SonOdemeTarihi, s.Kalan, "SonOdeme", false);
        }
    }
    /// <summary>Bir takipli kredinin olayları (tek kural): iptal edilmemiş her taksit, kasaya otomatik işlenir.</summary>
    internal static IEnumerable<TakipOlayDto> KrediOlaylari(KrediTakipDto dto) => dto.Taksitler.Where(t => t.Durum != "Iptal")
        .Select(t => new TakipOlayDto("Kredi", dto.Id, t.Id, dto.Ad + " / " + t.No + ". taksit", t.Tarih, t.Tutar, "Taksit", true));
    public static bool KanalKullaniliyor(KasaDbContext db, int id) => db.TakipKrediler.AsNoTracking().AsEnumerable().Any(k => Read<int>(k.KanalIdleriJson).Contains(id))
        || db.TakipHarcamalar.AsNoTracking().AsEnumerable().Any(h => Read<KanalPayYaz>(h.DagilimJson).Any(p => p.KanalId == id));
    internal static bool IslemYonetiliyor(KasaDbContext db, IslemEntity expense) => db.TakipHarcamalar.Any(h => h.IslemId == expense.Id)
        || (expense.KrediKartiId is { } id && db.TakipKartlar.Any(t => t.KrediKartiId == id));
}

/// <summary>
/// İstek ömürlü, yan etkisiz kart/kredi hesap bağlamı (finance-5/6, gap-okuma-yolu-maliyet-kilit-cekismesi-1). Kanal adları
/// istek başına, bir kartın harcama/taksit/ödeme/iade/alış bağı verisi kart başına birer toplu sorguyla bir kez okunur;
/// kaynak payları, iade sonrası paylar ve ödeme etkileri harcama/kart başına bir kez hesaplanır. Böylece sorgu sayısı
/// ödeme, harcama ve taksit sayısıyla büyümez; taksit tablosu yalnız ilgili kartın harcamalarına süzülerek okunur.
/// Okuma yolunda izlemesiz çalışır ve istek (tek okuma anlık görüntüsü) boyunca paylaşılır; veri değiştiren kod bağlamı
/// yeniden kurmalıdır. Yazma yollarının eski <c>(KasaDbContext db, ...)</c> imzaları her çağrıda taze, izlemeli bir bağlam
/// kurar: bellekte değiştirilmiş ama kaydedilmemiş kayıtlar eskisi gibi görünür. Uzun döngüler <see cref="Iptal"/>'i denetler.
/// </summary>
public sealed class TakipHesapBaglami
{
    public TakipHesapBaglami(KasaDbContext db, CancellationToken iptal = default) : this(db, iptal, izle: false) { }
    internal TakipHesapBaglami(KasaDbContext db, CancellationToken iptal, bool izle) { Db = db; Iptal = iptal; _izle = izle; }

    internal KasaDbContext Db { get; }
    public CancellationToken Iptal { get; }
    private readonly bool _izle;
    private DateOnly? _bugun;
    /// <summary>Hesabın günü (<c>db.Bugunu()</c>): bağlamın ilk kullanımında bir kez okunur, istek boyunca aynı kalır.</summary>
    public DateOnly Bugun => _bugun ??= Db.Bugunu();
    private Dictionary<int, string>? _kanalAdlari;
    internal IReadOnlyDictionary<int, string> KanalAdlari => _kanalAdlari ??= Db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
    internal IQueryable<T> Sorgu<T>(IQueryable<T> sorgu) where T : class => _izle ? sorgu : sorgu.AsNoTracking();

    private readonly Dictionary<int, KartHesapVerisi> _kartlar = [];
    internal KartHesapVerisi KartVerisi(int kartId)
    {
        if (!_kartlar.TryGetValue(kartId, out var veri)) _kartlar[kartId] = veri = new(this, kartId);
        return veri;
    }
    internal Dictionary<int, List<KanalPayYaz>> KaynakPaylariOnbellegi { get; } = [];
    internal Dictionary<int, List<KanalPayYaz>> IadeSonrasiOnbellegi { get; } = [];
    internal Dictionary<int, IReadOnlyDictionary<int, KartOdemeOnizlemeDto>> OdemeEtkileriOnbellegi { get; } = [];
}

/// <summary>Bir kartın hesap verisi: iptal dahil harcamaları, yalnız bu harcamaların taksitleri, Id sıralı ödemeleri ve
/// ayrıştırılmış ödeme payları; iadeler ve alış bağları ilk gerektiğinde. Sorgu biçimleri (süzgeç ve sıra) eski kart
/// hesabınınkiyle aynıdır: alacağın taksitlere uygulanma sırası değişmez.</summary>
internal sealed class KartHesapVerisi
{
    public KartHesapVerisi(TakipHesapBaglami b, int kartId)
    {
        b.Iptal.ThrowIfCancellationRequested();
        var db = b.Db;
        Harcamalar = b.Sorgu(db.TakipHarcamalar).Where(h => h.KrediKartiId == kartId).ToList();
        HarcamaById = Harcamalar.ToDictionary(h => h.Id);
        _harcamaIdleri = Harcamalar.Select(h => h.Id).ToArray();
        var ids = _harcamaIdleri;
        Taksitler = b.Sorgu(db.TakipKartTaksitler).Where(t => ids.Contains(t.HarcamaId)).ToList();
        TaksitById = Taksitler.ToDictionary(t => t.Id);
        Odemeler = b.Sorgu(db.TakipKartOdemeler).Where(p => p.KrediKartiId == kartId).OrderBy(p => p.Id).ToList();
        OdemePaylari = Odemeler.ToDictionary(p => p.Id, p => (IReadOnlyList<KartTaksitPayi>)FinansTakipServisi.Read<KartTaksitPayi>(p.PaylarJson));
    }

    private readonly int[] _harcamaIdleri;
    public List<TakipHarcamaEntity> Harcamalar { get; }
    public Dictionary<int, TakipHarcamaEntity> HarcamaById { get; }
    public List<TakipKartTaksitEntity> Taksitler { get; }
    public Dictionary<int, TakipKartTaksitEntity> TaksitById { get; }
    public List<TakipKartOdemeEntity> Odemeler { get; }
    public Dictionary<int, IReadOnlyList<KartTaksitPayi>> OdemePaylari { get; }

    private ILookup<int, TakipHarcamaEntity>? _iadeler;
    /// <summary>Kaynak harcama Id'sine göre iptal edilmemiş iadeler (kart süzgeci yok: eski tek harcamalık sorguyla aynı küme).</summary>
    public ILookup<int, TakipHarcamaEntity> Iadeler(TakipHesapBaglami b)
    {
        if (_iadeler is not null) return _iadeler;
        var ids = _harcamaIdleri;
        return _iadeler = b.Sorgu(b.Db.TakipHarcamalar).Where(h => h.KaynakHarcamaId != null && ids.Contains(h.KaynakHarcamaId.Value) && !h.Iptal)
            .ToList().ToLookup(h => h.KaynakHarcamaId!.Value);
    }

    private Dictionary<int, List<KanalPayYaz>>? _alisPaylari;
    /// <summary>Alışa bağlı giderden gelen harcamaların payı (gider Id → pay): bağlar tek sorguda, alışlar tek (bölünmüş)
    /// sorguda okunur; alış başına ödeme dağılımı bir kez hesaplanır. Alışa bağlı olmayan gider sözlükte yoktur.</summary>
    public IReadOnlyDictionary<int, List<KanalPayYaz>> AlisPaylari(TakipHesapBaglami b)
    {
        if (_alisPaylari is not null) return _alisPaylari;
        var islemIds = Harcamalar.Where(h => h.IslemId != null).Select(h => h.IslemId!.Value).Distinct().ToArray();
        if (islemIds.Length == 0) return _alisPaylari = [];
        var db = b.Db;
        var baglar = db.AlisOdemeler.Where(o => islemIds.Contains(o.IslemId)).Select(o => new { o.IslemId, o.AlisId }).ToList();
        var alisIds = baglar.Select(x => x.AlisId).Distinct().ToArray();
        var alislar = alisIds.Length == 0 ? [] : db.Alislar.AsNoTracking().Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem).Where(a => alisIds.Contains(a.Id)).AsSplitQuery().ToDictionary(a => a.Id);
        return _alisPaylari = baglar.ToDictionary(x => x.IslemId, x => FinansTakipServisi.AlisOdemePayi(alislar[x.AlisId], x.IslemId));
    }
}
