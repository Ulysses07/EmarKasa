using System.Security.Cryptography;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static class EkstreImportEndpoints
{
    private const int FileLimit = 10 * 1024 * 1024;
    private static readonly string[] Banks = ["Vakifbank", "Akbank", "QNB", "Isbank", "Garanti", "Denizbank"];

    public static WebApplication MapEkstreImportEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/ekstre-aktar").RequireAuthorization("Editor");
        api.MapPost("/yukle", Upload).RequireRateLimiting("guvenlik")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(FileLimit + 65536),
                new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = FileLimit + 65536 });
        api.MapGet("", (int? beforeId, KasaDbContext db) => Safe(() =>
        {
            Require(beforeId is null or > 0, "Geçerli belge sayfası seçin.");
            return Results.Ok(db.EkstreBelgeler.AsNoTracking().Where(d => beforeId == null || d.Id < beforeId).OrderByDescending(d => d.Id).Take(50)
            .Select(d => new { d.Id, d.Surum, d.Kaynak, d.Banka, d.HesapAdi, d.KartId, d.DosyaAdi, d.Yuklendi, d.SatirlarJson }).ToList()
            .Select(d => new EkstreBelgeOzetDto(d.Id, d.Surum, d.Kaynak, d.Banka, d.HesapAdi, d.KartId, d.DosyaAdi,
                DateTimeOffset.FromUnixTimeMilliseconds(d.Yuklendi), Read<EkstreOkunanSatir>(d.SatirlarJson).Count, db.EkstreKayitlar.Count(k => k.BelgeId == d.Id && !k.Iptal))).ToList());
        }));
        api.MapGet("/{id:int}", (int id, KasaDbContext db) => Safe(() => Results.Ok(Document(db, id))));
        api.MapGet("/kayitlar/{kayitId:int}", (int kayitId, KasaDbContext db) => Safe(() =>
        {
            var documentId = db.EkstreKayitlar.AsNoTracking().Where(k => k.Id == kayitId).Select(k => (int?)k.BelgeId).SingleOrDefault();
            Require(documentId is not null, "Kaynak ekstre kaydı bulunamadı.", 404);
            return Results.Ok(Document(db, documentId.Value));
        }));
        api.MapGet("/{id:int}/dosya", (int id, KasaDbContext db, HttpResponse response) =>
        {
            var d = db.EkstreBelgeler.AsNoTracking().SingleOrDefault(d => d.Id == id);
            if (d is null) return Results.NotFound();
            response.Headers.CacheControl = "private, no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(d.Dosya, "application/pdf", d.DosyaAdi);
        });
        api.MapPost("/{id:int}/onizleme", (int id, EkstreKaydetYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            Sync(db); return Results.Ok(Preview(db, id, dto));
        })));
        api.MapPost("/{id:int}/kaydet", (int id, EkstreKaydetYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var digest = FinansHesaplari.Ozet(new { id, dto.Satirlar, dto.OnizlemeOzeti, dto.TekrarOnay });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreKaydet", digest, key => Results.Ok(Document(db, key))) is { } old) return old;
            Sync(db);
            var preview = Preview(db, id, dto);
            Require(preview.OnizlemeOzeti == dto.OnizlemeOzeti, "Bilgiler değişmiş. Yeniden önizleyip onaylayın.", 409);
            Require(!preview.TekrarOnayGerekli || dto.TekrarOnay, "Önizlemedeki benzer kayıt ve belirsizlik uyarılarını kontrol edip ayrıca onaylayın.", 409);
            var document = GetDocument(db, id);
            db.EkstreDegisikligi = true;
            try
            {
                var applied = dto.Satirlar.Select(row => Apply(db, document, row)).ToList();
                RefreshPaymentSnapshots(db, applied);
                document.Surum++;
                FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreKaydet", digest, id); db.SaveChanges(); Sync(db);
            }
            finally { db.EkstreDegisikligi = false; }
            return Results.Ok(Document(db, id));
        })));
        api.MapPost("/{id:int}/kayitlar/{kayitId:int}/iptal", (int id, int kayitId, EkstreIptalYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            Require(!string.IsNullOrWhiteSpace(dto.Aciklama) && dto.Aciklama.Length <= 2000, "İptal gerekçesi girin (en fazla 2000 karakter).");
            var digest = FinansHesaplari.Ozet(new { id, kayitId, Aciklama = dto.Aciklama.Trim() });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreIptal", digest, key => Results.Ok(Document(db, key))) is { } old) return old;
            var document = GetDocument(db, id);
            var row = db.EkstreKayitlar.SingleOrDefault(k => k.Id == kayitId && k.BelgeId == id);
            Require(row is not null, "Ekstre kaydı bulunamadı.", 404);
            Require(!row.Iptal, "Bu satır zaten iptal edilmiş.", 409);
            AyKilidiKurallari.TarihAcik(db, row.Tarih);
            db.EkstreDegisikligi = true;
            try
            {
                if (row.IslemId is { } expense) db.Islemler.Remove(db.Islemler.Single(i => i.Id == expense));
                if (row.KartHarcamaId is { } charge) FinansTakipEndpoints.CancelCardCharge(db, row.KrediKartiId!.Value, charge, dto.Aciklama);
                if (row.KartOdemeId is { } payment) db.TakipKartOdemeler.Single(p => p.Id == payment).Iptal = true;
                if (row.KrediKartiId is { } card) db.TakipKartlar.Single(t => t.KrediKartiId == card).Surum++;
                row.Iptal = true; row.IptalAciklamasi = dto.Aciklama.Trim(); document.Surum++;
                FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreIptal", digest, id); db.SaveChanges(); Sync(db);
            }
            finally { db.EkstreDegisikligi = false; }
            return Results.Ok(Document(db, id));
        })));
        return app;
    }

    private static async Task<IResult> Upload(HttpRequest request, KasaDbContext db, IPdfMetinOkuyucu pdf, CancellationToken ct)
    {
        if (request.ContentLength is > FileLimit + 65536) return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
        if (!request.HasFormContentType) return Error("PDF dosyasını yükleyin.");
        IFormCollection form;
        try { form = await request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return Error("Dosya boyutu veya yükleme biçimi geçersiz.", 413); }
        var file = form.Files.GetFile("dosya"); var source = form["kaynak"].ToString(); var bank = form["banka"].ToString();
        var account = form["hesapAdi"].ToString().Trim(); int? card = int.TryParse(form["kartId"], out var parsed) ? parsed : null;
        if (source is not ("Kart" or "Banka") || !Banks.Contains(bank)) return Error("Geçerli kaynak ve banka seçin.");
        if (source == "Banka" && (account.Length is < 1 or > 100 || card is not null)) return Error("Banka hesabına kısa bir ad girin (en fazla 100 karakter).");
        if (source == "Kart" && (card is null || !db.KrediKartlari.Any(k => k.Id == card))) return Error("Kart seçin.");
        if (source == "Kart") account = "";
        if (file is null || file.Length is <= 0 or > FileLimit) return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            var block = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(block, ct)) > 0)
            {
                if (buffer.Length + count > FileLimit) return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
                await buffer.WriteAsync(block.AsMemory(0, count), ct);
            }
            bytes = buffer.ToArray();
        }
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return Error("Geçerli PDF dosyası seçin.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var existing = db.EkstreBelgeler.AsNoTracking().SingleOrDefault(d => d.DosyaOzeti == hash);
        IResult Existing(EkstreBelgeEntity d) => d.Kaynak == source && d.Banka == bank && d.HesapAdi == account && d.KartId == card
            ? Results.Ok(Document(db, d.Id)) : Error("Bu PDF farklı kaynak bilgileriyle yüklenmiş. İlk belgeden devam edin.", 409);
        if (existing is not null) return Existing(existing);
        EkstreOkumaSonucu read;
        try { read = EkstreMetinOkuyucu.Oku(await pdf.OkuAsync(bytes, ct), source, bank); }
        catch (PdfOkumaException e) { return Error(e.Message, e.StatusCode); }
        if (read.Satirlar.Count > 1500) return Error("PDF en fazla 1500 hareket içerebilir.", 422);
        var rows = Json(read.Satirlar); var warnings = Json(read.Uyarilar);
        if (rows.Length > 4_000_000 || warnings.Length > 100_000) return Error("PDF metni çok uzun; daha kısa tarih aralığı seçin.", 422);
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).Take(180).ToArray());
        if (string.IsNullOrWhiteSpace(name)) name = "ekstre.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        return Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var old = db.EkstreBelgeler.SingleOrDefault(d => d.DosyaOzeti == hash); if (old is not null) return Existing(old);
            var d = new EkstreBelgeEntity { Kaynak = source, Banka = bank, HesapAdi = account, KartId = card,
                DosyaAdi = name, DosyaOzeti = hash, Dosya = bytes, Yuklendi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), SatirlarJson = rows, UyarilarJson = warnings };
            db.EkstreBelgeler.Add(d); db.SaveChanges(); return Results.Ok(Document(db, d.Id));
        }));
    }

    private static EkstreOnizlemeDto Preview(KasaDbContext db, int id, EkstreKaydetYaz dto)
    {
        var document = GetDocument(db, id);
        Require(document.Surum == dto.Surum, "Belge değişmiş. Yenileyip yeniden önizleyin.", 409);
        Require(dto.Satirlar is { Count: > 0 and <= 1500 } && dto.Satirlar.All(r => r is not null), "İşlenecek satırları seçin.");
        Require(dto.Satirlar.Select(r => r.SatirNo).Distinct().Count() == dto.Satirlar.Count, "Bir kaynak satırı iki kez seçilemez.");
        var sourceRows = Read<EkstreOkunanSatir>(document.SatirlarJson).ToDictionary(r => r.No);
        var output = new List<EkstreSatirOnizleme>(); var warnings = new List<string>(); bool confirm = false;
        var oldPayments = db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal).ToList()
            .Where(p => Read<KartTaksitPayi>(p.PaylarJson).Any(a => a.TaksitId == 0 && a.Tutar > 0))
            .ToDictionary(p => p.Id, p => Json(OdemeEtkisi(db, p.KrediKartiId, Read<KartTaksitPayi>(p.PaylarJson), p.Id).Dagilimlar));
        var selected = new List<(EkstreSatirYaz Row, EkstreKayitEntity Applied, List<string> Notices)>();
        var cardVersions = db.TakipKartlar.OrderBy(c => c.KrediKartiId).Select(c => new { c.KrediKartiId, c.Surum }).ToList();
        var locked = db.AyKilidi.AsNoTracking().Single().KilitliSonTarih;
        var tx = db.Database.CurrentTransaction!;
        tx.CreateSavepoint("ekstre_preview"); db.EkstreDegisikligi = true;
        try
        {
            foreach (var row in dto.Satirlar)
            {
                Require(sourceRows.TryGetValue(row.SatirNo, out var original), "Kaynak satır bu PDF'de bulunamadı.");
                Validate(db, document, row, original);
                var notices = original.Uyarilar.ToList();
                if (original.ParaBirimi == "Belirsiz") notices.Add("Para birimi okunamadı. Bu satırı TL olarak kaydettiğinizi doğrulayın.");
                if (original.Yon == "Belirsiz") notices.Add("Giriş/çıkış yönü okunamadı; seçtiğiniz işlem türünü doğrulayın.");
                if (original.OnerilenIslem != row.IslemTuru) notices.Add("İşlem türü PDF önerisinden farklı; yönünü ve kasaya etkisini kontrol edin.");
                if (document.Kaynak == "Banka" && original.Sinif == "Transfer") notices.Add("Kendi hesaplarınız arasındaki transfer genel kasayı değiştirmez; böyle bir satırı seçmeden bırakın. Gelir/gider olarak işlerseniz genel kasa değişir.");
                notices.AddRange(Duplicates(db, document, row));
                var applied = Apply(db, document, row);
                selected.Add((row, applied, notices));
            }
            // A later selected charge may allocate an earlier payment's advance.
            // Present the final batch state, not the intermediate row order.
            RefreshPaymentSnapshots(db, selected.Select(s => s.Applied));
            foreach (var (row, applied, notices) in selected)
            {
                var cash = row.IslemTuru switch { "Gelir" => row.Tutar, "Gider" => -row.Tutar, "KartOdemesi" => -PaymentEffect(db, applied).KasaEtkisi, _ => 0m };
                var distinct = notices.Distinct().ToList(); confirm |= distinct.Count > 0;
                output.Add(new(row.SatirNo, row.Tarih, row.Aciklama.Trim(), row.Tutar, row.IslemTuru, cash, Read<TakipKanalPayi>(applied.DagilimJson), distinct));
            }
            foreach (var old in db.TakipKartOdemeler.Where(p => oldPayments.Keys.Contains(p.Id)).ToList())
            {
                var current = OdemeEtkisi(db, old.KrediKartiId, Read<KartTaksitPayi>(old.PaylarJson), old.Id).Dagilimlar;
                if (Json(current) != oldPayments[old.Id])
                {
                    warnings.Add("Önceden kaydedilen kart avansının kanal dağılımı bu harcamalarla belirlenir: " +
                        string.Join(", ", current.Select(p => $"{p.Kanal}: {p.Tutar.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} TL")) + ". Genel kasadan yeniden düşmez.");
                    confirm = true;
                }
            }
        }
        finally
        {
            tx.RollbackToSavepoint("ekstre_preview"); tx.ReleaseSavepoint("ekstre_preview"); db.ChangeTracker.Clear(); db.EkstreDegisikligi = false;
        }
        if (confirm) warnings.Add("İşaretli satırların yönünü, tutarını ve benzer kayıtlarını kontrol edin; ayrı kayıt olarak işleneceğini ayrıca onaylayın.");
        var digest = FinansHesaplari.Ozet(new { id, dto.Surum, dto.Satirlar, output, warnings, confirm, cardVersions, locked });
        return new(digest, output.Sum(r => r.KasaEtkisi), output, warnings, confirm);
    }

    private static void Validate(KasaDbContext db, EkstreBelgeEntity doc, EkstreSatirYaz row, EkstreOkunanSatir original)
    {
        Require(original.ParaBirimi is "TRY" or "TL" or "Belirsiz", "Yalnız TL hareketleri kaydedilebilir.");
        Require(!db.EkstreKayitlar.Any(k => k.BelgeId == doc.Id && k.SatirNo == row.SatirNo && !k.Iptal), "Bu kaynak satır zaten kaydedilmiş; geçmişinden devam edin.", 409);
        Require(row.Tarih != default && row.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First() && row.Tarih <= Bugun, "Hareket tarihi takip başlangıcı ile bugün arasında olmalı.");
        AyKilidiKurallari.TarihAcik(db, row.Tarih);
        Require(row.Tutar > 0 && row.Tutar <= 999_999_999_999.99m && decimal.Round(row.Tutar, 2) == row.Tutar, "Tutar pozitif ve kuruş hassasiyetinde olmalı.");
        Require(!string.IsNullOrWhiteSpace(row.Aciklama) && row.Aciklama.Length <= 2000, "Açıklama 1–2000 karakter olmalı.");
        Require(doc.Kaynak == "Banka" ? row.IslemTuru is "Gelir" or "Gider" or "KartOdemesi" : row.IslemTuru is "KartHarcama" or "KartIade" or "KartOdemesi", "Bu belge kaynağı için geçerli işlem türü seçin.");
        Require(row.Dagilimlar is not null && row.Dagilimlar.All(p => p is not null), "Kanal dağılımı geçersiz.");
        if (row.IslemTuru is "KartOdemesi" or "KartIade") Require(row.DagilimTuru == "Otomatik" && row.Dagilimlar.Count == 0, "Kart ödemesi/iadesi kaynak borçtan otomatik dağılır.");
        else ResolveShares(db, row);
        Require(row.IslemTuru == "KartIade" || row.KaynakHarcamaId is null, "Kaynak harcama yalnız kart iadesinde seçilir.");
        if (row.IslemTuru is "Gelir" or "Gider") Require(row.KrediKartiId is null, "Banka gelir/giderinde kart seçilemez.");
        else
        {
            var card = CardId(doc, row); var tracking = FinansTakipEndpoints.ManagedCard(db, card);
            Require(tracking.Aktif || row.IslemTuru == "KartOdemesi", "Kart yeni harekete kapalı.", 409);
            Require(row.Tarih >= tracking.Baslangic, "Tarih kart takip başlangıcından önce olamaz.");
        }
    }

    private static List<KanalPayYaz> ResolveShares(KasaDbContext db, EkstreSatirYaz row)
    {
        if (row.DagilimTuru == "Genel")
        {
            Require(row.IslemTuru is "Gelir" or "Gider" && row.Dagilimlar.Count == 0, "Yalnız banka gelir/gideri genel kasaya yazılabilir."); return [];
        }
        Require(row.DagilimTuru is "Esit" or "Ozel" && row.Dagilimlar.Count > 0, "Dağıtılacak kanalları seçin.");
        var ids = row.Dagilimlar.Select(p => p.KanalId).ToList();
        Require(ids.Distinct().Count() == ids.Count && db.Kanallar.Count(k => ids.Contains(k.Id)) == ids.Count, "Kayıtlı kanalları birer kez seçin.");
        if (row.DagilimTuru == "Esit") { Require(row.Dagilimlar.All(p => p.Tutar == 0), "Eşit dağılımda kanal tutarı sıfır gönderilmeli."); return EsitPaylar(ids, row.Tutar); }
        Require(row.Dagilimlar.All(p => p.Tutar > 0 && p.Tutar <= row.Tutar && decimal.Round(p.Tutar, 2) == p.Tutar) && row.Dagilimlar.Sum(p => p.Tutar) == row.Tutar, "Kanal tutarları pozitif olmalı ve hareket tutarına eşitlenmeli.");
        return row.Dagilimlar.OrderBy(p => p.KanalId).ToList();
    }

    private static int CardId(EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        Require(doc.Kaynak != "Kart" || row.KrediKartiId is null || row.KrediKartiId == doc.KartId, "Kart belgesinin kartı değiştirilemez.");
        var card = doc.Kaynak == "Kart" ? doc.KartId : row.KrediKartiId;
        Require(card is > 0, "Ödemenin kartını seçin."); return card.Value;
    }

    private static EkstreKayitEntity Apply(KasaDbContext db, EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        var result = new EkstreKayitEntity { BelgeId = doc.Id, SatirNo = row.SatirNo, Tarih = row.Tarih,
            Aciklama = row.Aciklama.Trim(), Tutar = row.Tutar, IslemTuru = row.IslemTuru, DagilimTuru = row.DagilimTuru };
        if (row.IslemTuru is "Gelir" or "Gider")
        {
            var shares = Adlandir(db, ResolveShares(db, row)); result.DagilimJson = Json(shares);
            if (row.IslemTuru == "Gider")
            {
                var expense = new IslemEntity { Tarih = row.Tarih, Cari = row.Aciklama.Trim(), TutarTl = row.Tutar,
                    Kanal = shares.Count == 1 ? shares[0].Kanal : "Genel kasa", KanalId = shares.Count == 1 ? shares[0].KanalId : null,
                    Tip = GiderTipi.Cari, Not = "PDF hesap hareketi · " + doc.Banka + " · " + doc.HesapAdi };
                db.Islemler.Add(expense); db.SaveChanges(); result.IslemId = expense.Id;
            }
        }
        else
        {
            var cardId = CardId(doc, row); result.KrediKartiId = cardId;
            var track = FinansTakipEndpoints.ManagedCard(db, cardId);
            if (row.IslemTuru == "KartOdemesi")
            {
                var paymentDto = new KartTakipOdemeYaz(Guid.NewGuid(), track.Surum, row.Tarih, row.Tutar, Not: row.Aciklama.Trim());
                FinansTakipEndpoints.ValidatePayment(db, cardId, paymentDto);
                var allocations = OdemePaylari(db, cardId, row.Tutar, null);
                result.DagilimJson = Json(OdemeEtkisi(db, cardId, allocations).Dagilimlar);
                var payment = new TakipKartOdemeEntity { KrediKartiId = cardId, Tarih = row.Tarih, Tutar = row.Tutar, Not = row.Aciklama.Trim(), PaylarJson = Json(allocations) };
                db.TakipKartOdemeler.Add(payment); db.SaveChanges(); result.KartOdemeId = payment.Id;
            }
            else
            {
                var refund = row.IslemTuru == "KartIade";
                var shares = refund ? new List<KanalPayYaz>() : ResolveShares(db, row);
                FinansTakipEndpoints.ApplyCardCharge(db, cardId, new KartHarcamaYaz(Guid.NewGuid(), track.Surum, row.Tarih, row.Aciklama.Trim(),
                    refund ? -row.Tutar : row.Tutar, 1, null, shares, row.KaynakHarcamaId));
                var charge = db.TakipHarcamalar.Where(h => h.KrediKartiId == cardId).OrderByDescending(h => h.Id).First();
                result.KartHarcamaId = charge.Id; result.DagilimJson = Json(Adlandir(db, Read<KanalPayYaz>(charge.DagilimJson)));
            }
            track.Surum++;
        }
        db.EkstreKayitlar.Add(result); db.SaveChanges(); Sync(db); return result;
    }

    private static KartOdemeOnizlemeDto PaymentEffect(KasaDbContext db, EkstreKayitEntity row)
    {
        var payment = db.TakipKartOdemeler.Single(p => p.Id == row.KartOdemeId);
        return OdemeEtkisi(db, payment.KrediKartiId, Read<KartTaksitPayi>(payment.PaylarJson), payment.Id);
    }
    private static void RefreshPaymentSnapshots(KasaDbContext db, IEnumerable<EkstreKayitEntity> rows)
    {
        foreach (var row in rows.Where(r => r.KartOdemeId != null)) row.DagilimJson = Json(PaymentEffect(db, row).Dagilimlar);
        db.SaveChanges();
    }

    private static IEnumerable<string> Duplicates(KasaDbContext db, EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        var duplicate = false;
        if (row.IslemTuru is "KartHarcama" or "KartIade")
        {
            var card = CardId(doc, row); var amount = row.IslemTuru == "KartIade" ? -row.Tutar : row.Tutar;
            duplicate = db.TakipHarcamalar.Any(h => h.KrediKartiId == card && !h.Iptal && h.Tarih == row.Tarih && h.Tutar == amount);
        }
        else if (row.IslemTuru == "KartOdemesi")
        {
            var card = CardId(doc, row);
            duplicate = db.TakipKartOdemeler.Any(p => p.KrediKartiId == card && !p.Iptal && p.Tarih == row.Tarih && p.Tutar == row.Tutar)
                || db.KartOdemeler.Any(p => p.KrediKartiId == card && p.Tarih == row.Tarih && p.Tutar == row.Tutar)
                || db.Islemler.Any(i => i.KrediKartiId == null && i.Tarih == row.Tarih && i.TutarTl == row.Tutar);
        }
        else if (row.IslemTuru == "Gider")
        {
            duplicate = db.Islemler.Any(i => i.Tarih == row.Tarih && i.TutarTl == row.Tutar && i.KrediKartiId == null)
                || db.TakipKartOdemeler.Any(p => !p.Iptal && p.Tarih == row.Tarih && p.Tutar == row.Tutar)
                || db.KartOdemeler.Any(p => p.Tarih == row.Tarih && p.Tutar == row.Tutar)
                || db.TakipKrediTaksitler.Any(t => !t.Iptal && t.Tarih == row.Tarih && t.Tutar == row.Tutar);
            if (!duplicate)
            {
                var managed = db.TakipKrediler.AsNoTracking().Select(k => k.KrediId).ToHashSet();
                duplicate = db.Krediler.AsNoTracking().Include(k => k.KanalKaydi).Where(k => !k.GerceklesmeTakibi).AsEnumerable().Where(k => !managed.Contains(k.Id))
                    .Any(k => KrediTuretici.TaksitGiderleri(k.ToCore()).Any(t => t.Tarih == row.Tarih && t.TutarTl == row.Tutar));
            }
            if (row.Aciklama.Contains("KREDİ", StringComparison.OrdinalIgnoreCase) || row.Aciklama.Contains("KREDI", StringComparison.OrdinalIgnoreCase))
                yield return "Kredi/kart ödemesi olabilir. Otomatik taksit veya mevcut kart ödemesini ikinci kez gider yazmayın.";
        }
        else
        {
            duplicate = db.EkstreKayitlar.Any(k => k.IslemTuru == "Gelir" && !k.Iptal && k.Tarih == row.Tarih && k.Tutar == row.Tutar)
                || db.Krediler.Any(k => k.CekimTarihi == row.Tarih && k.CekilenTutar == row.Tutar)
                || db.HesapHareketler.Any(h => h.Tarih == row.Tarih && h.Tutar == row.Tutar)
                || db.Gelenler.AsNoTracking().AsEnumerable().Any(g => g.DonemStart <= row.Tarih && g.DonemStart.AddDays(7) > row.Tarih && g.TutarTl == row.Tutar);
        }
        if (duplicate) yield return "Aynı tarihte ve tutarda mevcut/az önce seçilen bir kayıt var. Ayrı hareket olduğundan emin olun.";
    }

    internal static EkstreBelgeDto Document(KasaDbContext db, int id)
    {
        var d = GetDocument(db, id);
        var rows = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == id).OrderBy(k => k.Id).ToList();
        var channelNames = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        var payments = rows.Where(k => k.KartOdemeId != null && !k.Iptal).Select(k => k.KrediKartiId!.Value).Distinct()
            .SelectMany(card => Kart(db, card).Odemeler).ToDictionary(p => p.Id);
        return new(d.Id, d.Surum, d.Kaynak, d.Banka, d.HesapAdi, d.KartId, d.DosyaAdi, DateTimeOffset.FromUnixTimeMilliseconds(d.Yuklendi),
            Read<string>(d.UyarilarJson), Read<EkstreOkunanSatir>(d.SatirlarJson), rows
                .Select(k => new EkstreKayitDto(k.Id, k.SatirNo, k.Tarih, k.Aciklama, k.Tutar, k.IslemTuru, k.DagilimTuru,
                    k.KartOdemeId is { } payment && payments.TryGetValue(payment, out var current) ? current.Dagilimlar : Read<TakipKanalPayi>(k.DagilimJson)
                        .Select(p => p.KanalId is { } channel ? p with { Kanal = channelNames.GetValueOrDefault(channel, p.Kanal) } : p).ToList(),
                    k.KrediKartiId, k.IslemId, k.KartHarcamaId, k.KartOdemeId, k.Iptal)).ToList());
    }
    private static EkstreBelgeEntity GetDocument(KasaDbContext db, int id)
    {
        var d = db.EkstreBelgeler.SingleOrDefault(d => d.Id == id); Require(d is not null, "Belge bulunamadı.", 404); return d;
    }
    private static IResult Safe(Func<IResult> run) => FinansTakipEndpoints.Safe(() => { try { return run(); } catch (ImportException e) { return Error(e.Message, e.Status); } });
    private static IResult Error(string message, int status = 400) => Results.Json(new { hata = message }, statusCode: status);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string message, int status = 400) { if (!valid) throw new ImportException(message, status); }
    private sealed class ImportException(string message, int status) : Exception(message) { public int Status { get; } = status; }
}
