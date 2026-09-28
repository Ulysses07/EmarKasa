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
        // Okumalar salt okunur anlık görüntüde, istek başına tek hesap bağlamıyla (kart verisi bir kez okunur); Sync yapmaz.
        api.MapGet("/kartlar", (KasaDbContext db, CancellationToken ct) => View(db, b => db.KrediKartlari.Select(k => k.Id).ToList().Select(id => Kart(b, id)).ToList(), ct));
        api.MapGet("/kartlar/{id:int}", (int id, KasaDbContext db, CancellationToken ct) => View(db, b => { Require(db.KrediKartlari.Any(k => k.Id == id), "Kart bulunamadı.", 404); return Kart(b, id); }, ct));
        api.MapGet("/krediler", (KasaDbContext db, CancellationToken ct) => View(db, b => db.Krediler.Select(k => k.Id).ToList().Select(id => Kredi(b, id)).ToList(), ct));
        api.MapGet("/krediler/{id:int}", (int id, KasaDbContext db, CancellationToken ct) => View(db, b => { Require(db.Krediler.Any(k => k.Id == id), "Kredi bulunamadı.", 404); return Kredi(b, id); }, ct));
        api.MapGet("/ozet", (int? gun, KasaDbContext db, CancellationToken ct) => View(db, b => Ozet(b, gun ?? 30), ct));

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
        { Text(dto.Aciklama); ManagedCard(db, id).Aktif = dto.Aktif; return id; }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/harcamalar", (int id, KartHarcamaYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartHarcama", dto, () =>
        {
            ApplyCardCharge(db, id, dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/harcamalar/{harcamaId:int}/iptal", (int id, int harcamaId, TakipIptalYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartHarcamaIptal", new { harcamaId, dto }, () =>
        {
            CancelCardCharge(db, id, harcamaId, dto.Aciklama); return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
        api.MapPut("/kartlar/{id:int}/ekstreler/{ekstreId:int}", (int id, int ekstreId, KartEkstreYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartEkstre", new { ekstreId, dto }, () =>
        {
            ManagedCard(db, id); Date(dto.SonOdemeTarihi); Text(dto.Aciklama);
            var s = db.TakipEkstreler.SingleOrDefault(s => s.Id == ekstreId && s.KrediKartiId == id); Require(s is not null, "Ekstre bulunamadı.", 404);
            Require(dto.SonOdemeTarihi >= s!.KesimTarihi, "Son ödeme kesimden önce olamaz.");
            if (dto.AsgariOdeme is { } min) Money(min);
            s.SonOdemeTarihi = dto.SonOdemeTarihi; s.AsgariOdeme = dto.AsgariOdeme; return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
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
            Require(!db.TakipAvansTahsisleri.Any(t => t.OdemeId == odemeId), "Kilitli avans dağıtımı ayrıca iptal edilemez; gerekirse avansı yatıran ödemeyi (dönemi açıksa) iptal edin.", 409);
            payment!.Iptal = true;
            // Avans ödemesi iptal edilince avansının dağıtımları da iptal olur: dağıtılacak avans kalmaz (finance-8).
            var dagitimlar = db.TakipAvansTahsisleri.Where(t => t.KaynakOdemeId == odemeId).Select(t => t.OdemeId).ToList();
            foreach (var dagitim in db.TakipKartOdemeler.Where(p => dagitimlar.Contains(p.Id) && !p.Iptal).ToList()) dagitim.Iptal = true;
            return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
        api.MapGet("/kartlar/{id:int}/devir", (int id, KasaDbContext db) => View(db, () => Devir(db, id)));
        api.MapPost("/kartlar/{id:int}/devir-duzelt", (int id, KartDevirDuzeltYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartDevirDuzelt", dto, () =>
        {
            DevirDuzelt(db, id, dto); return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/gecis-onizleme", (int id, KartGecisYaz dto, KasaDbContext db) => View(db, () => CardPreview(db, id, dto))).RequireAuthorization("Editor");
        api.MapPost("/kartlar/{id:int}/gecis", (int id, KartGecisYaz dto, KasaDbContext db) => Change(db, true, id, dto.Surum, dto.IstekId, "KartGecis", dto, () =>
        {
            var preview = CardPreview(db, id, dto); Require(dto.Onay, "Geçiş önizlemesi onaylanmalı.");
            Require(preview.GenelKasaAnlikFarki <= 0, $"Kasada önceden sayılan tutar önerilen tutarı ({Tl(preview.OnerilenKasadaSayilanTutar!.Value)}) aşamaz: aşan {Tl(preview.GenelKasaAnlikFarki)} kasadan hiçbir zaman düşmez. Bankadaki kalan borcu ve eski kart kayıtlarını doğrulayıp yeniden önizleyin.", 409);
            Require(preview.KabulEdilebilir, $"Kasada önceden sayılan tutar en az {Tl(preview.EnAzKasadaSayilanTutar!.Value)} olmalı: altındaki kısım eski kuralla düşmüş/düşecek borçtur ve ödendiğinde kasadan ikinci kez düşer. Yalnız açılış borcu kasadan ayrıca ödenebilir; tutarı doğrulayıp yeniden önizleyin.", 409);
            KartGecisiYaz(db, id, dto.Baslangic, dto.KalanBorc, dto.KasadaOncedenSayilanTutar, dto.Dagilimlar, dto.Aciklama);
            return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");

        MapLoans(api);
        return app;
    }
    internal static void ApplyCardCharge(KasaDbContext db, int id, KartHarcamaYaz dto)
    {
            var track = ManagedCard(db, id); Require(track.Aktif, "Kart yeni kullanıma kapalı.", 409);
            Money(dto.Tutar, true); Require(dto.Tutar != 0, "Harcama sıfır olamaz."); Text(dto.Aciklama); Date(dto.Tarih);
            Require(dto.Aciklama.Trim() != KartGecisHesabi.DevirAciklamasi, "Bu açıklama eski borç devrine ayrılmıştır; harcamayı başka bir açıklamayla girin.");
            Require(dto.Tarih >= track.Baslangic, "Harcama takip başlangıcından önce olamaz.");
            Require(dto.TaksitSayisi is >= 1 and <= 60 && (dto.Tutar > 0 || dto.TaksitSayisi == 1), "Taksit sayısı 1–60; iade tek taksit olmalı.");
            Require(dto.Tarih.Year <= 9990, "Taksit planı tarih sınırını aşıyor.");
            if (dto.IlkKesimTarihi is { } cut) { Date(cut); Require(cut >= dto.Tarih && cut.Year <= 9990, "İlk kesim harcamadan önce olamaz."); }
            ValidateShares(db, dto.Dagilimlar, Math.Abs(dto.Tutar));
            IReadOnlyList<KanalPayYaz> shares = dto.Dagilimlar;
            TakipIadeHesabiEntity? iadeHesabi = null;
            if (dto.Tutar < 0)
            {
                var source = db.TakipHarcamalar.SingleOrDefault(h => h.Id == dto.KaynakHarcamaId && h.KrediKartiId == id && !h.Iptal && h.Tutar > 0);
                Require(source is not null, "İade için bu karta ait pozitif kaynak harcamayı seçin.");
                var refunded = -db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == source.Id && !h.Iptal).ToList().Sum(h => h.Tutar);
                Require(-dto.Tutar + refunded <= source.Tutar, "Toplam iade kaynak harcamayı aşamaz.");
                var taxIds = db.TakipKartTaksitler.Where(t => t.HarcamaId == source.Id).Select(t => t.Id).ToArray();
                var outstanding = KalanTaksitler(db, id);
                Require(-dto.Tutar <= taxIds.Sum(t => outstanding.GetValueOrDefault(t)), "Ödenmiş harcama iadesi bu akışta desteklenmiyor; mevcut ödeme kayıtları değişmedi.", 409);
                var refunds = db.TakipHarcamalar.Where(h => h.KaynakHarcamaId == source.Id && !h.Iptal).ToList();
                var accounts = IadeHesaplariOku(db.TakipIadeHesaplari, refunds);
                var sourceShares = IadeHesabi(KaynakPaylari(db, source), refunds, accounts).Kalan;
                Require(sourceShares.Count > 0, "İade öncesi kaynak harcamanın kanal dağılımını belirleyin.", 409);
                var paidTotal = db.TakipKartOdemeler.Where(p => p.KrediKartiId == id && !p.Iptal).AsEnumerable()
                    .SelectMany(p => Read<KartTaksitPayi>(p.PaylarJson)).Where(p => taxIds.Contains(p.TaksitId)).Sum(p => p.Tutar);
                // Pay okumalarla aynı kuralla (IadePayi) hesaplanır ve iade anındaki ödenmiş tutar saklanır: kaynak alış sonradan
                // yeniden dağıtılsa da iade payı kaynağın güncel oranını izler (gap-coklu-giris-cift-sayim-mutabakat-3).
                shares = IadePayi(sourceShares, paidTotal, -dto.Tutar, out var overflow);
                Require(overflow == 0, "İade kaynak harcamanın ödenmemiş kanal paylarını aşıyor; mevcut ödeme kayıtları değişmedi.", 409);
                var counted = EtkinKasadaSayilan(source, refunds, accounts);
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
                    Require(SameAt(prior + paidAmount) && SameAt(Math.Max(0, prior - counted))
                        && SameAt(Math.Max(0, prior + paidAmount - counted)),
                        "Bu iade önceki ödemenin kanal paylarını değiştireceği için uygulanamadı; mevcut ödeme kayıtları değişmedi.", 409);
                    prior += paidAmount;
                }
                Require(dto.Dagilimlar.Count == 0 || dto.Dagilimlar.OrderBy(p => p.KanalId).SequenceEqual(shares.OrderBy(p => p.KanalId)), "İade payları kaynak harcamanın oranıyla aynı olmalı; boş bırakırsanız kaynaktan hesaplanır.");
                // finance-2: eski borç devrinin henüz ödenmemiş, kasada önceden sayılmış kısmı iade edilirse o kısım eski
                // kuralla kasadan düşülmüş ama bankaya hiç ödenmeyecektir: iade tarihinde kasaya geri döner ve devrin sonraki
                // ödemelerinde kasada sayılmış kabul edilmez. Önceki ödemelerin kasa etkisi değişmez (düzeltme ödenmemiş
                // sayılmış kısmı aşmaz).
                var correction = counted > 0 ? Math.Min(-dto.Tutar, Math.Max(0, counted - paidTotal)) : 0;
                iadeHesabi = new() { IadeAnindaOdenen = paidTotal, KasadaSayilanDuzeltme = correction };
            }
            else Require(dto.KaynakHarcamaId is null, "Kaynak harcama yalnız iadede seçilir.");
            var charge = new TakipHarcamaEntity { KrediKartiId = id, Tarih = dto.Tarih, Aciklama = dto.Aciklama.Trim(), Tutar = dto.Tutar, TaksitSayisi = dto.TaksitSayisi, DagilimJson = Json(shares), KaynakHarcamaId = dto.KaynakHarcamaId };
            HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == id), charge, dto.IlkKesimTarihi);
            if (iadeHesabi is not null) { iadeHesabi.HarcamaId = charge.Id; db.TakipIadeHesaplari.Add(iadeHesabi); db.SaveChanges(); }
    }
    internal static void CancelCardCharge(KasaDbContext db, int id, int harcamaId, string aciklama)
    {
            ManagedCard(db, id); Text(aciklama);
            var charge = db.TakipHarcamalar.SingleOrDefault(h => h.Id == harcamaId && h.KrediKartiId == id); Require(charge is not null, "Harcama bulunamadı.", 404);
            // Ekstreden gelip alış ödemesine bağlanan harcama ödemenin alıştan ayrılmasıyla ekstre kaydına döner (AlisOdemeIslemleri.Iptal).
            if (charge!.IslemId is { } islem && AlisOdemeIslemleri.DevredilenEkstreSatiri(db, islem) is not null
                && db.AlisOdemeler.Where(o => o.IslemId == islem).Select(o => (int?)o.AlisId).FirstOrDefault() is { } alis)
                Require(false, $"Bu kart harcaması Alış #{alis} ödemesine bağlı; önce alış ödemesini alıştan ayırın (harcama ekstre kaydına döner).", 409);
            Require(charge.IslemId is null, "Alış/gider kaynağı olan harcama için açıklamalı iade girin.", 409);
            // finance-2: iptal, eski kuralla kasadan düşülmüş devrin kasada önceden sayılan tutarını kaybettirirdi.
            Require(charge.KasadaOncedenSayilanTutar <= 0, "Eski borç devri iptal edilemez; kalan borcu veya kasada önceden sayılan tutarı 'Devri düzelt' ile değiştirin.", 409);
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
    /// <summary>Geçişli kartın eski borç devri ve düzeltme sınırları (finance-2).</summary>
    internal static KartDevirDto Devir(KasaDbContext db, int id)
    {
        Require(db.KrediKartlari.Any(k => k.Id == id), "Kart bulunamadı.", 404);
        var track = db.TakipKartlar.AsNoTracking().SingleOrDefault(t => t.KrediKartiId == id);
        Require(track is { EskiKayit: true }, "Bu kart eski karttan geçişle takibe alınmadı; eski borç devri yok.", 404);
        var devir = KartGecisHesabi.AktifDevir(db.TakipHarcamalar.AsNoTracking(), track);
        var oneri = KartGecisHesabi.DevirOnerisi(db, track, devir?.Tutar ?? 0);
        decimal geriDonen = 0;
        if (devir is not null)
        {
            var iadeler = db.TakipHarcamalar.AsNoTracking().Where(h => h.KaynakHarcamaId == devir.Id && !h.Iptal).ToList();
            geriDonen = IadeHesaplariOku(db.TakipIadeHesaplari.AsNoTracking(), iadeler).Values.Sum(h => h.KasadaSayilanDuzeltme);
        }
        var engel = DevirEngeli(db, track, devir);
        return new(devir?.Id, track.Baslangic, devir?.Tutar ?? 0, devir?.KasadaOncedenSayilanTutar ?? 0, geriDonen,
            devir is null ? [] : Adlandir(db, Read<KanalPayYaz>(devir.DagilimJson)), track.EskiDusumKurali.ToString(),
            oneri.SistemKartBorcu, oneri.RaporDisiTutar, oneri.AcilisBorcu, oneri.Onerilen, oneri.EnAz, engel is null, engel);
    }
    /// <summary>Devir düzeltmesinin engeli: kilitli dönem (K4), devre yapılmış ödeme (önceki ödemelerin kasa etkisi değişirdi)
    /// ya da etkin iade (kasaya geri dönen tutar devre bağlı). Engel yoksa null.</summary>
    private static string? DevirEngeli(KasaDbContext db, TakipKartEntity track, TakipHarcamaEntity? devir)
    {
        if (db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single() is { } son && track.Baslangic <= son)
            return $"Devir tarihi ({KartGecisHesabi.Tarih(track.Baslangic)}) kilitli dönemde; düzeltme için ilgili ayı gerekçeyle açın.";
        if (devir is null) return null;
        var taxes = db.TakipKartTaksitler.Where(t => t.HarcamaId == devir.Id).Select(t => t.Id).ToHashSet();
        if (db.TakipKartOdemeler.AsNoTracking().Where(p => p.KrediKartiId == track.KrediKartiId && !p.Iptal).AsEnumerable()
            .Any(p => Read<KartTaksitPayi>(p.PaylarJson).Any(x => taxes.Contains(x.TaksitId))))
            return "Devre ödeme kaydedilmiş: düzeltme önceki ödemelerin kasa etkisini değiştirirdi. Banka borcu farkını iade ya da faiz / masraf olarak girin.";
        if (db.TakipHarcamalar.Any(h => h.KaynakHarcamaId == devir.Id && !h.Iptal))
            return "Devrin iadesi var: önce iadeyi gerekçeyle iptal edin (iadenin kasaya geri döndürdüğü tutar da kalkar).";
        return null;
    }
    /// <summary>Eski borç devrinin düzeltmesi (finance-2): etkin devir iptal edilir, aynı tarih ve açıklamayla yeni kalan
    /// borç, kasada önceden sayılan tutar ve kanal paylarıyla yazılır. Sınırlar geçiş onayındakiyle aynıdır
    /// (<see cref="KartGecisHesabi.DevirOnerisi"/>). Gerekçe, önceki ve yeni değerlerle Change üzerinden denetim izine
    /// yazılır; devir tarihi kilitli dönemdeyse reddedilir (K4).</summary>
    private static void DevirDuzelt(KasaDbContext db, int id, KartDevirDuzeltYaz dto)
    {
        var track = ManagedCard(db, id);
        Require(track.EskiKayit, "Bu kart eski karttan geçişle takibe alınmadı; devir düzeltmesi yalnız geçişli kartta yapılır.", 409);
        Money(dto.KalanBorc, true); Money(dto.KasadaOncedenSayilanTutar); Text(dto.Aciklama); ValidateShares(db, dto.Dagilimlar, Math.Abs(dto.KalanBorc));
        Require(dto.KasadaOncedenSayilanTutar <= Math.Max(0, dto.KalanBorc), "Önceden sayılan tutar kalan borcu aşamaz.");
        var devir = KartGecisHesabi.AktifDevir(db.TakipHarcamalar, track);
        Require(devir?.Id == dto.HarcamaId, "Eski borç devri değişmiş. Yenileyip tekrar deneyin.", 409);
        Require(devir is not null || dto.KalanBorc != 0, "Düzeltilecek devir yok; kalan borç girin.");
        if (DevirEngeli(db, track, devir) is { } engel) Require(false, engel, 409);
        var oneri = KartGecisHesabi.DevirOnerisi(db, track, dto.KalanBorc);
        Require(dto.KasadaOncedenSayilanTutar <= oneri.Onerilen, $"Kasada önceden sayılan tutar önerilen tutarı ({Tl(oneri.Onerilen)}) aşamaz: aşan kısım kasadan hiçbir zaman düşmez.", 409);
        Require(dto.KasadaOncedenSayilanTutar >= oneri.EnAz, $"Kasada önceden sayılan tutar en az {Tl(oneri.EnAz)} olmalı: altındaki kısım eski kuralla düşmüş/düşecek borçtur ve ödendiğinde kasadan ikinci kez düşer.", 409);
        if (devir is not null) devir.Iptal = true;
        if (dto.KalanBorc != 0) HarcamaEkle(db, db.KrediKartlari.Single(c => c.Id == id), new() { KrediKartiId = id, Tarih = track.Baslangic, Aciklama = KartGecisHesabi.DevirAciklamasi,
            Tutar = dto.KalanBorc, KasadaOncedenSayilanTutar = dto.KasadaOncedenSayilanTutar, DagilimJson = Json(dto.Dagilimlar) });
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
        { Text(dto.Aciklama); ManagedLoan(db, id).Aktif = dto.Aktif; return id; }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
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
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
        api.MapPost("/krediler/{id:int}/erken-kapat", (int id, KrediKapatYaz dto, KasaDbContext db) => Change(db, false, id, dto.Surum, dto.IstekId, "KrediKapat", dto, () =>
        {
            var track = ManagedLoan(db, id); Text(dto.Aciklama); Money(dto.Tutar); Date(dto.Tarih); Require(dto.Tarih >= Bugun, "Kapama geçmişe yazılamaz.");
            var rows = db.TakipKrediTaksitler.Where(t => t.KrediId == id).ToList();
            Require(dto.Tarih != Bugun || !rows.Any(t => !t.Iptal && t.Tarih == Bugun), "Bugünkü taksit kasaya işlendi; kapama için yarın veya sonrası seçin.", 409);
            Require(rows.Any(t => !t.Iptal && t.Tarih >= dto.Tarih), "Kapatılacak ileri taksit yok.", 409);
            foreach (var row in rows.Where(t => !t.Iptal && t.Tarih >= dto.Tarih)) row.Iptal = true;
            db.TakipKrediTaksitler.Add(new() { KrediId = id, No = rows.Max(t => t.No) + 1, Tarih = dto.Tarih, Tutar = dto.Tutar, Not = "Erken kapama: " + dto.Aciklama.Trim(), DagilimJson = Json(EsitPaylar(Read<int>(track.KanalIdleriJson), dto.Tutar)) });
            return id;
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
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
        }, gerekce: dto.Aciklama)).RequireAuthorization("Editor");
    }
    private static TakipGecisDto CardPreview(KasaDbContext db, int id, KartGecisYaz d)
    {
        Require(db.KrediKartlari.Any(k => k.Id == id), "Kart bulunamadı.", 404); Require(!db.TakipKartlar.Any(k => k.KrediKartiId == id), "Kart zaten yeni takipte.", 409);
        Date(d.Baslangic); Require(d.Baslangic >= Bugun, "Geçiş tarihi bugün veya sonrası olmalı."); Money(d.KalanBorc, true); Money(d.KasadaOncedenSayilanTutar);
        Require(d.KasadaOncedenSayilanTutar <= Math.Max(0, d.KalanBorc), "Önceden sayılan tutar kalan borcu aşamaz."); Text(d.Aciklama); ValidateShares(db, d.Dagilimlar, Math.Abs(d.KalanBorc));
        Require(!db.Islemler.Any(i => i.KrediKartiId == id && i.Tarih >= d.Baslangic), "Geçiş tarihinden sonraki mevcut harcamaları kapsamayacak bir başlangıç seçin; açık borcu bu tarihte doğrulayın.", 409);
        // Yeni kuralda başlangıçtan önceki her eski gider, bugüne düşen dahil, eski ay sonu
        // kuralıyla aynen düşmeye devam eder; aynı gün geçiş engeli bu yüzden gerekmez.
        var s = KartGecisHesabi.Hesapla(db, id, d.Baslangic, d.KalanBorc, Bugun);
        var fark = d.KasadaOncedenSayilanTutar - s.OnerilenKasadaSayilanTutar;
        var opening = db.KrediKartlari.AsNoTracking().Where(c => c.Id == id).Select(c => c.Borc).Single();
        var minimum = KartGecisHesabi.EnAzKasadaSayilanTutar(s, opening);
        var notes = new List<string>
        {
            "Eski satırlar ve geçiş öncesi kasa sonuçları korunur. Geçişten sonraki kart harcamaları ay sonunda düşmez; yalnız kaydedilen ödemeler kasadan düşer.",
            $"Sistem kart borcu {Tl(s.SistemKartBorcu)} (açılış borcu + eski kart giderleri − eski kart ödemeleri); girilen kalan borç {Tl(d.KalanBorc)}.",
            // Başlangıç ileri tarihteyse bu giderlerin bir kısmının ay sonu henüz gelmemiştir.
            $"Başlangıçtan önce eski ay sonu kuralıyla kasadan düşen/düşecek kart gideri: {Tl(s.EskiKuraldaIslenenTutar)}."
                + (s.BaslangicaKadarDusecekTutar != 0 ? $" Bunun {Tl(s.BaslangicaKadarDusecekTutar)} kısmı henüz düşmedi; bugün ile başlangıç arasındaki ay sonlarında düşecek." : ""),
        };
        if (s.SonBekleyenDusumTarihi is { } last)
            notes.Add($"{Tl(s.BekleyenEskiDusumTutari)} eski kart gideri eski kuralla {last.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)} tarihine kadar ay sonlarında düşmeye devam eder.");
        notes.Add($"Önerilen kasada önceden sayılan tutar: {Tl(s.OnerilenKasadaSayilanTutar)} (kalan borç ile sistem kart borcunun küçüğü). Bu kısım ödendiğinde kasadan tekrar düşmez.");
        if (opening != 0) notes.Add($"Kartın açılış borcu ({Tl(opening)}) eski modelde kasadan hiç düşmedi; önerilen tutar onu da sayılmış kabul eder. Bu borç kasadan ayrıca ödenecekse tutarı en çok bu kadar azaltın.");
        // Önerilenin altı ödemede kasadan düşer; açılış borcu dışındaki kısım eski kuralla zaten düşmüş/düşecek
        // borcun ikinci düşümüdür (eski Windows istemcisi tutarı varsayılan 0 gönderir).
        if (d.KasadaOncedenSayilanTutar < minimum)
            notes.Add($"Girilen tutar önerilenin {Tl(-fark)} altında{(opening > 0 ? $"; açılış borcu bunun en çok {Tl(opening)} kadarını açıklar" : "")}. Aşan kısım eski kuralla düşmüş/düşecek borçtur ve ödendiğinde kasadan ikinci kez düşer: geçiş bu tutarla onaylanamaz, en az {Tl(minimum)} girin.");
        else if (fark < 0) notes.Add($"Girilen tutar önerilenin {Tl(-fark)} altında: bu tutar ödeme yapıldığında kasadan düşer. Yalnız açılış borcu kasadan ayrıca ödenecekse doğrudur; aksi halde ikinci kez düşer.");
        if (fark > 0) notes.Add($"Girilen tutar önerilenin {Tl(fark)} üstünde: bu kısım kasadan hiçbir zaman düşmez; geçiş bu tutarla onaylanamaz.");
        notes.Add("Açılış dağılımını ve tutarları banka/kasa kayıtlarıyla doğrulayın.");
        return new("Kart", id, d.Baslangic, fark, d.Dagilimlar.Count > 0 ? fark : 0, d.KasadaOncedenSayilanTutar, notes, fark <= 0 && d.KasadaOncedenSayilanTutar >= minimum,
            s.SistemKartBorcu, s.EskiKuraldaIslenenTutar, s.BekleyenEskiDusumTutari, s.SonBekleyenDusumTarihi, s.OnerilenKasadaSayilanTutar, minimum);
    }
    private static string Tl(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " TL";
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
    /// <summary>Takip özeti: kartlar ve krediler bir kez hesaplanır, olaylar aynı DTO'lardan türetilir (ikinci hesap yok).
    /// Olay süzgeci: önümüzdeki <paramref name="days"/> gün ve ödenmemiş geciken kart son ödemeleri.</summary>
    internal static TakipOzetDto Ozet(TakipHesapBaglami b, int days)
    {
        Require(days is >= 1 and <= 366, "Gün 1–366 olmalı.");
        var db = b.Db; var today = b.Bugun;
        var cards = db.KrediKartlari.Select(k => k.Id).ToList().Select(id => Kart(b, id)).ToList();
        var debts = cards.SelectMany(c => c.KanalKartBorclari ?? []).GroupBy(p => p.KanalId)
            .OrderBy(g => g.Key is null).ThenBy(g => g.Key)
            .Select(g => new TakipKanalPayi(g.Key, g.First().Kanal, g.Sum(p => p.Tutar))).ToList();
        var loans = db.Krediler.Select(k => k.Id).ToList().Select(id => Kredi(b, id)).ToList();
        var events = TakipOlaylari(b, cards.ToDictionary(c => c.Id), loans.ToDictionary(l => l.Id));
        return new TakipOzetDto(today, cards.Sum(c => Math.Max(0, c.Borc)), loans.Sum(l => l.KalanPlanliOdeme),
            events.Where(e => (e.Tarih >= today && e.Tarih <= today.AddDays(days))
                || (e.Kaynak == "Kart" && e.Tur == "SonOdeme" && e.Tutar > 0 && e.Tarih < today)).OrderBy(e => e.Tarih).ToList(),
            debts, cards.Sum(c => Math.Max(0, -c.Borc)));
    }
    /// <summary>Salt okunur uç (GET ve önizlemeler): tutarlı okuma anlık görüntüsü, yazma kilidi ve Sync yok. Takip
    /// kayıtları yazma yollarının sonunda, tarihe bağlı ekstreler bakım adımında yazılır (okumada türetilir).</summary>
    internal static IResult View(KasaDbContext db, Func<object> read) => Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(read())));
    internal static IResult View(KasaDbContext db, Func<TakipHesapBaglami, object> read, CancellationToken ct) =>
        Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(read(new TakipHesapBaglami(db, ct)))));
    /// <param name="gerekce">Ucun zorunlu tuttuğu açıklama: atılmaz, bu isteğin bütün değişikliklerinin denetim olayına
    /// (önceki/yeni değerle) istek kimliğiyle birlikte yazılır ve GET /api/denetim ile okunur.</param>
    private static IResult Change(KasaDbContext db, bool card, int id, int? version, Guid requestId, string kind, object payload, Func<int> edit, string? gerekce = null) => Safe(() => AlisEndpoints.Mutate(db, () =>
    {
        using var denetim = db.Denetle(gerekce, requestId);
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
