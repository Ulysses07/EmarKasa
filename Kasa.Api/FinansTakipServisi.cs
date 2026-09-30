using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
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
        var before = decimal.ToInt64(onceki * 100);
        var after = decimal.ToInt64((onceki + amount) * 100);
        return sorted.Select((id, i) => new KanalPayYaz(id,
            (after / sorted.Length + (i < after % sorted.Length ? 1 : 0) - before / sorted.Length - (i < before % sorted.Length ? 1 : 0)) / 100m)).ToList();
    }
    internal static List<KanalPayYaz> Oranla(IReadOnlyList<KanalPayYaz> weights, decimal amount, decimal onceki = 0)
    {
        var positive = weights.Where(w => w.Tutar > 0).ToList();
        if (positive.Count == 0 || amount <= 0)
            return [];
        return AlisDagitici.Dagit(positive.Select(w => new AlisKanalPayi(w.KanalId, w.Tutar)).ToList(), onceki, amount)
            .Select(p => new KanalPayYaz(p.KanalId, p.Tutar)).Where(p => p.Tutar > 0).ToList();
    }
    /// <summary>Oranla'nın taşmaya dayanıklı biçimi. onceki+tutar pozitif ağırlık toplamını
    /// aşmıyorsa sonuç Oranla ile birebir aynıdır ve Tasan 0'dır. Aşıyorsa (Oranla'nın istisna
    /// fırlattığı bozuk/eski veri) yalnız sığan kısım dağıtılır, fazlası Tasan olarak döner.</summary>
    public static (List<KanalPayYaz> Paylar, decimal Tasan) KirparakOranla(IReadOnlyList<KanalPayYaz> weights, decimal amount, decimal onceki = 0)
    {
        var positive = weights.Where(w => w.Tutar > 0).ToList();
        if (positive.Count == 0 || amount <= 0)
            return ([], 0);
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
        if (charge.IslemId is not { } id)
            return Read<KanalPayYaz>(charge.DagilimJson);
        var purchaseId = db.AlisOdemeler.Where(o => o.IslemId == id).Select(o => (int?)o.AlisId).FirstOrDefault();
        if (purchaseId is { } alisId)
            return AlisOdemePayi(AlisEndpoints.Query(db).AsNoTracking().Single(a => a.Id == alisId), id);
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
        if (b.KaynakPaylariOnbellegi.TryGetValue(charge.Id, out var memo))
            return memo;
        var result = charge.IslemId is { } id && b.KartVerisi(charge.KrediKartiId).AlisPaylari(b).TryGetValue(id, out var shares)
            ? shares : Read<KanalPayYaz>(charge.DagilimJson);
        return b.KaynakPaylariOnbellegi[charge.Id] = result;
    }
    internal static TakipEkstreEntity Ekstre(KasaDbContext db, KrediKartiEntity card, DateOnly cut)
    {
        var existing = db.TakipEkstreler.Local.FirstOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut)
            ?? db.TakipEkstreler.SingleOrDefault(s => s.KrediKartiId == card.Id && s.KesimTarihi == cut);
        if (existing is not null)
            return existing;
        existing = new() { KrediKartiId = card.Id, KesimTarihi = cut, SonOdemeTarihi = Vade(cut, card.SonOdemeTarihi.Day) };
        db.TakipEkstreler.Add(existing);
        db.SaveChanges();
        return existing;
    }
    internal static List<KanalPayYaz> IadeSonrasiPaylar(KasaDbContext db, TakipHarcamaEntity charge) => IadeDagilimiOku(db, charge).Kalan;
    /// <summary>İzlemeli (yazma yolu) iade hesabı: bellekte değiştirilmiş kayıtlar da görülür.</summary>
    internal static IadeDagilimi IadeDagilimiOku(KasaDbContext db, TakipHarcamaEntity charge)
    {
        var iadeler = db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == charge.Id && !h.Iptal).ToList();
        return IadeHesabi(KaynakPaylari(db, charge), iadeler, IadeHesaplariOku(db.TakipIadeHesaplari, iadeler));
    }
    internal static Dictionary<int, TakipIadeHesabiEntity> IadeHesaplariOku(IQueryable<TakipIadeHesabiEntity> tablo, IReadOnlyCollection<TakipHarcamaEntity> iadeler)
    {
        if (iadeler.Count == 0)
            return [];
        var ids = iadeler.Select(h => h.Id).ToArray();
        return tablo.Where(x => ids.Contains(x.HarcamaId)).ToDictionary(x => x.HarcamaId);
    }
    /// <summary>Aynı kural; kartın iadeleri toplu okunur, sonuç harcama başına bir kez hesaplanır.</summary>
    internal static List<KanalPayYaz> IadeSonrasiPaylar(TakipHesapBaglami b, TakipHarcamaEntity charge) => IadeDagilimiOku(b, charge).Kalan;
    internal static IadeDagilimi IadeDagilimiOku(TakipHesapBaglami b, TakipHarcamaEntity charge)
    {
        if (b.IadeDagilimiOnbellegi.TryGetValue(charge.Id, out var memo))
            return memo;
        var v = b.KartVerisi(charge.KrediKartiId);
        return b.IadeDagilimiOnbellegi[charge.Id] = IadeHesabi(KaynakPaylari(b, charge), v.Iadeler(b)[charge.Id], v.IadeHesaplari(b));
    }
    /// <summary>İade sonrası kaynak payı ve iade başına pay.</summary>
    /// <param name="Kalan">Kaynak payından iadeler düşüldükten sonra kalan (pozitif) kanal payları.</param>
    /// <param name="Paylar">İptal edilmemiş iade Id → o iadenin kanal payı.</param>
    internal sealed record IadeDagilimi(List<KanalPayYaz> Kalan, IReadOnlyDictionary<int, List<KanalPayYaz>> Paylar);
    /// <summary>İade sonrası kaynak payı ve iade başına pay. Kart ödemesinin kanal etkisi, kalan kart borcu, iade doğrulaması
    /// ve iade satırının gösterimi bu tek hesaptan okunur (gap-coklu-giris-cift-sayim-mutabakat-3). İadeler Id sırasıyla
    /// işlenir: hesap kaydı olan iadenin payı o anki kalan kaynaktan, iade anındaki ödenmiş kısım düşülerek her okumada
    /// türetilir (<see cref="IadePayi"/>); kaynak alış yeniden dağıtılınca ya da gider sonradan alışa bağlanınca iade yeni
    /// oranı izler. Hesap kaydı olmayan (bu sürümden önceki, veri adımında eşleşmeyen) iadenin dondurulmuş payı eski kuralla
    /// aynen düşülür: bugünkü raporlar değişmez. Türetilen pay kaynağa sığmıyorsa (tutarsız alış verisi) kalan boştur:
    /// harcamanın payları "Dağılım bekliyor" olur, para yanlış kanala yazılmaz.</summary>
    internal static IadeDagilimi IadeHesabi(IReadOnlyList<KanalPayYaz> kaynak, IEnumerable<TakipHarcamaEntity> iadeler, IReadOnlyDictionary<int, TakipIadeHesabiEntity> hesaplar)
    {
        var source = kaynak.ToDictionary(p => p.KanalId, p => p.Tutar);
        var paylar = new Dictionary<int, List<KanalPayYaz>>();
        var tutarsiz = false;
        foreach (var refund in iadeler.OrderBy(h => h.Id))
        {
            List<KanalPayYaz> pay;
            if (hesaplar.TryGetValue(refund.Id, out var hesap))
            {
                pay = IadePayi(source.Where(p => p.Value > 0).Select(p => new KanalPayYaz(p.Key, p.Value)).ToList(), hesap.IadeAnindaOdenen, -refund.Tutar, out var tasan);
                tutarsiz |= tasan > 0;
            }
            else
                pay = Read<KanalPayYaz>(refund.DagilimJson);
            paylar[refund.Id] = pay;
            foreach (var share in pay)
                if (source.ContainsKey(share.KanalId))
                    source[share.KanalId] -= share.Tutar;
        }
        return new(tutarsiz ? [] : source.Where(p => p.Value > 0).Select(p => new KanalPayYaz(p.Key, p.Value)).ToList(), paylar);
    }
    /// <summary>İadenin kanal payı (iade kaydında ve her okumada aynı kural): kaynak payının iade anında ödenmemiş kısmından
    /// oranla. <paramref name="tasan"/>: kaynak payına sığmayan tutar; 0 değilse pay kaynakla tutarsızdır.</summary>
    public static List<KanalPayYaz> IadePayi(IReadOnlyList<KanalPayYaz> kaynak, decimal odenen, decimal iade, out decimal tasan)
    {
        var paid = KirparakOranla(kaynak, odenen).Paylar.ToDictionary(p => p.KanalId, p => p.Tutar);
        var unpaid = kaynak.Select(p => new KanalPayYaz(p.KanalId, p.Tutar - paid.GetValueOrDefault(p.KanalId))).Where(p => p.Tutar > 0).ToList();
        var paylar = KirparakOranla(unpaid, iade).Paylar;
        tasan = kaynak.Any(p => p.Tutar > 0) ? iade - paylar.Sum(p => p.Tutar) : 0;
        return paylar;
    }
    /// <summary>Kaynak harcamanın (eski borç devri) kasada önceden sayılan tutarı; iadelerinin kasaya geri döndürdüğü kısım
    /// düşülür (finance-2). Düzeltme yoksa değer aynen döner: ondalık ölçeği de değişmez, raporlar birebir aynı kalır.</summary>
    internal static decimal EtkinKasadaSayilan(TakipHarcamaEntity charge, IEnumerable<TakipHarcamaEntity> iadeler, IReadOnlyDictionary<int, TakipIadeHesabiEntity> hesaplar)
    {
        if (charge.KasadaOncedenSayilanTutar <= 0)
            return charge.KasadaOncedenSayilanTutar;
        var duzeltme = iadeler.Sum(r => hesaplar.TryGetValue(r.Id, out var h) ? h.KasadaSayilanDuzeltme : 0);
        return duzeltme > 0 ? charge.KasadaOncedenSayilanTutar - duzeltme : charge.KasadaOncedenSayilanTutar;
    }
    /// <summary>İlk kesim tarihinin kartın düzenli kesiminden en çok uzaklığı (gün): banka kesimi tatil nedeniyle birkaç gün
    /// kayabilir; daha uzak tarih (ör. formdaki varsayılan "bugün") kartın döngüsünde değildir.</summary>
    internal const int IlkKesimToleransi = 7;
    /// <summary><paramref name="date"/>'e en yakın düzenli kesim: kartın <paramref name="day"/> gününe (kısa ayda ay sonuna)
    /// düşen, <paramref name="date"/>'ten önceki ya da o gün/sonraki ilk kesim. Eşit uzaklıkta önceki seçilir.</summary>
    internal static DateOnly EnYakinDuzenliKesim(DateOnly date, int day)
    {
        var next = Kesim(date, day);
        var previous = Gun(next.AddMonths(-1), day);
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
            if (IlkKesimHatasi(db, card, ilk) is { } hata)
                FinansTakipEndpoints.Require(false, hata);
        }
        db.TakipHarcamalar.Add(charge);
        db.SaveChanges();
        var cents = decimal.ToInt64(Math.Abs(charge.Tutar) * 100);
        var sign = Math.Sign(charge.Tutar);
        for (var i = 0; i < charge.TaksitSayisi; i++)
        {
            var statement = Ekstre(db, card, Gun(cut.AddMonths(i), day));
            db.TakipKartTaksitler.Add(new()
            {
                HarcamaId = charge.Id,
                EkstreId = statement.Id,
                Tutar = sign * (cents / charge.TaksitSayisi + (i < cents % charge.TaksitSayisi ? 1 : 0)) / 100m
            });
        }
        db.SaveChanges();
    }
    /// <summary>İlk kesim tarihinin (<see cref="HarcamaEkle"/> firstCut) kart döngüsü denetimi: düzenli kesime en çok
    /// <see cref="IlkKesimToleransi"/> gün uzak ve yuvarlandığı kesim kartın takip başlangıcından önce değil. Uygunsa null.
    /// Kart ekranı harcaması, kartlı alış ödemesi ve kartlı genel gider aynı iletiyi verir.</summary>
    internal static string? IlkKesimHatasi(KasaDbContext db, KrediKartiEntity card, DateOnly ilk)
    {
        var day = card.KesimTarihi.Day;
        var cut = EnYakinDuzenliKesim(ilk, day);
        if (Math.Abs(ilk.DayNumber - cut.DayNumber) > IlkKesimToleransi)
            return $"İlk kesim tarihi kartın hesap kesim gününe ({day}) en fazla {IlkKesimToleransi} gün uzak olabilir; bankanın kaydırdığı kesimi ya da harcamanın düştüğü sonraki kesimi girin.";
        var baslangic = db.TakipKartlar.Where(t => t.KrediKartiId == card.Id).Select(t => t.Baslangic).Single();
        return cut >= baslangic ? null
            : $"İlk kesim tarihi kartın takip başlangıcından ({KartGecisHesabi.Tarih(baslangic)}) önceki {KartGecisHesabi.Tarih(cut)} kesimine denk geliyor; takipten önceki ekstreler izlenmez. Harcamanın düştüğü sonraki kesimi girin ya da ilk kesimi boş bırakın.";
    }
    /// <summary>Takipli karta bağlı giderin (genel gider ya da alış ödemesi) dondurulmuş kanal payı: tek kanallı giderde o kanal,
    /// Ortak giderde o anki aktif kanallara eşit pay, diğerlerinde (alış ödemesi "Dağılım bekliyor", çok kanallı ayrılmış gider)
    /// boş. Alışa bağlı giderin payı ayrıca alıştan okunur (<see cref="KaynakPaylari(KasaDbContext, TakipHarcamaEntity)"/>).</summary>
    internal static List<KanalPayYaz> DonmusPaylar(KasaDbContext db, IslemEntity expense)
    {
        if (expense.KanalId is { } channel)
            return [new(channel, Math.Abs(expense.TutarTl))];
        if (expense.Kanal != KanalEtiketleri.Ortak)
            return [];
        var channelIds = db.Kanallar.Where(k => k.Aktif).Select(k => k.Id).ToList();
        return channelIds.Count > 0 ? EsitPaylar(channelIds, Math.Abs(expense.TutarTl)) : [];
    }
    /// <summary>
    /// Takipli karta bağlı giderin kart harcaması (kaynak bağı <c>IslemId</c>): Sync'in taksitsiz aynalaması ve kartlı alış ödemesi
    /// ile kartlı genel giderin taksitli kaydı aynı kuralı kullanır (gap-coklu-giris-cift-sayim-mutabakat-6). Varsayılan tek taksit
    /// ve ilk kesimsiz harcama önceki Sync kaydıyla birebir aynıdır; mevcut kayıtlar değişmez. Kart sürümü artar.
    /// </summary>
    internal static TakipHarcamaEntity KaynakHarcamaEkle(KasaDbContext db, IslemEntity expense, int taksitSayisi = 1, DateOnly? ilkKesim = null)
    {
        var tracking = db.TakipKartlar.Single(t => t.KrediKartiId == expense.KrediKartiId);
        var card = db.KrediKartlari.Single(k => k.Id == tracking.KrediKartiId);
        var charge = new TakipHarcamaEntity
        {
            KrediKartiId = card.Id,
            IslemId = expense.Id,
            Tarih = expense.Tarih,
            Aciklama = expense.Cari,
            Tutar = expense.TutarTl,
            TaksitSayisi = taksitSayisi,
            DagilimJson = Json(DonmusPaylar(db, expense))
        };
        HarcamaEkle(db, card, charge, ilkKesim);
        tracking.Surum++;
        return charge;
    }
    /// <summary>Giderin kart takibindeki harcaması (kaynak bağı); yoksa null. Takipli karta bağlı ama takip başlangıcından önce
    /// tarihli (eski kuralda kalan) giderin harcaması yoktur.</summary>
    internal static TakipHarcamaEntity? KaynakHarcama(KasaDbContext db, int islemId) => db.TakipHarcamalar.SingleOrDefault(h => h.IslemId == islemId);
    /// <summary>
    /// Giderin kart harcaması kaldırılabilir ya da kanalı değiştirilebilir mi (gap-coklu-giris-cift-sayim-mutabakat-5): taksitlerine pay
    /// ayırmış iptal edilmemiş kart ödemesi, iptal edilmemiş iadesi ya da harcamayı ya da taksidini gösteren ekstre satırı varsa hayır.
    /// Ödenmiş harcamanın kaldırılması önceki ödemelerin kanal payını ve kasayı değiştirirdi; iade kaynağını, ekstre satırı kaydını
    /// kaybederdi. Engel yoksa null, varsa nedeni (<see cref="HarcamaEngeli"/>; ilk bulunan, bu sırayla denetlenir).
    /// </summary>
    internal static HarcamaEngeli? KaynakHarcamaEngeli(KasaDbContext db, TakipHarcamaEntity charge)
    {
        var taksitler = db.TakipKartTaksitler.Where(t => t.HarcamaId == charge.Id).Select(t => t.Id).ToHashSet();
        if (db.TakipKartOdemeler.Where(p => p.KrediKartiId == charge.KrediKartiId && !p.Iptal).AsEnumerable()
            .Any(p => Read<KartTaksitPayi>(p.PaylarJson).Any(x => taksitler.Contains(x.TaksitId))))
            return HarcamaEngeli.Odendi;
        if (db.TakipHarcamalar.Any(h => h.KaynakHarcamaId == charge.Id && !h.Iptal))
            return HarcamaEngeli.IadesiVar;
        var ids = taksitler.ToArray();
        return db.EkstreKayitlar.Any(k => !k.Iptal && (k.KartHarcamaId == charge.Id || k.EslesmeTuru == EslesmeTurleri.KartHarcama && k.EslesmeId == charge.Id
            || k.EslesmeTuru == EslesmeTurleri.KartTaksidi && k.EslesmeId != null && ids.Contains(k.EslesmeId.Value))) ? HarcamaEngeli.Ekstre : null;
    }
    /// <summary>Kart harcamasının kaldırılmasını ya da kanalının değiştirilmesini engelleyen neden (<see cref="KaynakHarcamaEngeli"/>).
    /// İletiler çağıranda, işleme göre yazılır (gider düzenleme/silme, alış ödemesini ayırma).</summary>
    internal enum HarcamaEngeli
    {
        /// <summary>Taksitlerine pay ayırmış iptal edilmemiş kart ödemesi var.</summary>
        Odendi,
        /// <summary>İptal edilmemiş iadesi var.</summary>
        IadesiVar,
        /// <summary>Harcamayı ya da taksidini gösteren iptal edilmemiş ekstre satırı var (eşleştirme dahil).</summary>
        Ekstre,
    }
    /// <summary>
    /// Eski kuralla yazılmış, otomatik dönüştürülmeyen takip kayıtlarının uyarıları (bütünlük denetimi; açılışta loglanır,
    /// bkz. <see cref="KartGecisHesabi.IlkSurumKalintilari"/>). Kayıtlar raporlara bugünkü halleriyle girer; dönüştürme geçmiş
    /// raporları değiştirirdi ve doğru hali ancak banka ekstresiyle belirlenebilir. Salt okunur; iki desen aranır:
    /// kartın kesim günü (kısa ayda ay sonu) dışında kesilmiş ekstre — taksitleri ilk kesim gününe sabitleyen eski kuralın
    /// kartın döngüsüne paralel ekstresi olabilir (finance-3; aynı ay ikinci kesim bildirimi), kesim günü sonradan
    /// değiştirildiyse eski günün ekstresidir — ve genel gider ekranından takipli karta girilmiş eksi gider (gidere bağlı,
    /// kaynak harcamasız eksi harcama): kaynaksız alacak olarak herhangi bir kanalın taksidine mahsup edilir (finance-9).
    /// </summary>
    public static List<string> EskiKuralKalintilari(KasaDbContext db)
    {
        var kartlar = db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id);
        string Kart(int id) => $"Kart {id} ({(kartlar.TryGetValue(id, out var k) ? k.Ad : "silinmiş kart")})";
        var uyarilar = new List<string>();
        foreach (var ekstreler in db.TakipEkstreler.AsNoTracking().ToList().GroupBy(s => s.KrediKartiId).OrderBy(g => g.Key))
        {
            if (!kartlar.TryGetValue(ekstreler.Key, out var kart))
                continue;
            var day = kart.KesimTarihi.Day;
            var disarida = ekstreler.Select(s => s.KesimTarihi).Where(t => t != Gun(t, day)).Order().ToList();
            if (disarida.Count == 0)
                continue;
            var ciftAylar = ekstreler.GroupBy(s => (s.KesimTarihi.Year, s.KesimTarihi.Month)).Where(a => a.Count() > 1).Select(a => a.Key).ToHashSet();
            uyarilar.Add($"{Kart(kart.Id)}: kartın kesim günü ({day}) dışında kesilmiş {disarida.Count} ekstre var "
                + $"({KartGecisHesabi.Tarih(disarida[0])}–{KartGecisHesabi.Tarih(disarida[^1])}; {disarida.Count(t => ciftAylar.Contains((t.Year, t.Month)))} tanesi aynı ay kartın başka bir ekstresiyle birlikte). "
                + "Taksitleri ilk kesim gününe sabitleyen eski kuralın paralel ekstresi olabilir (aynı ay ikinci kesim bildirimi); kesim günü sonradan değiştirildiyse eski günün ekstreleridir. "
                + "Kayıtlar otomatik dönüştürülmez, raporlar değişmez; banka ekstreleriyle karşılaştırın.");
        }
        foreach (var eksiler in db.TakipHarcamalar.AsNoTracking().Where(h => h.IslemId != null && h.KaynakHarcamaId == null && !h.Iptal && h.Tutar < 0).ToList()
            .GroupBy(h => h.KrediKartiId).OrderBy(g => g.Key))
            uyarilar.Add($"{Kart(eksiler.Key)}: genel gider ekranından girilmiş {eksiler.Count()} eksi kart gideri (toplam {KartGecisHesabi.Tl(-eksiler.Sum(h => h.Tutar))}; "
                + $"{KartGecisHesabi.Tarih(eksiler.Min(h => h.Tarih))}–{KartGecisHesabi.Tarih(eksiler.Max(h => h.Tarih))}) kaynak harcamasız alacak olarak herhangi bir kanalın taksidine mahsup ediliyor; "
                + "iade akışının kaynak ve kanal korumaları uygulanmadı. Kayıtlar otomatik dönüştürülmez, raporlar değişmez; iadenin kaynak harcamasını banka ekstresiyle doğrulayın.");
        uyarilar.AddRange(DuzeltilmemisDevirIadeleri(db, Kart));
        return uyarilar;
    }
    /// <summary>
    /// Eski borç devrine (kasada önceden sayılan tutarı olan harcama) bu sürümden önce girilmiş iadeler (finance-2, K1):
    /// iade edilen kısım eski kuralla kasadan düşülmüştü ama kasaya geri dönmüyor. Hesap kaydı olmayan iade ya da kaydındaki
    /// ödenmiş tutara göre düzeltme gerektirdiği halde düzeltmesi 0 olan iade sayılır. Kayıtlar otomatik dönüştürülmez
    /// (düzeltme geçmiş ve kilitli ayları değiştirirdi), raporlar değişmez; yalnız uyarı.
    /// </summary>
    private static IEnumerable<string> DuzeltilmemisDevirIadeleri(KasaDbContext db, Func<int, string> kart)
    {
        var devirler = db.TakipHarcamalar.AsNoTracking().Where(h => h.KaynakHarcamaId == null && !h.Iptal).ToList().Where(h => h.KasadaOncedenSayilanTutar > 0).ToDictionary(h => h.Id);
        if (devirler.Count == 0)
            yield break;
        var ids = devirler.Keys.ToArray();
        var iadeler = db.TakipHarcamalar.AsNoTracking().Where(h => h.KaynakHarcamaId != null && ids.Contains(h.KaynakHarcamaId.Value) && !h.Iptal).OrderBy(h => h.Id).ToList();
        var hesaplar = IadeHesaplariOku(db.TakipIadeHesaplari.AsNoTracking(), iadeler);
        var eksik = new List<TakipHarcamaEntity>();
        foreach (var grup in iadeler.GroupBy(h => h.KaynakHarcamaId!.Value))
        {
            decimal duzeltilen = 0;
            foreach (var iade in grup)
            {
                if (!hesaplar.TryGetValue(iade.Id, out var hesap))
                { eksik.Add(iade); continue; }
                var gereken = Math.Min(-iade.Tutar, Math.Max(0, devirler[grup.Key].KasadaOncedenSayilanTutar - duzeltilen - hesap.IadeAnindaOdenen));
                if (gereken > 0 && hesap.KasadaSayilanDuzeltme == 0)
                    eksik.Add(iade);
                duzeltilen += hesap.KasadaSayilanDuzeltme;
            }
        }
        foreach (var grup in eksik.GroupBy(h => h.KrediKartiId).OrderBy(g => g.Key))
            yield return $"{kart(grup.Key)}: eski borç devrine girilmiş {grup.Count()} iade (toplam {KartGecisHesabi.Tl(-grup.Sum(h => h.Tutar))}; "
                + $"{KartGecisHesabi.Tarih(grup.Min(h => h.Tarih))}–{KartGecisHesabi.Tarih(grup.Max(h => h.Tarih))}) kasada önceden sayılan tutarı düzeltmeden kaydedildi: "
                + "iade edilen kısım eski kuralla kasadan düşülmüştü ama kasaya geri dönmüyor. Kayıtlar otomatik dönüştürülmez, raporlar değişmez; "
                + "devre ödeme yapılmadıysa iadeyi gerekçeyle iptal edip yeniden girin (yeni iade kasaya geri döner).";
    }
    /// <summary>
    /// Veri adımı (göç <c>20261001000200_KartTakipDuzeltmeleri</c> uygulandığı açılışta bir kez, göç öncesi yedekten sonra):
    /// bu sürümden önceki iadelere hesap kaydı yazar (gap-coklu-giris-cift-sayim-mutabakat-3). İade anındaki ödenmiş tutar
    /// saklanmadığından adaylar sırayla denenir: iade tarihine kadar tarihli (iptal edilmemiş) ödemelerin kaynak taksitlerine
    /// payı, 0, ödeme Id sırasıyla kümülatif toplamlar (iptal edilmişler dahil, sonra hariç). Aynı kuralla türetilen pay
    /// dondurulmuş payla kanal, sıra ve tutar metni dahil birebir aynıysa aday yazılır: bugünkü bütün raporlar değişmez,
    /// kaynak sonradan yeniden dağıtılınca iade yeni oranı izler. Hiçbir aday tutmayan iade eski kuralda (dondurulmuş pay)
    /// kalır. Kasada önceden sayılan tutar düzeltmesi yazılmaz (0): eski iadelerin kasa etkisi değişmez.
    /// </summary>
    public static (int Eslesen, int Eslesmeyen) IadeHesabiTohumu(KasaDbContext db)
    {
        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        var mevcut = db.TakipIadeHesaplari.AsNoTracking().Select(x => x.HarcamaId).ToHashSet();
        var kartlar = db.TakipHarcamalar.AsNoTracking().Where(h => h.KaynakHarcamaId != null && !h.Iptal).Select(h => new { h.Id, h.KrediKartiId }).ToList()
            .Where(h => !mevcut.Contains(h.Id)).Select(h => h.KrediKartiId).Distinct().Order().ToList();
        int eslesen = 0, eslesmeyen = 0;
        foreach (var kartId in kartlar)
        {
            var b = new TakipHesapBaglami(db);
            var v = b.KartVerisi(kartId);
            var hesaplar = new Dictionary<int, TakipIadeHesabiEntity>(v.IadeHesaplari(b));
            foreach (var kaynak in v.Harcamalar.Where(h => h.KaynakHarcamaId is null).OrderBy(h => h.Id))
            {
                var iadeler = v.Iadeler(b)[kaynak.Id].OrderBy(h => h.Id).ToList();
                if (iadeler.All(h => hesaplar.ContainsKey(h.Id)))
                    continue;
                var taksitler = v.Taksitler.Where(t => t.HarcamaId == kaynak.Id).Select(t => t.Id).ToHashSet();
                decimal Odenen(TakipKartOdemeEntity p) => v.OdemePaylari[p.Id].Where(x => taksitler.Contains(x.TaksitId)).Sum(x => x.Tutar);
                var kaynakPay = KaynakPaylari(b, kaynak);
                foreach (var iade in iadeler.Where(h => !hesaplar.ContainsKey(h.Id)))
                {
                    var onceki = IadeHesabi(kaynakPay, iadeler.Where(h => h.Id < iade.Id), hesaplar).Kalan;
                    var dondurulmus = PayMetni(Read<KanalPayYaz>(iade.DagilimJson));
                    var adaylar = new List<decimal> { v.Odemeler.Where(p => !p.Iptal && p.Tarih <= iade.Tarih).Sum(Odenen), 0 };
                    foreach (var iptalDahil in new[] { true, false })
                    {
                        decimal toplam = 0;
                        foreach (var p in v.Odemeler.Where(p => iptalDahil || !p.Iptal))
                            adaylar.Add(toplam += Odenen(p));
                    }
                    decimal? bulunan = null;
                    foreach (var aday in adaylar.Distinct())
                        if (IadePayi(onceki, aday, -iade.Tutar, out var tasan) is var pay && tasan == 0 && PayMetni(pay).SequenceEqual(dondurulmus))
                        { bulunan = aday; break; }
                    if (bulunan is not { } odenen)
                    { eslesmeyen++; continue; }
                    var hesap = new TakipIadeHesabiEntity { HarcamaId = iade.Id, IadeAnindaOdenen = odenen, KasadaSayilanDuzeltme = 0 };
                    db.TakipIadeHesaplari.Add(hesap);
                    hesaplar[iade.Id] = hesap;
                    eslesen++;
                }
            }
        }
        db.SaveChanges();
        transaction?.Commit();
        return (eslesen, eslesmeyen);
    }
    private static List<string> PayMetni(IEnumerable<KanalPayYaz> paylar) =>
        paylar.Select(p => p.KanalId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + p.Tutar.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
    /// <summary>Eski kartı yeni takibe alır; doğrulama (KartGecisHesabi ile önizleme) çağırandadır. Yeni
    /// geçişler işlem tarihi kuralıyla yazılır: başlangıçtan önceki eski giderler eski ay sonu kuralıyla
    /// bir kez düşer, devir borcunun kasada önceden sayılan kısmı ödemede ikinci kez düşmez. Mali sonucu
    /// belirleyen karar denetim izi olarak saklanır: açıklama ve onay anındaki önizleme özeti.</summary>
    public static void KartGecisiYaz(KasaDbContext db, int kartId, DateOnly baslangic, decimal kalanBorc, decimal kasadaOncedenSayilan, IReadOnlyList<KanalPayYaz> dagilimlar, string aciklama)
    {
        var s = KartGecisHesabi.Hesapla(db, kartId, baslangic, kalanBorc);
        var kayit = new KartGecisKaydi(db.Bugunu(), kalanBorc, kasadaOncedenSayilan, s.SistemKartBorcu, s.EskiKuraldaIslenenTutar, s.BekleyenEskiDusumTutari, s.SonBekleyenDusumTarihi, s.OnerilenKasadaSayilanTutar);
        db.TakipKartlar.Add(new()
        {
            KrediKartiId = kartId,
            Baslangic = baslangic,
            EskiKayit = true,
            EskiDusumKurali = EskiDusumKurali.IslemTarihi,
            GecisAciklamasi = aciklama.Trim(),
            GecisOzetiJson = JsonSerializer.Serialize(kayit)
        });
        db.SaveChanges();
        if (kalanBorc != 0)
            HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == kartId), new()
            {
                KrediKartiId = kartId,
                Tarih = baslangic,
                Aciklama = KartGecisHesabi.DevirAciklamasi,
                Tutar = kalanBorc,
                KasadaOncedenSayilanTutar = kasadaOncedenSayilan,
                DagilimJson = Json(dagilimlar)
            });
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
            // Kaydını yazma yolu yapmamış (taksitsiz) gider tek taksitle aynalanır; taksitli kartlı gider ve alış ödemesi harcamasını
            // kaydederken aynı kuralla yazar (KaynakHarcamaEkle) ve burada bilinen sayılır.
            foreach (var expense in db.Islemler.Where(i => i.KrediKartiId == card.Id && i.Tarih >= tracking.Baslangic).ToList().Where(i => !known.Contains(i.Id)))
                KaynakHarcamaEkle(db, expense);
            if (tracking.Aktif)
            {
                // Sıfır borçlu aktif kart için de aylık kesim olayı vardır.
                var cut = Kesim(today, card.KesimTarihi.Day);
                if (cut >= tracking.Baslangic)
                    Ekstre(db, card, cut);
                var last = Gun(cut.AddMonths(-1), card.KesimTarihi.Day);
                if (last >= tracking.Baslangic)
                    Ekstre(db, card, last);
            }
            // Borçtan önce yatırılmış avans yeni kaynak harcamaya bağlanır. Ödeme
            // tarihi/tutarı değişmez; dağılım bekleyen payın kanalı belli olur.
            AvanslariDagit(db, card, today);
        }
        db.SaveChanges();
    }
    /// <summary>
    /// Avansın (harcamaya bağlanamamış ödeme payı, TaksitId 0) yeni kaynak harcamaya bağlanması. Açık dönemdeki ödemenin
    /// payları yeniden yazılır (önceki davranış). Kilitli döneme düşen ödemenin payları kilitli ayın raporunu değiştireceği
    /// için yazılmaz (finance-8): avansın bağlanan kısmı bugün tarihli, tutarı 0 olan ayrı bir dağıtım ödemesiyle
    /// (<see cref="TakipAvansTahsisEntity"/>) kaydedilir. Dağıtım taksit paylarını ve aynı tutarda eksi avans payını taşır:
    /// kart borcu ve kasa değişmez, o tarihte "Dağılım bekliyor" payı harcamanın kanalına geçer. Kart kilit yüzünden yeni
    /// harcamaya kapanmaz. Bir kez dağıtım almış ödeme, kilit sonradan açılsa da dağıtım kaydıyla devam eder.
    /// </summary>
    private static void AvanslariDagit(KasaDbContext db, KrediKartiEntity card, DateOnly today)
    {
        var odemeler = db.TakipKartOdemeler.Where(p => p.KrediKartiId == card.Id && !p.Iptal).OrderBy(p => p.Id).ToList();
        var avansli = odemeler.Where(p => Read<KartTaksitPayi>(p.PaylarJson).Any(x => x.TaksitId == 0 && x.Tutar > 0)).ToList();
        if (avansli.Count == 0)
            return;
        var ids = avansli.Select(p => p.Id).ToArray();
        var aktif = odemeler.ToDictionary(p => p.Id);
        var tahsisler = db.TakipAvansTahsisleri.Where(t => ids.Contains(t.KaynakOdemeId)).ToList()
            .Where(t => aktif.ContainsKey(t.OdemeId)).ToLookup(t => t.KaynakOdemeId, t => aktif[t.OdemeId]);
        DateOnly? kilit = null;
        var kilitOkundu = false;
        foreach (var payment in avansli)
        {
            var pays = Read<KartTaksitPayi>(payment.PaylarJson);
            var dagitimlar = tahsisler[payment.Id].ToList();
            var advance = pays.Where(p => p.TaksitId == 0).Sum(p => p.Tutar)
                + dagitimlar.SelectMany(t => Read<KartTaksitPayi>(t.PaylarJson)).Where(p => p.TaksitId == 0).Sum(p => p.Tutar);
            if (advance <= 0)
                continue;
            var allocated = OdemePaylari(db, card.Id, advance, null, ignoreAdvances: true);
            var taksitPaylari = allocated.Where(p => p.TaksitId != 0).ToList();
            if (taksitPaylari.Count == 0)
                continue;
            if (!kilitOkundu)
            { kilit = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single(); kilitOkundu = true; }
            // Ödeme payının yeniden yazılması sonraki (Id sırası) ödemelerin etkisini de değiştirebilir: kilitli döneme düşen
            // sonraki ödeme varsa da dağıtım kaydı kullanılır (kilit kuralı yeniden yazımı reddederdi).
            if (dagitimlar.Count == 0 && !(kilit is { } son && (payment.Tarih <= son || odemeler.Any(p => p.Id > payment.Id && p.Tarih <= son))))
            {
                payment.PaylarJson = Json(pays.Where(p => p.TaksitId != 0).Concat(allocated));
                continue;
            }
            // Dağıtım kilit sonrası tarihlidir; saat kilit sınırının gerisindeyse (yalnız tamamlanmış ay kilitlenir) beklenir.
            if (kilit is { } sinir && today <= sinir)
                continue;
            var dagitilan = taksitPaylari.Sum(p => p.Tutar);
            var tahsis = new TakipKartOdemeEntity
            {
                KrediKartiId = card.Id,
                Tarih = today,
                Tutar = 0,
                Not = $"Kilitli avans dağıtımı: {KartGecisHesabi.Tarih(payment.Tarih)} tarihli ödemenin avansı",
                PaylarJson = Json(taksitPaylari.Append(new KartTaksitPayi(0, -dagitilan)))
            };
            db.TakipKartOdemeler.Add(tahsis);
            db.SaveChanges();
            db.TakipAvansTahsisleri.Add(new() { OdemeId = tahsis.Id, KaynakOdemeId = payment.Id });
            db.SaveChanges();
        }
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
                if (result.ContainsKey(pay.TaksitId))
                    result[pay.TaksitId] -= pay.Tutar;
                else if (pay.TaksitId == 0 && !ignoreAdvances)
                    credit += pay.Tutar;
        foreach (var refund in charges.Where(h => h.Tutar < 0))
        {
            var amount = -refund.Tutar;
            if (refund.KaynakHarcamaId is { } sourceId)
            {
                foreach (var sourceTax in taxes.Where(t => t.HarcamaId == sourceId).OrderBy(t => t.Id))
                {
                    var applied = Math.Min(amount, Math.Max(0, result.GetValueOrDefault(sourceTax.Id)));
                    result[sourceTax.Id] -= applied;
                    amount -= applied;
                }
                // Ödenmiş kaynak için kalan iade alacak bakiyesidir; başka kanalın
                // harcamasına kendiliğinden atanmaz. Banka ödeme toplamı değişmez.
            }
            else
                credit += amount; // Açılış alacak bakiyesi.
        }
        foreach (var id in result.Keys.ToArray())
        {
            if (result[id] < 0)
            { credit -= result[id]; result[id] = 0; }
            var applied = Math.Min(credit, result[id]);
            result[id] -= applied;
            credit -= applied;
        }
        return result;
    }
    internal static List<KartTaksitPayi> OdemePaylari(KasaDbContext db, int cardId, decimal amount, int? statementId, bool ignoreAdvances = false)
    {
        var remaining = KalanTaksitler(db, cardId, ignoreAdvances);
        var statements = db.TakipEkstreler.Where(e => e.KrediKartiId == cardId).OrderBy(e => e.SonOdemeTarihi).ThenBy(e => e.Id).ToList();
        if (statementId is { } selected)
            statements = statements.OrderBy(e => e.Id == selected ? 0 : 1).ToList();
        var result = new List<KartTaksitPayi>();
        var left = amount;
        foreach (var statement in statements)
        {
            var taxes = db.TakipKartTaksitler.Where(t => t.EkstreId == statement.Id).ToList();
            var weights = taxes.Where(t => remaining.GetValueOrDefault(t.Id) > 0).Select(t => new KanalPayYaz(t.Id, remaining[t.Id])).ToList();
            var allocated = Math.Min(left, weights.Sum(t => t.Tutar));
            if (allocated <= 0)
                continue;
            result.AddRange(Oranla(weights, allocated).Select(p => new KartTaksitPayi(p.KanalId, p.Tutar)));
            left -= allocated;
        }
        if (left > 0)
            result.Add(new(0, left));
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
        if (gorulen.Count >= KirpmaUyarisiSiniri)
            gorulen.Clear();
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
        var v = b.KartVerisi(cardId);
        var taxes = v.TaksitById;
        var charges = v.HarcamaById;
        var db = b.Db;
        var shares = new List<TakipKanalPayi>();
        var statementShares = new List<KartEkstreOdemePayi>();
        decimal cash = 0;
        foreach (var group in pays.GroupBy(p => taxes.TryGetValue(p.TaksitId, out var t) ? t.HarcamaId : 0))
        {
            var amount = group.Sum(p => p.Tutar);
            if (group.Key == 0)
            { cash += amount; shares.Add(new(null, KanalEtiketleri.DagilimBekliyor, amount)); continue; }
            var charge = charges[group.Key];
            var previous = previousByCharge.GetValueOrDefault(charge.Id);
            // Devrin kasada önceden sayılan tutarından iadelerinin kasaya geri döndürdüğü kısım düşülür (finance-2).
            var counted = EtkinKasadaSayilan(charge, v.Iadeler(b)[charge.Id], v.IadeHesaplari(b));
            var credit = Math.Min(amount, Math.Max(0, counted - previous));
            var effect = amount - credit;
            cash += effect;
            var source = IadeSonrasiPaylar(b, charge);
            if (source.Count == 0)
                shares.Add(new(null, KanalEtiketleri.DagilimBekliyor, effect));
            else
            {
                var (paylar, tasan) = KirparakOranla(source, effect, Math.Max(0, previous - counted));
                shares.AddRange(Adlandir(b.KanalAdlari, paylar));
                if (tasan > 0)
                {
                    // Bozuk/eski ödeme payı bütün raporları 500'e düşürmesin: sığmayan kısım
                    // görünür "Dağılım bekliyor" payı olur ve incelenmek üzere loglanır.
                    shares.Add(new(null, KanalEtiketleri.DagilimBekliyor, tasan));
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
        if (b.OdemeEtkileriOnbellegi.TryGetValue(cardId, out var memo))
            return memo;
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
    /// DTO'su (ekstre, harcama, kalan borç payları) hesaplanmaz. <c>Anahtar</c>: kasa hareket dökümündeki kaydı (ödeme
    /// "TakipKartOdeme:{Id}", önceden sayılan iade "TakipHarcama:{Id}"); hesaba girmez.</summary>
    internal static IEnumerable<(DateOnly Tarih, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Anahtar)> KartOdemeDagilimlari(TakipHesapBaglami b, int cardId)
    {
        var v = b.KartVerisi(cardId);
        var effects = OdemeEtkileri(b, cardId);
        var sonuc = v.Odemeler.Where(p => !p.Iptal).Select(p => (p.Tarih, p.Not, effects[p.Id].Dagilimlar, "TakipKartOdeme:" + p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        // Ödemelerden sonra eklenir: mevcut ödemelerin rapordaki kaynak anahtarları (sıra) değişmez.
        sonuc.AddRange(OncedenSayilanIadeleri(b, cardId));
        return sonuc;
    }
    /// <summary>Eski borç devrine yapılan iadenin kasada önceden sayılan kısmı (finance-2): eski kuralla kasadan düşülmüş borç
    /// iade edildiğinden iade tarihinde kasaya geri döner (eksi kart ödemesi), iadenin kanal payıyla. Payı olmayan iadede
    /// tutar "Dağılım bekliyor"a döner. İade iptal edilirse satır da kalkar.</summary>
    internal static List<(DateOnly Tarih, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Anahtar)> OncedenSayilanIadeleri(TakipHesapBaglami b, int cardId)
    {
        var v = b.KartVerisi(cardId);
        var sonuc = new List<(DateOnly, string?, IReadOnlyList<TakipKanalPayi>, string)>();
        if (!v.Harcamalar.Any(h => h.KaynakHarcamaId != null && !h.Iptal))
            return sonuc;
        var hesaplar = v.IadeHesaplari(b);
        foreach (var iade in v.Harcamalar.Where(h => h.KaynakHarcamaId != null && !h.Iptal).OrderBy(h => h.Id))
        {
            if (!hesaplar.TryGetValue(iade.Id, out var hesap) || hesap.KasadaSayilanDuzeltme <= 0)
                continue;
            var (paylar, tasan) = KirparakOranla(IadeSatiriPayi(b, iade), hesap.KasadaSayilanDuzeltme);
            var satirlar = Adlandir(b.KanalAdlari, paylar).Select(p => p with { Tutar = -p.Tutar }).ToList();
            var bekleyen = paylar.Count == 0 ? hesap.KasadaSayilanDuzeltme : tasan;
            if (bekleyen > 0)
                satirlar.Add(new(null, KanalEtiketleri.DagilimBekliyor, -bekleyen));
            sonuc.Add((iade.Tarih, "Önceden sayılan kart borcu iadesi: " + iade.Aciklama, satirlar, "TakipHarcama:" + iade.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return sonuc;
    }
    /// <summary>İade satırının kanal payı: kaynağının tek iade hesabından (<see cref="IadeHesabi"/>). İptal edilmiş iade ve
    /// kaynağı bulunamayan satır kayıtlı (dondurulmuş) payıyla gösterilir.</summary>
    private static List<KanalPayYaz> IadeSatiriPayi(TakipHesapBaglami b, TakipHarcamaEntity iade) =>
        iade is { KaynakHarcamaId: { } kaynakId, Iptal: false } && b.KartVerisi(iade.KrediKartiId).HarcamaById.TryGetValue(kaynakId, out var kaynak)
            && IadeDagilimiOku(b, kaynak).Paylar.TryGetValue(iade.Id, out var pay) ? pay : KaynakPaylari(b, iade);
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
                debt > 0 ? [new(null, KanalEtiketleri.DagilimBekliyor, debt)] : []);
        }
        var today = b.Bugun;
        var v = b.KartVerisi(id);
        var charges = v.Harcamalar;
        var taxes = v.Taksitler;
        var payments = v.Odemeler;
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
            charges.Select(h =>
            {
                var source = h.KaynakHarcamaId is null ? KaynakPaylari(b, h) : IadeSatiriPayi(b, h);
                var duzeltme = h.KaynakHarcamaId is not null && v.IadeHesaplari(b).TryGetValue(h.Id, out var hesap) ? hesap.KasadaSayilanDuzeltme : 0;
                return new KartHarcamaDto(h.Id, h.IslemId, h.Tarih, h.Aciklama, h.Tutar, h.TaksitSayisi, h.Iptal,
                    source.Count > 0 ? Adlandir(b.KanalAdlari, source) : [new(null, KanalEtiketleri.DagilimBekliyor, Math.Abs(h.Tutar))], importedCharges[h.Id].SingleOrDefault()?.Id, duzeltme);
            }).ToList(),
            // İptal edilmiş ödemenin kasa/kanal etkisi yoktur. Payları sonradan girilen iadeyle
            // kaynak ağırlığını aşabileceğinden etkisi hiç hesaplanmaz.
            payments.Select(p =>
            {
                var importId = importedPayments[p.Id].SingleOrDefault()?.Id;
                int? avans = v.AvansKaynaklari(b).TryGetValue(p.Id, out var kaynak) ? kaynak : null;
                if (p.Iptal)
                    return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, 0, p.Not, true, [], importId, avans);
                var effect = effects[p.Id];
                return new KartTakipOdemeDto(p.Id, p.Tarih, p.Tutar, effect.KasaEtkisi, p.Not, false, effect.Dagilimlar, importId, avans);
            }).ToList(),
            KalanKartBorcPaylari(b, charges, taxes, remaining), KartGecisHesabi.Gecis(db, track));
    }
    /// <summary>Tarihe bağlı ekstreler okumada yazılmadan türetilir. Aktif kartın bugünkü ve önceki kesim ekstresini
    /// Sync (bakım adımı) yazar; henüz yazılmamışsa (gün dönümünden sonra bakım çalışmadan) aynı tarihli boş ekstre
    /// kimliksiz (Id 0) eklenir. Böyle bir ekstrede taksit olamaz: taksit yazan her yol ekstresini de yazar.</summary>
    private static List<KartEkstreDto> TarihTuretilmisEkstreler(List<KartEkstreDto> statements, KrediKartiEntity card, TakipKartEntity track, DateOnly today)
    {
        if (!track.Aktif)
            return statements;
        var cut = Kesim(today, card.KesimTarihi.Day);
        var missing = new[] { cut, Gun(cut.AddMonths(-1), card.KesimTarihi.Day) }
            .Where(d => d >= track.Baslangic && statements.All(s => s.KesimTarihi != d)).Distinct().ToList();
        if (missing.Count == 0)
            return statements;
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
            if (debt <= 0)
                continue;
            var weights = IadeSonrasiPaylar(b, charge);
            var total = weights.Sum(p => p.Tutar);
            if (weights.Count == 0 || total < debt)
            { unknown += debt; continue; }
            // Brüt borcun ödenmemiş son kısmı: kasada daha önce sayılmış devir
            // borcu da ödeme ile kapanır. Nakit paylarını kullanmak bu kısmı
            // yanlış açık bırakır; kalan tutarı sıfırdan oranlamak da kuruşu taşır.
            shares.AddRange(Oranla(weights, debt, total - debt));
        }
        var result = Adlandir(b.KanalAdlari, shares.GroupBy(p => p.KanalId).OrderBy(g => g.Key)
            .Select(g => new KanalPayYaz(g.Key, g.Sum(p => p.Tutar))));
        if (unknown > 0)
            result.Add(new(null, KanalEtiketleri.DagilimBekliyor, unknown));
        return result;
    }
    public static KrediTakipDto Kredi(KasaDbContext db, int id) => Kredi(new TakipHesapBaglami(db, default, izle: true), id);
    /// <summary>Kredi DTO'su; taksit payları bağlamın bir kez okunmuş kanal adlarıyla adlandırılır.</summary>
    public static KrediTakipDto Kredi(TakipHesapBaglami b, int id)
    {
        b.Iptal.ThrowIfCancellationRequested();
        var db = b.Db;
        var today = b.Bugun;
        var loan = db.Krediler.AsNoTracking().Single(k => k.Id == id);
        var tracking = db.TakipKrediler.AsNoTracking().SingleOrDefault(k => k.KrediId == id);
        if (tracking is null)
        {
            var old = KrediTuretici.TaksitGiderleri(loan.ToCore()).Select((t, i) => new KrediPlanTaksitDto(0, i + 1, t.Tarih, t.TutarTl, t.Tarih <= today ? TaksitDurumlari.KasayaIslendi : TaksitDurumlari.Bekliyor, null, [])).ToList();
            return new(id, 0, loan.Ad, false, true, null, loan.CekilenTutar, loan.CekimTarihi, old.Where(t => t.Tarih > today).Sum(t => t.Tutar), [], old);
        }
        var installments = db.TakipKrediTaksitler.AsNoTracking().Where(t => t.KrediId == id).OrderBy(t => t.No).ToList();
        return new(id, tracking.Surum, loan.Ad, true, tracking.Aktif, tracking.Baslangic, loan.CekilenTutar, loan.CekimTarihi,
            installments.Where(t => !t.Iptal && t.Tarih > today).Sum(t => t.Tutar), Adlandir(b.KanalAdlari, Read<KanalPayYaz>(tracking.CekimPaylariJson)),
            installments.Select(t => new KrediPlanTaksitDto(t.Id, t.No, t.Tarih, t.Tutar, t.Iptal ? TaksitDurumlari.Iptal : t.Tarih <= today ? TaksitDurumlari.KasayaIslendi : TaksitDurumlari.Bekliyor, t.Not, Adlandir(b.KanalAdlari, Read<KanalPayYaz>(t.DagilimJson)))).ToList());
    }
    /// <summary>Bildirim işinin olayları: yazma yolu olarak önce Sync yapar (kendi transaction'ında), sonra olayları
    /// <see cref="TakipOlaylari"/> ile türetir; ardından takipsiz (geçişi yapılmamış) kart ve kredilerin eski model olayları
    /// gelir (<see cref="EskiModelOlaylari"/>). Takipli olayların içeriği ve sırası değişmez.</summary>
    public static IReadOnlyList<TakipOlayDto> GetNotificationEvents(KasaDbContext db, DateOnly today)
    {
        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        Sync(db);
        var b = new TakipHesapBaglami(db);
        var result = TakipOlaylari(b);
        result.AddRange(EskiModelOlaylari.Hepsi(b));
        transaction?.Commit();
        return result;
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
            if (aktif)
                yield return new(TakipKaynaklari.Kart, dto.Id, s.Id, dto.Ad, s.KesimTarihi, s.Borc, TakipOlayTurleri.Kesim, false);
            if (s.Kalan > 0)
                yield return new(TakipKaynaklari.Kart, dto.Id, s.Id, dto.Ad, s.SonOdemeTarihi, s.Kalan, TakipOlayTurleri.SonOdeme, false);
        }
    }
    /// <summary>Bir takipli kredinin olayları (tek kural): iptal edilmemiş her taksit, kasaya otomatik işlenir.</summary>
    internal static IEnumerable<TakipOlayDto> KrediOlaylari(KrediTakipDto dto) => dto.Taksitler.Where(t => t.Durum != TaksitDurumlari.Iptal)
        .Select(t => new TakipOlayDto(TakipKaynaklari.Kredi, dto.Id, t.Id, dto.Ad + " / " + t.No + ". taksit", t.Tarih, t.Tutar, TakipOlayTurleri.Taksit, true));
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
        if (!_kartlar.TryGetValue(kartId, out var veri))
            _kartlar[kartId] = veri = new(this, kartId);
        return veri;
    }
    internal Dictionary<int, List<KanalPayYaz>> KaynakPaylariOnbellegi { get; } = [];
    internal Dictionary<int, FinansTakipServisi.IadeDagilimi> IadeDagilimiOnbellegi { get; } = [];
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
        if (_iadeler is not null)
            return _iadeler;
        var ids = _harcamaIdleri;
        return _iadeler = b.Sorgu(b.Db.TakipHarcamalar).Where(h => h.KaynakHarcamaId != null && ids.Contains(h.KaynakHarcamaId.Value) && !h.Iptal)
            .ToList().ToLookup(h => h.KaynakHarcamaId!.Value);
    }

    private Dictionary<int, TakipIadeHesabiEntity>? _iadeHesaplari;
    /// <summary>Kartın iadelerinin hesap kayıtları (iade Id → kayıt); iadesi olmayan kartta sorgu atılmaz.</summary>
    public IReadOnlyDictionary<int, TakipIadeHesabiEntity> IadeHesaplari(TakipHesapBaglami b)
    {
        if (_iadeHesaplari is not null)
            return _iadeHesaplari;
        var ids = Harcamalar.Where(h => h.KaynakHarcamaId != null).Select(h => h.Id).ToArray();
        return _iadeHesaplari = ids.Length == 0 ? [] : b.Sorgu(b.Db.TakipIadeHesaplari).Where(x => ids.Contains(x.HarcamaId)).ToDictionary(x => x.HarcamaId);
    }

    private Dictionary<int, int>? _avansKaynaklari;
    /// <summary>Kilitli avans dağıtımı ödemeleri (ödeme Id → avansı dağıtılan ödeme Id). Dağıtım ödemesinin tutarı 0'dır
    /// (gerçek ödeme pozitiftir): böyle ödeme yoksa sorgu atılmaz.</summary>
    public IReadOnlyDictionary<int, int> AvansKaynaklari(TakipHesapBaglami b)
    {
        if (_avansKaynaklari is not null)
            return _avansKaynaklari;
        var ids = Odemeler.Where(p => p.Tutar == 0).Select(p => p.Id).ToArray();
        return _avansKaynaklari = ids.Length == 0 ? [] : b.Sorgu(b.Db.TakipAvansTahsisleri).Where(t => ids.Contains(t.OdemeId)).ToDictionary(t => t.OdemeId, t => t.KaynakOdemeId);
    }

    private Dictionary<int, List<KanalPayYaz>>? _alisPaylari;
    /// <summary>Alışa bağlı giderden gelen harcamaların payı (gider Id → pay): bağlar tek sorguda, alışlar tek (bölünmüş)
    /// sorguda okunur; alış başına ödeme dağılımı bir kez hesaplanır. Alışa bağlı olmayan gider sözlükte yoktur.</summary>
    public IReadOnlyDictionary<int, List<KanalPayYaz>> AlisPaylari(TakipHesapBaglami b)
    {
        if (_alisPaylari is not null)
            return _alisPaylari;
        var islemIds = Harcamalar.Where(h => h.IslemId != null).Select(h => h.IslemId!.Value).Distinct().ToArray();
        if (islemIds.Length == 0)
            return _alisPaylari = [];
        var db = b.Db;
        var baglar = db.AlisOdemeler.Where(o => islemIds.Contains(o.IslemId)).Select(o => new { o.IslemId, o.AlisId }).ToList();
        var alisIds = baglar.Select(x => x.AlisId).Distinct().ToArray();
        var alislar = alisIds.Length == 0 ? [] : db.Alislar.AsNoTracking().Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem).Where(a => alisIds.Contains(a.Id)).AsSplitQuery().ToDictionary(a => a.Id);
        return _alisPaylari = baglar.ToDictionary(x => x.IslemId, x => FinansTakipServisi.AlisOdemePayi(alislar[x.AlisId], x.IslemId));
    }
}
