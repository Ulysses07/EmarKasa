using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static partial class FinansTakipEndpoints
{
    public static WebApplication MapFinansTakipEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/takip").RequireAuthorization("Finans");
        MapKartMasrafEndpoints(api);
        api.MapGet("/kartlar", (KasaDbContext db) => View(db, () => db.KrediKartlari.Select(k => k.Id).ToList().Select(id => Kart(db, id)).ToList()));
        api.MapGet("/kartlar/{id:int}", (int id, KasaDbContext db) => View(db, () => { Require(db.KrediKartlari.Any(k => k.Id == id), "Kart bulunamadı.", 404); return Kart(db, id); }));
        api.MapGet("/krediler", (KasaDbContext db) => View(db, () => db.Krediler.Select(k => k.Id).ToList().Select(id => Kredi(db, id)).ToList()));
        api.MapGet("/krediler/{id:int}", (int id, KasaDbContext db) => View(db, () => { Require(db.Krediler.Any(k => k.Id == id), "Kredi bulunamadı.", 404); return Kredi(db, id); }));
        api.MapGet("/ozet", (int? gun, KasaDbContext db) => View(db, () =>
        {
            var days = gun ?? 30; Require(days is >= 1 and <= 366, "Gün 1–366 olmalı.");
            var today = Bugun;
            var cards = db.KrediKartlari.Select(k => k.Id).ToList().Select(id => Kart(db, id)).ToList();
            var debts = cards.SelectMany(c => c.KanalKartBorclari ?? []).GroupBy(p => p.KanalId)
                .OrderBy(g => g.Key is null).ThenBy(g => g.Key)
                .Select(g => new TakipKanalPayi(g.Key, g.First().Kanal, g.Sum(p => p.Tutar))).ToList();
            return new TakipOzetDto(today, cards.Sum(c => Math.Max(0, c.Borc)),
                db.Krediler.Select(k => k.Id).ToList().Sum(id => Kredi(db, id).KalanPlanliOdeme),
                GetNotificationEvents(db, today).Where(e => (e.Tarih >= today && e.Tarih <= today.AddDays(days))
                    || (e.Kaynak == "Kart" && e.Tur == "SonOdeme" && e.Tutar > 0 && e.Tarih < today)).OrderBy(e => e.Tarih).ToList(),
                debts, cards.Sum(c => Math.Max(0, -c.Borc)));
        }));

        api.MapPost("/kartlar", (KartTakipYaz dto, KasaDbContext db) => Change(db, true, 0, null, dto.IstekId, "KartYeni", dto, () =>
        {
            ValidateCard(dto, db, true);
            var card = new KrediKartiEntity { Ad = dto.Ad.Trim(), Limit = dto.Limit, Borc = dto.AcilisBorc,
                KesimTarihi = new(2000, 1, dto.KesimGunu), SonOdemeTarihi = new(2000, 1, dto.SonOdemeGunu) };
            db.KrediKartlari.Add(card); db.SaveChanges();
            db.TakipKartlar.Add(new() { KrediKartiId = card.Id, Baslangic = dto.AcilisTarihi }); db.SaveChanges();
            if (dto.AcilisBorc != 0) HarcamaEkle(db, card, new() { KrediKartiId = card.Id, Tarih = dto.AcilisTarihi, Aciklama = "Açılış borcu", Tutar = dto.AcilisBorc, DagilimJson = Json(dto.AcilisDagilimlari) });
            return card.Id;
        })).RequireAuthorization("Editor");
        api.MapPut("/kartlar/{id:int}", (int id, KartTakipYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartDuzelt", dto, () =>
        {
            ManagedCard(db, id); ValidateCard(dto, db, false);
            var card = db.KrediKartlari.Single(k => k.Id == id);
            card.Ad = dto.Ad.Trim(); card.Limit = dto.Limit; card.KesimTarihi = new(2000, 1, dto.KesimGunu); card.SonOdemeTarihi = new(2000, 1, dto.SonOdemeGunu);
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/durum", (int id, TakipDurumYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartDurum", dto, () =>
        { Text(dto.Aciklama); ManagedCard(db, id).Aktif = dto.Aktif; return id; })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/harcamalar", (int id, KartHarcamaYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartHarcama", dto, () =>
        {
            ApplyCardCharge(db, id, dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/harcamalar/{harcamaId:int}/iptal", (int id, int harcamaId, TakipIptalYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartHarcamaIptal", new { harcamaId, dto }, () =>
        {
            CancelCardCharge(db, id, harcamaId, dto.Aciklama); return id;
        })).RequireAuthorization("Editor");
        api.MapPut("/kartlar/{id:int}/ekstreler/{ekstreId:int}", (int id, int ekstreId, KartEkstreYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartEkstre", new { ekstreId, dto }, () =>
        {
            ManagedCard(db, id); Date(dto.SonOdemeTarihi); Text(dto.Aciklama);
            var s = db.TakipEkstreler.SingleOrDefault(s => s.Id == ekstreId && s.KrediKartiId == id); Require(s is not null, "Ekstre bulunamadı.", 404);
            Require(dto.SonOdemeTarihi >= s!.KesimTarihi, "Son ödeme kesimden önce olamaz.");
            if (dto.AsgariOdeme is { } min) Money(min);
            s.SonOdemeTarihi = dto.SonOdemeTarihi; s.AsgariOdeme = dto.AsgariOdeme; return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/odeme-onizleme", (int id, KartTakipOdemeYaz dto, KasaDbContext db) => View(db, () =>
        { ValidatePayment(db, id, dto); return OdemeEtkisi(db, id, OdemePaylari(db, id, dto.Tutar, dto.EkstreId)); })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/odemeler", (int id, KartTakipOdemeYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartOdeme", dto, () =>
        {
            ValidatePayment(db, id, dto);
            db.TakipKartOdemeler.Add(new() { KrediKartiId = id, Tarih = dto.Tarih, Tutar = dto.Tutar, Not = dto.Not?.Trim(), PaylarJson = Json(OdemePaylari(db, id, dto.Tutar, dto.EkstreId)) });
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/odemeler/{odemeId:int}/iptal", (int id, int odemeId, TakipIptalYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartOdemeIptal", new { odemeId, dto }, () =>
        {
            ManagedCard(db, id); Text(dto.Aciklama);
            var payment = db.TakipKartOdemeler.SingleOrDefault(p => p.Id == odemeId && p.KrediKartiId == id); Require(payment is not null, "Ödeme bulunamadı.", 404);
            payment!.Iptal = true; return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/gecis-onizleme", (int id, KartGecisYaz dto, KasaDbContext db) => View(db, () => CardPreview(db, id, dto))).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/gecis", (int id, KartGecisYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartGecis", dto, () =>
        {
            CardPreview(db, id, dto); Require(dto.Onay, "Geçiş önizlemesi onaylanmalı.");
            db.TakipKartlar.Add(new() { KrediKartiId = id, Baslangic = dto.Baslangic, EskiKayit = true }); db.SaveChanges();
            if (dto.KalanBorc != 0) HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == id), new() { KrediKartiId = id, Tarih = dto.Baslangic, Aciklama = "Onaylanan eski borç devri", Tutar = dto.KalanBorc, KasadaOncedenSayilanTutar = dto.KasadaOncedenSayilanTutar, DagilimJson = Json(dto.Dagilimlar) });
            return id;
        })).RequireAuthorization("Editor");

        MapLoans(api);
        return app;
    }
    internal static void ApplyCardCharge(KasaDbContext db, int id, KartHarcamaYaz dto)
    {
            var track = ManagedCard(db, id); Require(track.Aktif, "Kart yeni kullanıma kapalı.", 409);
            Money(dto.Tutar, true); Require(dto.Tutar != 0, "Harcama sıfır olamaz."); Text(dto.Aciklama); Date(dto.Tarih);
            Require(dto.Tarih >= track.Baslangic, "Harcama takip başlangıcından önce olamaz.");
            Require(dto.TaksitSayisi is >= 1 and <= 60 && (dto.Tutar > 0 || dto.TaksitSayisi == 1), "Taksit sayısı 1–60; iade tek taksit olmalı.");
            Require(dto.Tarih.Year <= 9990, "Taksit planı tarih sınırını aşıyor.");
            if (dto.IlkKesimTarihi is { } cut) { Date(cut); Require(cut >= dto.Tarih && cut.Year <= 9990, "İlk kesim harcamadan önce olamaz."); }
            ValidateShares(db, dto.Dagilimlar, Math.Abs(dto.Tutar));
            IReadOnlyList<KanalPayYaz> shares = dto.Dagilimlar;
            if (dto.Tutar < 0)
            {
                var source = db.TakipHarcamalar.SingleOrDefault(h => h.Id == dto.KaynakHarcamaId && h.KrediKartiId == id && !h.Iptal && h.Tutar > 0);
                Require(source is not null, "İade için bu karta ait pozitif kaynak harcamayı seçin.");
                var refunded = -db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == source.Id && !h.Iptal).ToList().Sum(h => h.Tutar);
                Require(-dto.Tutar + refunded <= source.Tutar, "Toplam iade kaynak harcamayı aşamaz.");
                var taxIds = db.TakipKartTaksitler.Where(t => t.HarcamaId == source.Id).Select(t => t.Id).ToArray();
                var outstanding = KalanTaksitler(db, id);
                Require(-dto.Tutar <= taxIds.Sum(t => outstanding.GetValueOrDefault(t)), "Ödenmiş harcama iadesi bu akışta desteklenmiyor; mevcut ödeme kayıtları değişmedi.", 409);
                var sourceShares = IadeSonrasiPaylar(db, source);
                Require(sourceShares.Count > 0, "İade öncesi kaynak harcamanın kanal dağılımını belirleyin.", 409);
                var paidTotal = db.TakipKartOdemeler.Where(p => p.KrediKartiId == id && !p.Iptal).AsEnumerable()
                    .SelectMany(p => Read<KartTaksitPayi>(p.PaylarJson)).Where(p => taxIds.Contains(p.TaksitId)).Sum(p => p.Tutar);
                var paidShares = Oranla(sourceShares, paidTotal).ToDictionary(p => p.KanalId, p => p.Tutar);
                var unpaidShares = sourceShares.Select(p => new KanalPayYaz(p.KanalId, p.Tutar - paidShares.GetValueOrDefault(p.KanalId))).Where(p => p.Tutar > 0).ToList();
                shares = Oranla(unpaidShares, -dto.Tutar);
                var refundShares = shares.ToDictionary(p => p.KanalId, p => p.Tutar);
                var proposed = sourceShares.Select(p => new KanalPayYaz(p.KanalId, p.Tutar - refundShares.GetValueOrDefault(p.KanalId))).Where(p => p.Tutar > 0).ToList();
                // D'Hondt ağırlıkları değişince eski ödemelerin tek kuruşu bile
                // başka kanala taşınabilir. Bu akış geçmiş payları değiştiremez.
                bool SameAt(decimal total) => Oranla(sourceShares, total).OrderBy(p => p.KanalId).SequenceEqual(Oranla(proposed, total).OrderBy(p => p.KanalId));
                decimal prior = 0;
                foreach (var previous in db.TakipKartOdemeler.Where(p => p.KrediKartiId == id && !p.Iptal).OrderBy(p => p.Id).ToList())
                {
                    var paidAmount = Read<KartTaksitPayi>(previous.PaylarJson).Where(p => taxIds.Contains(p.TaksitId)).Sum(p => p.Tutar);
                    if (paidAmount == 0) continue;
                    Require(SameAt(prior + paidAmount) && SameAt(Math.Max(0, prior - source.KasadaOncedenSayilanTutar))
                        && SameAt(Math.Max(0, prior + paidAmount - source.KasadaOncedenSayilanTutar)),
                        "Bu iade önceki ödemenin kanal paylarını değiştireceği için uygulanamadı; mevcut ödeme kayıtları değişmedi.", 409);
                    prior += paidAmount;
                }
                Require(dto.Dagilimlar.Count == 0 || dto.Dagilimlar.OrderBy(p => p.KanalId).SequenceEqual(shares.OrderBy(p => p.KanalId)), "İade payları kaynak harcamanın oranıyla aynı olmalı; boş bırakırsanız kaynaktan hesaplanır.");
            }
            else Require(dto.KaynakHarcamaId is null, "Kaynak harcama yalnız iadede seçilir.");
            HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == id), new() { KrediKartiId = id, Tarih = dto.Tarih, Aciklama = dto.Aciklama.Trim(), Tutar = dto.Tutar, TaksitSayisi = dto.TaksitSayisi, DagilimJson = Json(shares), KaynakHarcamaId = dto.KaynakHarcamaId }, dto.IlkKesimTarihi);
    }
    internal static void CancelCardCharge(KasaDbContext db, int id, int harcamaId, string aciklama)
    {
            ManagedCard(db, id); Text(aciklama);
            var charge = db.TakipHarcamalar.SingleOrDefault(h => h.Id == harcamaId && h.KrediKartiId == id); Require(charge is not null, "Harcama bulunamadı.", 404);
            Require(charge!.IslemId is null, "Alış/gider kaynağı olan harcama için açıklamalı iade girin.", 409);
            Require(!db.TakipHarcamalar.Any(h => h.KaynakHarcamaId == charge.Id && !h.Iptal), "İadesi bulunan harcama iptal edilemez.", 409);
            if (charge.KaynakHarcamaId is { } refundSource)
            {
                var sourceTaxes = db.TakipKartTaksitler.Where(t => t.HarcamaId == refundSource).Select(t => t.Id).ToHashSet();
                Require(!db.TakipKartOdemeler.Where(p => p.KrediKartiId == id && !p.Iptal).AsEnumerable().Any(p => Read<KartTaksitPayi>(p.PaylarJson).Any(x => sourceTaxes.Contains(x.TaksitId))),
                    "Ödemesi bulunan kaynağın iadesi bu akışta iptal edilemez; önceki ödeme payları korunur.", 409);
            }
            var ids = db.TakipKartTaksitler.Where(t => t.HarcamaId == harcamaId).Select(t => t.Id).ToHashSet();
            Require(!db.TakipKartOdemeler.Where(p => p.KrediKartiId == id && !p.Iptal).AsEnumerable().Any(p => Read<KartTaksitPayi>(p.PaylarJson).Any(x => ids.Contains(x.TaksitId))), "Ödeme bağlı harcama iptal edilemez; iade girin.", 409);
            charge.Iptal = true;
    }
    private static void MapLoans(RouteGroupBuilder api)
    {
        api.MapPost("/krediler", (KrediTakipYaz dto, KasaDbContext db) => Change(db, false, 0, null, dto.IstekId, "KrediYeni", dto, () =>
        {
            Text(dto.Ad); Money(dto.CekilenTutar); Money(dto.AylikOdeme); Date(dto.CekimTarihi); Date(dto.IlkTaksitTarihi); ValidateChannels(db, dto.KanalIdleri);
            Require(dto.TaksitSayisi is >= 1 and <= 600 && dto.IlkTaksitTarihi.Year < 9940, "Taksit sayısı/tarih aralığı geçersiz.");
            Require(dto.IlkTaksitTarihi > dto.CekimTarihi && dto.AylikOdeme > 0, "İlk taksit çekimden sonra, tutar pozitif olmalı."); Money(dto.AylikOdeme * dto.TaksitSayisi);
            var baseline = db.Ayarlar.Select(a => a.TakipBaslangic).First();
            Require(dto.MevcutKredi || dto.CekimTarihi >= baseline, "Yeni çekim takip başlangıcından önce olamaz.");
            if (dto.MevcutKredi) Require(dto.IlkTaksitTarihi >= Bugun, "Mevcut kredide yalnız kalan ileri taksit planını girin.");
            var loan = new KrediEntity { Ad = dto.Ad.Trim(), CekilenTutar = dto.CekilenTutar, CekimTarihi = dto.CekimTarihi, TaksitSayisi = dto.TaksitSayisi,
                AylikOdeme = dto.AylikOdeme, OdemeGunu = dto.IlkTaksitTarihi.Day, KanalId = dto.KanalIdleri.Count == 1 ? dto.KanalIdleri[0] : null,
                Kanal = dto.KanalIdleri.Count == 1 ? db.Kanallar.Single(k => k.Id == dto.KanalIdleri[0]).Ad : Kanallar.Ortak };
            db.Krediler.Add(loan);
            db.GecmisEtkisizKrediOlusturma = dto.MevcutKredi;
            try { db.SaveChanges(); }
            finally { db.GecmisEtkisizKrediOlusturma = false; }
            db.TakipKrediler.Add(new() { KrediId = loan.Id, Baslangic = dto.MevcutKredi ? Bugun : dto.CekimTarihi, MevcutKredi = dto.MevcutKredi,
                KanalIdleriJson = Json(dto.KanalIdleri.Order()), CekimPaylariJson = Json(EsitPaylar(dto.KanalIdleri, dto.CekilenTutar)) }); db.SaveChanges();
            decimal cumulative = 0;
            for (var i = 0; i < dto.TaksitSayisi; i++)
            {
                db.TakipKrediTaksitler.Add(new() { KrediId = loan.Id, No = i + 1, Tarih = Gun(dto.IlkTaksitTarihi.AddMonths(i), dto.IlkTaksitTarihi.Day), Tutar = dto.AylikOdeme,
                    DagilimJson = Json(EsitPaylar(dto.KanalIdleri, dto.AylikOdeme, cumulative)) }); cumulative += dto.AylikOdeme;
            }
            return loan.Id;
        })).RequireAuthorization("Editor");
        api.MapPost("/krediler/{id:int}/durum", (int id, TakipDurumYaz dto, KasaDbContext db) => Change(db, false, id, dto.Surum, dto.IstekId, "KrediDurum", dto, () =>
        { Text(dto.Aciklama); ManagedLoan(db, id).Aktif = dto.Aktif; return id; })).RequireAuthorization("Editor");
        api.MapPut("/krediler/{id:int}/taksitler/{taksitId:int}", (int id, int taksitId, KrediTaksitYaz dto, KasaDbContext db) => Change(db, false, id, dto.Surum, dto.IstekId, "KrediTaksit", new { taksitId, dto }, () =>
        {
            var track = ManagedLoan(db, id); Text(dto.Aciklama); Text(dto.Not, false); Money(dto.Tutar); Date(dto.Tarih);
            var row = db.TakipKrediTaksitler.SingleOrDefault(t => t.Id == taksitId && t.KrediId == id); Require(row is not null, "Taksit bulunamadı.", 404);
            if (row!.Tarih <= Bugun) Require(row.Tarih == dto.Tarih && row.Tutar == dto.Tutar && row.Iptal == dto.Iptal, "Kasaya işlenmiş taksidin tutarı/tarihi değiştirilemez; yalnız not eklenebilir.", 409);
            else Require(dto.Tarih > Bugun && dto.Tarih >= track.Baslangic, "Plan değişikliği ileri bir tarih olmalı.");
            row.Tarih = dto.Tarih; row.Tutar = dto.Tutar; row.Iptal = dto.Iptal; row.Not = dto.Not?.Trim();
            if (row.Tarih > Bugun)
            {
                var rows = db.TakipKrediTaksitler.Where(t => t.KrediId == id).OrderBy(t => t.No).ToList();
                decimal previous = 0;
                foreach (var t in rows.Where(t => !t.Iptal))
                {
                    if (t.Tarih > Bugun) t.DagilimJson = Json(EsitPaylar(Read<int>(track.KanalIdleriJson), t.Tutar, previous));
                    previous += t.Tutar;
                }
            }
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/krediler/{id:int}/erken-kapat", (int id, KrediKapatYaz dto, KasaDbContext db) => Change(db, false, id, dto.Surum, dto.IstekId, "KrediKapat", dto, () =>
        {
            var track = ManagedLoan(db, id); Text(dto.Aciklama); Money(dto.Tutar); Date(dto.Tarih); Require(dto.Tarih >= Bugun, "Kapama geçmişe yazılamaz.");
            var rows = db.TakipKrediTaksitler.Where(t => t.KrediId == id).ToList();
            Require(dto.Tarih != Bugun || !rows.Any(t => !t.Iptal && t.Tarih == Bugun), "Bugünkü taksit kasaya işlendi; kapama için yarın veya sonrası seçin.", 409);
            Require(rows.Any(t => !t.Iptal && t.Tarih >= dto.Tarih), "Kapatılacak ileri taksit yok.", 409);
            foreach (var row in rows.Where(t => !t.Iptal && t.Tarih >= dto.Tarih)) row.Iptal = true;
            db.TakipKrediTaksitler.Add(new() { KrediId = id, No = rows.Max(t => t.No) + 1, Tarih = dto.Tarih, Tutar = dto.Tutar, Not = "Erken kapama: " + dto.Aciklama.Trim(), DagilimJson = Json(EsitPaylar(Read<int>(track.KanalIdleriJson), dto.Tutar)) });
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/krediler/{id:int}/gecis-onizleme", (int id, KrediGecisYaz dto, KasaDbContext db) => View(db, () => LoanPreview(db, id, dto))).RequireAuthorization("Editor");
        api.MapPost("/krediler/{id:int}/gecis", (int id, KrediGecisYaz dto, KasaDbContext db) => Change(db, false, id, dto.Surum, dto.IstekId, "KrediGecis", dto, () =>
        {
            LoanPreview(db, id, dto); Require(dto.Onay, "Geçiş önizlemesi onaylanmalı.");
            var loan = db.Krediler.Single(k => k.Id == id);
            db.TakipKrediler.Add(new() { KrediId = id, Baslangic = dto.Baslangic, EskiKayit = true, MevcutKredi = true, KanalIdleriJson = Json(dto.KanalIdleri.Order()), CekimPaylariJson = Json(EsitPaylar(dto.KanalIdleri, loan.CekilenTutar)) }); db.SaveChanges();
            decimal cumulative = 0;
            foreach (var (t, i) in KrediTuretici.TaksitGiderleri(loan.ToCore()).Select((t, i) => (t, i)))
                if (t.Tarih >= dto.Baslangic)
                {
                    db.TakipKrediTaksitler.Add(new() { KrediId = id, No = i + 1, Tarih = t.Tarih, Tutar = t.TutarTl, DagilimJson = Json(EsitPaylar(dto.KanalIdleri, t.TutarTl, cumulative)) }); cumulative += t.TutarTl;
                }
            return id;
        })).RequireAuthorization("Editor");
    }
    private static TakipGecisDto CardPreview(KasaDbContext db, int id, KartGecisYaz d)
    {
        Require(db.KrediKartlari.Any(k => k.Id == id), "Kart bulunamadı.", 404); Require(!db.TakipKartlar.Any(k => k.KrediKartiId == id), "Kart zaten yeni takipte.", 409);
        Date(d.Baslangic); Require(d.Baslangic >= Bugun, "Geçiş tarihi bugün veya sonrası olmalı."); Money(d.KalanBorc, true); Money(d.KasadaOncedenSayilanTutar);
        Require(d.KasadaOncedenSayilanTutar <= Math.Max(0, d.KalanBorc), "Önceden sayılan tutar kalan borcu aşamaz."); Text(d.Aciklama); ValidateShares(db, d.Dagilimlar, Math.Abs(d.KalanBorc));
        Require(!db.Islemler.Any(i => i.KrediKartiId == id && i.Tarih >= d.Baslangic), "Geçiş tarihinden sonraki mevcut harcamaları kapsamayacak bir başlangıç seçin; açık borcu bu tarihte doğrulayın.", 409);
        if (d.Baslangic == Bugun)
        {
            var sameDayEffect = db.Islemler.Where(i => i.KrediKartiId == id).AsEnumerable().Any(i =>
            {
                var next = i.Tarih.AddMonths(1);
                return new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month)) == Bugun;
            });
            Require(!sameDayEffect, "Bugünkü eski kart düşümü kasaya işlendi; geçiş için yarın veya sonrası seçin.", 409);
        }
        return new("Kart", id, d.Baslangic, 0, 0, d.KasadaOncedenSayilanTutar,
            ["Eski satırlar ve geçiş öncesi kasa sonuçları korunur.", "Geçişten sonraki otomatik ay sonu düşümü durur; yalnız kaydedilen ödemeler işler.", "Önceden kasada sayılan borç kısmı yeni ödemede tekrar düşmez. Açılış dağılımını ve bu tutarı banka/kasa kayıtlarıyla doğrulayın."], true);
    }
    private static TakipGecisDto LoanPreview(KasaDbContext db, int id, KrediGecisYaz d)
    {
        Require(db.Krediler.Any(k => k.Id == id), "Kredi bulunamadı.", 404); Require(!db.TakipKrediler.Any(k => k.KrediId == id), "Kredi zaten yeni takipte.", 409);
        Date(d.Baslangic); Require(d.Baslangic >= Bugun, "Geçiş tarihi bugün veya sonrası olmalı."); Text(d.Aciklama); ValidateChannels(db, d.KanalIdleri);
        Require(!db.KrediTaksitOdemeler.Any(p => p.KrediId == id), "Eski gerçek ödeme bağlantıları ayrıca mutabakat gerektiriyor; otomatik geçiş yapılamaz.", 409);
        if (d.Baslangic == Bugun)
            Require(!KrediTuretici.TaksitGiderleri(db.Krediler.Single(k => k.Id == id).ToCore()).Any(t => t.Tarih == Bugun), "Bugünkü kredi taksidi kasaya işlendi; geçiş için yarın veya sonrası seçin.", 409);
        return new("Kredi", id, d.Baslangic, 0, 0, 0, ["Geçmiş çekim yeniden kasaya girmez; geçmiş kanal bakiyesi değiştirilmez.", "Başlangıçtan itibaren kalan taksitler seçili sabit kanallara eşit bölünür. Önceki taksitler eski kuralla korunur."], true);
    }
    internal static void ValidateShares(KasaDbContext db, IReadOnlyList<KanalPayYaz>? shares, decimal amount)
    {
        Require(shares is not null && shares.Count <= 100, "En fazla 100 kanal payı girilebilir.");
        foreach (var p in shares!) { Require(p is not null, "Boş kanal payı olamaz."); Money(p!.Tutar); }
        Require(shares.Select(p => p.KanalId).Distinct().Count() == shares.Count, "Kanal payları tekil olmalı.");
        var ids = db.Kanallar.Select(k => k.Id).ToHashSet(); Require(shares.All(p => ids.Contains(p.KanalId)), "Kayıtlı kanal seçin.");
        Require(shares.Count == 0 || shares.Sum(p => p.Tutar) == amount, "Kanal payları toplamı tutara eşit olmalı.");
    }
    private static void ValidateChannels(KasaDbContext db, IReadOnlyList<int>? ids)
    {
        Require(ids is not null && ids.Count is >= 1 and <= 100, "1–100 kanal seçin."); var known = db.Kanallar.Select(k => k.Id).ToHashSet();
        Require(ids!.Distinct().Count() == ids.Count && ids.All(known.Contains), "Kayıtlı ve tekil kanallar seçin.");
    }
    private static void ValidateCard(KartTakipYaz d, KasaDbContext db, bool create)
    {
        Text(d.Ad); Money(d.Limit); Require(d.KesimGunu is >= 1 and <= 31 && d.SonOdemeGunu is >= 1 and <= 31, "Gün 1–31 olmalı.");
        if (!create) return;
        Date(d.AcilisTarihi); Money(d.AcilisBorc, true); Require(d.AcilisTarihi >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "Açılış takip başlangıcından önce olamaz.");
        ValidateShares(db, d.AcilisDagilimlari, Math.Abs(d.AcilisBorc));
    }
    internal static void ValidatePayment(KasaDbContext db, int id, KartTakipOdemeYaz d)
    {
        var track = ManagedCard(db, id); Date(d.Tarih); Money(d.Tutar); Require(d.Tutar > 0 && d.Tarih >= track.Baslangic && d.Tarih <= Bugun, "Ödeme pozitif ve takip başlangıcı ile bugün arasında olmalı."); Text(d.Not, false);
        Require(d.EkstreId is null || db.TakipEkstreler.Any(e => e.Id == d.EkstreId && e.KrediKartiId == id), "Bu karta ait ekstre seçin.");
    }
    internal static TakipKartEntity ManagedCard(KasaDbContext db, int id) { var t = db.TakipKartlar.SingleOrDefault(t => t.KrediKartiId == id); Require(t is not null, "Bu eski kart için önce geçiş önizlemesini onaylayın.", 409); return t!; }
    private static TakipKrediEntity ManagedLoan(KasaDbContext db, int id) { var t = db.TakipKrediler.SingleOrDefault(t => t.KrediId == id); Require(t is not null, "Bu eski kredi için önce geçiş önizlemesini onaylayın.", 409); return t!; }
    internal static IResult View(KasaDbContext db, Func<object> read) => Safe(() => AlisEndpoints.Mutate(db, () => { Sync(db); return Results.Ok(read()); }));
    private static IResult Change(KasaDbContext db, bool card, int id, int? version, Guid requestId, string kind, object payload, Func<int> edit) => Safe(() => AlisEndpoints.Mutate(db, () =>
    {
        var node = JsonSerializer.SerializeToNode(payload)!;
        void RemoveVersion(JsonNode? n) { if (n is JsonObject o) { o.Remove("Surum"); foreach (var child in o.ToArray()) RemoveVersion(child.Value); } else if (n is JsonArray a) foreach (var child in a) RemoveVersion(child); }
        RemoveVersion(node); var digest = FinansHesaplari.Ozet(new { id, payload = node.ToJsonString() });
        Sync(db);
        if (FinansHesaplari.Tekrar(db, requestId, kind, digest, key => Results.Ok(card ? (object)Kart(db, key) : Kredi(db, key))) is { } replay) return replay;
        if (id != 0)
        {
            Require(card ? db.KrediKartlari.Any(k => k.Id == id) : db.Krediler.Any(k => k.Id == id), "Kayıt bulunamadı.", 404);
            var current = card ? db.TakipKartlar.Where(t => t.KrediKartiId == id).Select(t => (int?)t.Surum).SingleOrDefault() : db.TakipKrediler.Where(t => t.KrediId == id).Select(t => (int?)t.Surum).SingleOrDefault();
            Require(version == (current ?? 0), "Kayıt değişmiş. Yenileyip tekrar deneyin.", 409);
        }
        var resultId = edit();
        if (card) db.TakipKartlar.Single(t => t.KrediKartiId == resultId).Surum++;
        else db.TakipKrediler.Single(t => t.KrediId == resultId).Surum++;
        FinansHesaplari.IstekKaydet(db, requestId, kind, digest, resultId); db.SaveChanges();
        Sync(db);
        return Results.Ok(card ? (object)Kart(db, resultId) : Kredi(db, resultId));
    }));
    internal static IResult Safe(Func<IResult> run) { try { return run(); } catch (TakipHatasi e) { return Results.Json(new { hata = e.Message }, statusCode: e.Status); } }
    private sealed class TakipHatasi(int status, string message) : Exception(message) { public int Status => status; }
    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string message, int status = 400) { if (!valid) throw new TakipHatasi(status, message); }
    private static void Money(decimal value, bool signed = false) => Require(value >= (signed ? -999_999_999_999.99m : 0) && value <= 999_999_999_999.99m && decimal.Round(value, 2) == value, "Tutar kuruş hassasiyetinde ve izin verilen sınırda olmalı.");
    private static void Date(DateOnly value) => Require(value != default && value.Year <= 9990, "Geçerli tarih seçin.");
    private static void Text(string? value, bool required = true) => Require((!required || !string.IsNullOrWhiteSpace(value)) && (value?.Length ?? 0) <= 2000, "Açıklama/ad boş olamaz ve 2000 karakteri aşamaz.");
}
