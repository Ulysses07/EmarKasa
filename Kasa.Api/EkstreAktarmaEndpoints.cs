using System.Security.Cryptography;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static class EkstreAktarmaEndpoints
{
    private const int FileLimit = 10 * 1024 * 1024;
    /// <summary>Desteklenen bankaların tek kaynağı: yükleme yalnız bu kodları kabul eder; web ve masaüstü seçim listesini ve görünen
    /// adları GET /api/ekstre-aktar/bankalar'dan alır (istemcilerde kopya ya da yedek liste yoktur).</summary>
    public static readonly IReadOnlyList<EkstreBankaDto> Bankalar =
    [
        new("Vakifbank", "VakıfBank"), new("Akbank", "Akbank"), new("QNB", "QNB"), new("Isbank", "İş Bankası"), new("Garanti", "Garanti BBVA"),
        new("Denizbank", "DenizBank"),
    ];

    public static WebApplication MapEkstreAktarmaEndpoints(this WebApplication app)
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
        api.MapGet("/{id:int}/dosya", (int id, KasaDbContext db, BelgeDeposu depo, HttpResponse response, ILoggerFactory loglar) =>
        {
            var d = db.EkstreBelgeler.AsNoTracking().SingleOrDefault(d => d.Id == id);
            if (d is null)
                return Results.NotFound();
            Stream akis;
            try
            { akis = depo.Ac(d.DosyaOzeti); }
            catch (BelgeDosyasiYokException)
            {
                loglar.CreateLogger("Kasa.Api.EkstreImportEndpoints").LogError("Ekstre belgesi {Id} dosyası belge deposunda yok ({Ozet}, {Depo}).", id, d.DosyaOzeti, depo.Kok);
                return Results.NotFound(new { hata = BelgeEndpoints.DosyaYok });
            }
            response.Headers.CacheControl = "private, no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(akis, "application/pdf", d.DosyaAdi);
        });
        // Önizleme bir benzetimdir: satırlar (ve tarihe bağlı türetme, Sync) geri alınan kayıt noktasında uygulanır, kalıcı
        // hiçbir şey yazılmaz. Benzetim yazarak hesaplandığından yazma transaction'ı içinde çalışır.
        api.MapPost("/{id:int}/onizleme", (int id, EkstreKaydetYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
            Results.Ok(Preview(db, id, dto)))));
        // Eşleşme adayları salt okunur: tutarlı anlık görüntüde çalışır, yazma kilidi almaz ve Sync yapmaz. Tutar URL'ye girmesin diye POST.
        api.MapPost("/{id:int}/eslesme-adaylari", (int id, EkstreEslesmeAdayiSorgu dto, KasaDbContext db) => Safe(() => AlisEndpoints.Oku(db, () =>
        {
            var document = GetDocument(db, id);
            Require(dto is not null && dto.Tarih != default, "Satırın tarihini girin.");
            Require(dto.Tutar > 0 && dto.Tutar <= 999_999_999_999.99m && decimal.Round(dto.Tutar, 2) == dto.Tutar, "Tutar pozitif ve kuruş hassasiyetinde olmalı.");
            return Results.Ok(EslesmeAdaylari(db, document, dto.Tarih, dto.Tutar));
        })));
        api.MapPost("/{id:int}/kaydet", (int id, EkstreKaydetYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var digest = FinansHesaplari.Ozet(new { id, dto.Satirlar, dto.OnizlemeOzeti, dto.TekrarOnay });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreKaydet", digest, key => Results.Ok(Document(db, key))) is { } old)
                return old;
            // Sıra korunmalı: önce kalıcı Sync, sonra önizleme. Önizleme ucu Sync'i geri alınan kayıt noktasında çalıştırır;
            // kaydet yolunda kalıcı Sync önce çalıştığından ikisi aynı durumdan aynı özeti (kart sürümleri dahil) hesaplar
            // (BenzerKayitCaprazTests.Ekstre_onizlemesi_Sync_dahil_kalici_yazmaz_kaydet_ayni_ozetle_yazar).
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
                FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreKaydet", digest, id);
                db.SaveChanges();
                Sync(db);
            }
            finally { db.EkstreDegisikligi = false; }
            return Results.Ok(Document(db, id));
        })));
        api.MapPost("/{id:int}/kayitlar/{kayitId:int}/iptal", (int id, int kayitId, EkstreIptalYaz dto, KasaDbContext db) => Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            Require(!string.IsNullOrWhiteSpace(dto.Aciklama) && dto.Aciklama.Length <= 2000, "İptal gerekçesi girin (en fazla 2000 karakter).");
            // Gerekçe kullanıcı metnidir: GirdiDogrulama.Metin ile aynı kural (ve aynı konumlu ileti), reddedilir.
            var gecersiz = GirdiDogrulama.GecersizKarakterIletisi(dto.Aciklama);
            Require(gecersiz is null, $"İptal gerekçesi: {gecersiz}");
            var digest = FinansHesaplari.Ozet(new { id, kayitId, Aciklama = dto.Aciklama.Trim() });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "EkstreIptal", digest, key => Results.Ok(Document(db, key))) is { } old)
                return old;
            var document = GetDocument(db, id);
            var row = db.EkstreKayitlar.SingleOrDefault(k => k.Id == kayitId && k.BelgeId == id);
            Require(row is not null, "Ekstre kaydı bulunamadı.", 404);
            Require(!row.Iptal, "Bu satır zaten iptal edilmiş.", 409);
            AyKilidiKurallari.TarihAcik(db, row.Tarih);
            // Kaydı alış ödemesine bağlanmış satırın mali kaydı artık alışındır: önce ödeme alıştan ayrılır (satır kendi kaydına döner).
            if (row.IslemTuru != EkstreIslemTurleri.Eslestir && row.EslesmeTuru is not null && BagliAlis(db, row) is { } alisId)
                throw new ImportException($"Bu satırın kaydı Alış #{alisId} ödemesine bağlandı. Önce ödemeyi alıştan ayırın (ödeme iptali satırı kendi kaydına döndürür), sonra bu satırı iptal edin.", 409);
            db.EkstreDegisikligi = true;
            try
            {
                // Eşleşme satırının sahiplik sütunları boştur: iptali hiçbir kaydı ve kart borcunu değiştirmez, yalnız bağı kaldırır.
                if (row.IslemId is { } expense)
                    db.Islemler.Remove(db.Islemler.Single(i => i.Id == expense));
                if (row.KartHarcamaId is { } charge)
                    FinansTakipEndpoints.CancelCardCharge(db, row.KrediKartiId!.Value, charge, dto.Aciklama);
                if (row.KartOdemeId is { } payment)
                    db.TakipKartOdemeler.Single(p => p.Id == payment).Iptal = true;
                if (row.EslesmeTuru is null && row.KrediKartiId is { } card)
                    db.TakipKartlar.Single(t => t.KrediKartiId == card).Surum++;
                row.Iptal = true;
                row.IptalAciklamasi = dto.Aciklama.Trim();
                document.Surum++;
                FinansHesaplari.IstekKaydet(db, dto.IstekId, "EkstreIptal", digest, id);
                db.SaveChanges();
                Sync(db);
            }
            finally { db.EkstreDegisikligi = false; }
            return Results.Ok(Document(db, id));
        })));
        // Yükleme formunun banka seçenekleri (kod ve görünen ad); veritabanına dokunmaz.
        api.MapGet("/bankalar", () => Bankalar);
        return app;
    }

    private static async Task<IResult> Upload(HttpRequest request, KasaDbContext db, IPdfMetinOkuyucu pdf, TimeProvider saat, BelgeDeposu depo, CancellationToken ct)
    {
        if (request.ContentLength is > FileLimit + 65536)
            return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
        if (!request.HasFormContentType)
            return Error("PDF dosyasını yükleyin.");
        IFormCollection form;
        try
        { form = await request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return Error("Dosya boyutu veya yükleme biçimi geçersiz.", 413); }
        var file = form.Files.GetFile("dosya");
        var source = form["kaynak"].ToString();
        var bank = form["banka"].ToString();
        var account = form["hesapAdi"].ToString().Trim();
        int? card = int.TryParse(form["kartId"], out var parsed) ? parsed : null;
        if (source is not (EkstreKaynaklari.Kart or EkstreKaynaklari.Banka) || !Bankalar.Any(b => b.Kod == bank))
            return Error("Geçerli kaynak ve banka seçin.");
        if (source == EkstreKaynaklari.Banka && (account.Length is < 1 or > 100 || card is not null))
            return Error("Banka hesabına kısa bir ad girin (en fazla 100 karakter).");
        if (source == EkstreKaynaklari.Kart && (card is null || !db.KrediKartlari.Any(k => k.Id == card)))
            return Error("Kart seçin.");
        if (source == EkstreKaynaklari.Kart)
            account = "";
        if (file is null || file.Length is <= 0 or > FileLimit)
            return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            var block = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(block, ct)) > 0)
            {
                if (buffer.Length + count > FileLimit)
                    return Error("PDF dosyası en fazla 10 MB olabilir.", 413);
                await buffer.WriteAsync(block.AsMemory(0, count), ct);
            }
            bytes = buffer.ToArray();
        }
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Error("Geçerli PDF dosyası seçin.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var existing = db.EkstreBelgeler.AsNoTracking().SingleOrDefault(d => d.DosyaOzeti == hash);
        IResult Existing(EkstreBelgeEntity d) => d.Kaynak == source && d.Banka == bank && d.HesapAdi == account && d.KartId == card
            ? Results.Ok(Document(db, d.Id)) : Error("Bu PDF farklı kaynak bilgileriyle yüklenmiş. İlk belgeden devam edin.", 409);
        if (existing is not null)
            return Existing(existing);
        EkstreOkumaSonucu read;
        try
        { read = EkstreMetinOkuyucu.Oku(await pdf.OkuAsync(bytes, ct), source, bank); }
        catch (PdfOkumaException e) { return Error(e.Message, e.StatusCode); }
        if (read.Satirlar.Count > 1500)
            return Error("PDF en fazla 1500 hareket içerebilir.", 422);
        var rows = Json(read.Satirlar);
        var warnings = Json(read.Uyarilar);
        if (rows.Length > 4_000_000 || warnings.Length > 100_000)
            return Error("PDF metni çok uzun; daha kısa tarih aralığı seçin.", 422);
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).Take(180).ToArray());
        if (string.IsNullOrWhiteSpace(name))
            name = "ekstre.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            name += ".pdf";
        // PDF, yazma kilidi alınmadan belge deposuna yazılır (diske işlenmiş); satır ondan sonra eklenir. Satır kaydedilemezse dosya
        // hiçbir kaydın göstermediği dosya olarak bakımda silinir.
        if (depo.Yaz(bytes, ct).Ozet != hash)
            return Error("PDF belge deposuna doğrulanarak yazılamadı. Yeniden deneyin.", 500);
        return Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var old = db.EkstreBelgeler.SingleOrDefault(d => d.DosyaOzeti == hash);
            if (old is not null)
                return Existing(old);
            var d = new EkstreBelgeEntity
            {
                Kaynak = source,
                Banka = bank,
                HesapAdi = account,
                KartId = card,
                DosyaAdi = name,
                DosyaOzeti = hash,
                Yuklendi = saat.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = rows,
                UyarilarJson = warnings
            };
            db.EkstreBelgeler.Add(d);
            db.SaveChanges();
            return Results.Ok(Document(db, d.Id));
        }));
    }

    private sealed record KartSurumu(int KrediKartiId, int Surum);
    private static EkstreOnizlemeDto Preview(KasaDbContext db, int id, EkstreKaydetYaz dto)
    {
        List<KartSurumu> cardVersions = [];
        DateOnly? locked = null;
        var document = GetDocument(db, id);
        Require(document.Surum == dto.Surum, "Belge değişmiş. Yenileyip yeniden önizleyin.", 409);
        Require(dto.Satirlar is { Count: > 0 and <= 1500 } && dto.Satirlar.All(r => r is not null), "İşlenecek satırları seçin.");
        Require(dto.Satirlar.Select(r => r.SatirNo).Distinct().Count() == dto.Satirlar.Count, "Bir kaynak satırı iki kez seçilemez.");
        var sourceRows = Read<EkstreOkunanSatir>(document.SatirlarJson).ToDictionary(r => r.No);
        var output = new List<EkstreSatirOnizleme>();
        var warnings = new List<string>();
        bool confirm = false;
        var selected = new List<(EkstreSatirYaz Row, EkstreKayitEntity Applied, List<string> Notices)>();
        // Bu önizlemede az önce uygulanan satırlar (EkstreKayit kimliği → kaynak satır no): benzer kayıt uyarısında adlandırılır.
        var batch = new Dictionary<int, int>();
        var tx = db.Database.CurrentTransaction!;
        tx.CreateSavepoint("ekstre_preview");
        try
        {
            // Tarihe bağlı türetme de benzetimin parçasıdır ve geri alınır; kaydet yolunda Sync kalıcı olarak önce çalıştığından
            // burada etkisizdir. Özetin okumaları iki yolda da Sync sonrasındaki aynı duruma göredir.
            Sync(db);
            var oldPayments = db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal).ToList()
                .Where(p => Read<KartTaksitPayi>(p.PaylarJson).Any(a => a.TaksitId == 0 && a.Tutar > 0))
                .ToDictionary(p => p.Id, p => Json(OdemeEtkisi(db, p.KrediKartiId, Read<KartTaksitPayi>(p.PaylarJson), p.Id).Dagilimlar));
            cardVersions = db.TakipKartlar.OrderBy(c => c.KrediKartiId).Select(c => new KartSurumu(c.KrediKartiId, c.Surum)).ToList();
            locked = db.AyKilidi.AsNoTracking().Single().KilitliSonTarih;
            var similar = new BenzerKayitServisi(db);
            // Bu belgenin önceden kaydedilmiş ve bu önizlemede uygulanan satırları (EkstreKayit kimlikleri): benzerlikte yalnız aynı günde sayılır.
            var sameDocument = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == id).Select(k => k.Id).ToHashSet();
            db.EkstreDegisikligi = true;
            foreach (var row in dto.Satirlar)
            {
                Require(sourceRows.TryGetValue(row.SatirNo, out var original), "Kaynak satır bu PDF'de bulunamadı.");
                Validate(db, document, row, original);
                var notices = original.Uyarilar.ToList();
                if (original.ParaBirimi == "Belirsiz")
                    notices.Add("Para birimi okunamadı. Bu satırı TL olarak kaydettiğinizi doğrulayın.");
                if (original.Yon == "Belirsiz")
                    notices.Add("Giriş/çıkış yönü okunamadı; seçtiğiniz işlem türünü doğrulayın.");
                // Eşleştirme kasa ve kart borcunu değiştirmez, yeni kayıt da üretmez: tür/transfer/benzer kayıt uyarıları ona ait değildir.
                if (row.IslemTuru != EkstreIslemTurleri.Eslestir)
                {
                    if (original.OnerilenIslem != row.IslemTuru)
                        notices.Add("İşlem türü PDF önerisinden farklı; yönünü ve kasaya etkisini kontrol edin.");
                    if (document.Kaynak == EkstreKaynaklari.Banka && original.Sinif == "Transfer")
                        notices.Add("Kendi hesaplarınız arasındaki transfer genel kasayı değiştirmez; böyle bir satırı seçmeden bırakın. Gelir/gider olarak işlerseniz genel kasa değişir.");
                    notices.AddRange(Duplicates(db, similar, document, row, batch, sameDocument));
                }
                var applied = Apply(db, document, row);
                batch[applied.Id] = row.SatirNo;
                sameDocument.Add(applied.Id);
                selected.Add((row, applied, notices));
            }
            // A later selected charge may allocate an earlier payment's advance.
            // Present the final batch state, not the intermediate row order.
            RefreshPaymentSnapshots(db, selected.Select(s => s.Applied));
            foreach (var (row, applied, notices) in selected)
            {
                var cash = row.IslemTuru switch { EkstreIslemTurleri.Gelir => row.Tutar, EkstreIslemTurleri.Gider => -row.Tutar, EkstreIslemTurleri.KartOdemesi => -PaymentEffect(db, applied).KasaEtkisi, _ => 0m };
                var distinct = notices.Distinct().ToList();
                confirm |= distinct.Count > 0;
                output.Add(new(row.SatirNo, row.Tarih, Aciklama(row), row.Tutar, row.IslemTuru, cash, Read<TakipKanalPayi>(applied.DagilimJson), distinct));
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
            tx.RollbackToSavepoint("ekstre_preview");
            tx.ReleaseSavepoint("ekstre_preview");
            db.ChangeTracker.Clear();
            db.EkstreDegisikligi = false;
        }
        if (confirm)
            warnings.Add("İşaretli satırların yönünü, tutarını ve benzer kayıtlarını kontrol edin; ayrı kayıt olarak işleneceğini ayrıca onaylayın.");
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
        Require(Aciklama(row).Length > 0 && row.Aciklama.Length <= 2000, "Açıklama 1–2000 karakter olmalı.");
        Require(doc.Kaynak == EkstreKaynaklari.Banka ? row.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.Eslestir : row.IslemTuru is EkstreIslemTurleri.KartHarcama or EkstreIslemTurleri.KartIade or EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.Eslestir, "Bu belge kaynağı için geçerli işlem türü seçin.");
        Require(row.Dagilimlar is not null && row.Dagilimlar.All(p => p is not null), "Kanal dağılımı geçersiz.");
        if (row.IslemTuru == EkstreIslemTurleri.Eslestir)
        { ValidateMatch(db, doc, row); return; }
        Require(row.EslesenKayitTuru is null && row.EslesenKayitId is null, "Mevcut kayıt yalnız 'Mevcut kayıtla eşleştir' türünde seçilir.");
        if (row.IslemTuru is EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.KartIade)
            Require(row.DagilimTuru == DagilimBicimleri.Otomatik && row.Dagilimlar.Count == 0, "Kart ödemesi/iadesi kaynak borçtan otomatik dağılır.");
        else
            ResolveShares(db, row);
        Require(row.IslemTuru == EkstreIslemTurleri.KartIade || row.KaynakHarcamaId is null, "Kaynak harcama yalnız kart iadesinde seçilir.");
        if (row.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider)
            Require(row.KrediKartiId is null, "Banka gelir/giderinde kart seçilemez.");
        else
        {
            var card = CardId(doc, row);
            var tracking = FinansTakipEndpoints.ManagedCard(db, card);
            Require(tracking.Aktif || row.IslemTuru == EkstreIslemTurleri.KartOdemesi, "Kart yeni harekete kapalı.", 409);
            Require(row.Tarih >= tracking.Baslangic, "Tarih kart takip başlangıcından önce olamaz.");
        }
    }

    /// <summary>Eşleşme adayı ucunun döndürdüğü en çok kayıt.</summary>
    internal const int EnFazlaAday = 20;
    /// <summary>Taksit satırının, taksidin girdiği ekstrenin kesim tarihinden en çok uzaklığı (gün): banka taksidi harcama tarihiyle
    /// ya da ekstre döneminin bir günüyle basar.</summary>
    internal const int TaksitKesimPenceresi = 35;
    private const string AdayYok = "Eşleştirilecek kayıt bulunamadı ya da iptal edilmiş. Adayları yeniden yükleyin.";

    /// <summary>
    /// 'Eslestir' satırı: yeni kayıt üretmez, yalnız mevcut kayda bağ yazar. Banka belgesi kartsız bir gidere ('Gider') ya da bir kart
    /// ödemesine ('KartOdeme'); kart belgesi yalnız kendi kartının harcamasına ('KartHarcama'), taksitli harcamanın bir taksidine
    /// ('KartTaksidi') ya da ödemesine ('KartOdeme') bağlanır. Tutar aynı, tarih en çok <see cref="BenzerKayitServisi.GunPenceresi"/> gün
    /// farklı olmalıdır (taksitte harcama tarihi ya da taksidin ekstre kesimi ±<see cref="TaksitKesimPenceresi"/> gün). Bir kayıt aynı
    /// belge türünden (banka/kart) yalnız bir eşleştirme satırına bağlanabilir: kart ödemesi hem banka hem kart ekstresinde görünür;
    /// taksitli harcamanın her taksidi ayrı hedeftir.
    /// </summary>
    private static void ValidateMatch(KasaDbContext db, EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        Require(row.DagilimTuru == DagilimBicimleri.Eslesme && row.Dagilimlar.Count == 0 && row.KaynakHarcamaId is null, "Mevcut kayıtla eşleştirmede kanal dağılımı ve iade kaynağı seçilmez.");
        Require(row.KrediKartiId is null || doc.Kaynak == EkstreKaynaklari.Kart && row.KrediKartiId == doc.KartId, "Eşleştirmede kart seçilmez; kart eşleşen kayıttan okunur.");
        Require(row.EslesenKayitTuru is not null && row.EslesenKayitId is > 0, "Eşleştirilecek mevcut kaydı seçin.");
        var tur = row.EslesenKayitTuru!;
        var id = row.EslesenKayitId!.Value;
        bool Yakin(DateOnly t) => Math.Abs(t.DayNumber - row.Tarih.DayNumber) <= BenzerKayitServisi.GunPenceresi;
        const string Uyusmaz = "Seçilen kayıt bu satırla eşleşmiyor: tutarı aynı, tarihi en çok 3 gün farklı olmalı.";
        switch (tur)
        {
            case EslesmeTurleri.Gider when doc.Kaynak == EkstreKaynaklari.Banka:
                var gider = db.Islemler.AsNoTracking().SingleOrDefault(i => i.Id == id);
                Require(gider is not null, AdayYok, 404);
                Require(gider.KrediKartiId is null, "Banka satırı yalnız kartsız giderle eşleşir; kart harcaması kart ekstresinden eşleştirilir.");
                Require(gider.TutarTl == row.Tutar && Yakin(gider.Tarih), Uyusmaz);
                break;
            case EslesmeTurleri.KartOdeme:
                var odeme = db.TakipKartOdemeler.AsNoTracking().SingleOrDefault(p => p.Id == id);
                Require(odeme is { Iptal: false }, AdayYok, 404);
                Require(doc.Kaynak == EkstreKaynaklari.Banka || odeme.KrediKartiId == doc.KartId, "Kart ekstresi yalnız kendi kartının ödemesiyle eşleşir.");
                Require(odeme.Tutar == row.Tutar && Yakin(odeme.Tarih), Uyusmaz);
                break;
            case EslesmeTurleri.KartHarcama when doc.Kaynak == EkstreKaynaklari.Kart:
                var harcama = db.TakipHarcamalar.AsNoTracking().SingleOrDefault(h => h.Id == id && h.KrediKartiId == doc.KartId);
                Require(harcama is { Iptal: false }, AdayYok, 404);
                Require(harcama.Tutar == row.Tutar && Yakin(harcama.Tarih), Uyusmaz);
                break;
            case EslesmeTurleri.KartTaksidi when doc.Kaynak == EkstreKaynaklari.Kart:
                var taksit = (from t in db.TakipKartTaksitler.AsNoTracking()
                              join h in db.TakipHarcamalar.AsNoTracking() on t.HarcamaId equals h.Id
                              join e in db.TakipEkstreler.AsNoTracking() on t.EkstreId equals e.Id
                              where t.Id == id && h.KrediKartiId == doc.KartId
                              select new { t.Tutar, h.Iptal, h.TaksitSayisi, h.Tarih, e.KesimTarihi }).SingleOrDefault();
                Require(taksit is { Iptal: false, TaksitSayisi: > 1 }, AdayYok, 404);
                Require(taksit.Tutar == row.Tutar && (Yakin(taksit.Tarih) || Math.Abs(taksit.KesimTarihi.DayNumber - row.Tarih.DayNumber) <= TaksitKesimPenceresi),
                    "Seçilen taksit bu satırla eşleşmiyor: tutarı taksit tutarına eşit, tarihi harcamaya en çok 3, taksidin ekstre kesimine en çok 35 gün uzak olmalı.");
                break;
            default:
                throw new ImportException(doc.Kaynak == EkstreKaynaklari.Banka ? "Banka satırı kartsız bir gider ya da kart ödemesiyle eşleştirilir." : "Kart satırı bu kartın harcaması, taksidi ya da ödemesiyle eşleştirilir.", 400);
        }
        Require(!EslesmisHedefler(db, doc.Kaynak).Contains((tur, id)), "Bu kayıt başka bir ekstre satırıyla eşleşti.", 409);
    }

    /// <summary>Aynı belge türündeki (banka/kart) iptal edilmemiş eşleştirme satırlarının hedefleri. İzlenen sorgu: aynı önizlemede
    /// az önce uygulanan satır da görülür (bir kaydı iki satıra bağlayan paket reddedilir).</summary>
    private static HashSet<(string Tur, int Id)> EslesmisHedefler(KasaDbContext db, string kaynak) =>
        db.EkstreKayitlar.Where(k => !k.Iptal && k.IslemTuru == EkstreIslemTurleri.Eslestir && k.EslesmeTuru != null && k.EslesmeId != null
                && db.EkstreBelgeler.Any(b => b.Id == k.BelgeId && b.Kaynak == kaynak))
            .Select(k => new { k.EslesmeTuru, k.EslesmeId }).ToList().Select(k => (k.EslesmeTuru!, k.EslesmeId!.Value)).ToHashSet();

    /// <summary>
    /// Satırın eşleştirilebileceği mevcut kayıtlar: <see cref="BenzerKayitServisi"/> ile aynı kural (aynı tutar, ±3 gün) ve belge
    /// türüne uygun kaynaklar. Banka: kartsız giderler (elle, alış ödemesi, aylık gider, başka ekstre) ve bütün kartların ödemeleri. Kart:
    /// bu kartın harcamaları (kartlı gider/alış ödemesinden türeyenler dahil; eski kart gideri takip harcaması değildir), taksitli
    /// harcamaların tutarı eşit taksitleri (<see cref="TaksitKesimPenceresi"/>) ve ödemeleri. Başka bir eşleştirme satırına bağlı kayıt
    /// aday değildir. Tarih farkına göre en çok <see cref="EnFazlaAday"/> kayıt.
    /// </summary>
    private static List<EkstreEslesmeAdayiDto> EslesmeAdaylari(KasaDbContext db, EkstreBelgeEntity doc, DateOnly tarih, decimal tutar)
    {
        var eslesmis = EslesmisHedefler(db, doc.Kaynak);
        var servis = new BenzerKayitServisi(db);
        var adaylar = new List<(EkstreEslesmeAdayiDto Aday, int Fark, int Sira)>();
        int Fark(DateOnly t) => Math.Abs(t.DayNumber - tarih.DayNumber);
        void Ekle(string tur, int id, BenzerKayitDto k, int sira, int? kart = null)
        {
            if (!eslesmis.Contains((tur, id)))
                adaylar.Add((new(tur, id, k.Tarih, k.Tutar, k.Aciklama, kart ?? k.KrediKartiId, k.KanalEtiketi, k.AlisId, k.EkstreKayitId), Fark(k.Tarih), sira));
        }
        if (doc.Kaynak == EkstreKaynaklari.Banka)
        {
            foreach (var k in servis.Bul(new BenzerAramasi(BenzerAramaTurleri.Gider, tarih, tutar), k => k.Kaynak is BenzerKayitKaynaklari.Islem or BenzerKayitKaynaklari.KartOdeme, int.MaxValue))
                Ekle(k.Kaynak == BenzerKayitKaynaklari.Islem ? EslesmeTurleri.Gider : EslesmeTurleri.KartOdeme, k.Id, k, k.Kaynak == BenzerKayitKaynaklari.Islem ? 0 : 1);
        }
        else
        {
            var kart = doc.KartId!.Value;
            var harcamalar = servis.Bul(new BenzerAramasi(BenzerAramaTurleri.KartHarcama, tarih, tutar, kart), null, int.MaxValue);
            var giderIds = harcamalar.Where(k => k.Kaynak == BenzerKayitKaynaklari.Islem).Select(k => k.Id).ToArray();
            var gidereBagli = db.TakipHarcamalar.AsNoTracking().Where(h => h.IslemId != null && giderIds.Contains(h.IslemId.Value) && !h.Iptal)
                .ToDictionary(h => h.IslemId!.Value, h => h.Id);
            foreach (var k in harcamalar)
                if (k.Kaynak == BenzerKayitKaynaklari.KartHarcama)
                    Ekle(EslesmeTurleri.KartHarcama, k.Id, k, 0);
                else if (gidereBagli.TryGetValue(k.Id, out var harcama))
                    Ekle(EslesmeTurleri.KartHarcama, harcama, k, 0, kart);
            foreach (var t in TaksitAdaylari(db, kart, tarih, tutar).Where(t => !eslesmis.Contains((EslesmeTurleri.KartTaksidi, t.Id))))
                adaylar.Add((new(EslesmeTurleri.KartTaksidi, t.Id, t.KesimTarihi, tutar, $"{t.Aciklama} · {t.HarcamaTarihi:dd.MM.yyyy} harcaması · {t.No}/{t.TaksitSayisi}. taksit", kart,
                    HarcamaId: t.HarcamaId, TaksitNo: t.No, TaksitSayisi: t.TaksitSayisi), Math.Min(Fark(t.HarcamaTarihi), Fark(t.KesimTarihi)), 1));
            foreach (var k in servis.Bul(new BenzerAramasi(BenzerAramaTurleri.KartOdeme, tarih, tutar, kart), k => k.Kaynak == BenzerKayitKaynaklari.KartOdeme && k.KrediKartiId == kart, int.MaxValue))
                Ekle(EslesmeTurleri.KartOdeme, k.Id, k, 2);
        }
        return adaylar.OrderBy(a => a.Fark).ThenBy(a => a.Sira).ThenByDescending(a => a.Aday.Id).Take(EnFazlaAday).Select(a => a.Aday).ToList();
    }

    /// <summary>Taksit adayı: taksit ve harcama kimliği, harcamanın açıklaması, tarihi ve taksit sayısı, taksidin ekstre kesimi ve sırası.</summary>
    private sealed record TaksitAdayi(int Id, int HarcamaId, string Aciklama, DateOnly HarcamaTarihi, int TaksitSayisi, DateOnly KesimTarihi, int No);

    /// <summary>Kartın iptal edilmemiş taksitli (taksit sayısı &gt; 1) harcamalarının tutarı <paramref name="tutar"/>'a eşit taksitleri:
    /// harcama tarihi ±<see cref="BenzerKayitServisi.GunPenceresi"/> ya da taksidin ekstre kesimi ±<see cref="TaksitKesimPenceresi"/>
    /// gün. Eşleşme adayı ucu ve kart harcaması satırının benzer kayıt uyarısı (gap-coklu-giris-cift-sayim-mutabakat-6) aynı kuralı kullanır.</summary>
    private static List<TaksitAdayi> TaksitAdaylari(KasaDbContext db, int kart, DateOnly tarih, decimal tutar)
    {
        int Fark(DateOnly t) => Math.Abs(t.DayNumber - tarih.DayNumber);
        var taksitler = (from t in db.TakipKartTaksitler.AsNoTracking()
                         join h in db.TakipHarcamalar.AsNoTracking() on t.HarcamaId equals h.Id
                         join e in db.TakipEkstreler.AsNoTracking() on t.EkstreId equals e.Id
                         where h.KrediKartiId == kart && !h.Iptal && h.TaksitSayisi > 1 && t.Tutar == tutar
                         select new { t.Id, t.HarcamaId, h.Aciklama, HarcamaTarihi = h.Tarih, h.TaksitSayisi, e.KesimTarihi }).ToList()
            .Where(t => Fark(t.HarcamaTarihi) <= BenzerKayitServisi.GunPenceresi || Fark(t.KesimTarihi) <= TaksitKesimPenceresi).ToList();
        if (taksitler.Count == 0)
            return [];
        var taksitHarcamalari = taksitler.Select(t => t.HarcamaId).Distinct().ToArray();
        var siralar = db.TakipKartTaksitler.AsNoTracking().Where(t => taksitHarcamalari.Contains(t.HarcamaId)).Select(t => new { t.Id, t.HarcamaId }).ToList()
            .GroupBy(t => t.HarcamaId).SelectMany(g => g.OrderBy(t => t.Id).Select((t, i) => (t.Id, No: i + 1))).ToDictionary(x => x.Id, x => x.No);
        return taksitler.Select(t => new TaksitAdayi(t.Id, t.HarcamaId, t.Aciklama, t.HarcamaTarihi, t.TaksitSayisi, t.KesimTarihi, siralar[t.Id])).ToList();
    }

    /// <summary>Kaydı alış ödemesine bağlanmış (sahipliği eşleşmeye dönmüş) satırın alışı; bağ yoksa null.</summary>
    internal static int? BagliAlis(KasaDbContext db, EkstreKayitEntity row) => row switch
    {
        { EslesmeTuru: EslesmeTurleri.Gider, EslesmeId: { } islem } => db.AlisOdemeler.Where(o => o.IslemId == islem).Select(o => (int?)o.AlisId).FirstOrDefault(),
        { EslesmeTuru: EslesmeTurleri.KartHarcama, EslesmeId: { } harcama } => db.AlisOdemeler
            .Where(o => db.TakipHarcamalar.Any(h => h.Id == harcama && h.IslemId == o.IslemId)).Select(o => (int?)o.AlisId).FirstOrDefault(),
        _ => null
    };

    private static List<KanalPayYaz> ResolveShares(KasaDbContext db, EkstreSatirYaz row)
    {
        if (row.DagilimTuru == DagilimBicimleri.Genel)
        {
            Require(row.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider && row.Dagilimlar.Count == 0, "Yalnız banka gelir/gideri genel kasaya yazılabilir.");
            return [];
        }
        Require(row.DagilimTuru is DagilimBicimleri.Esit or DagilimBicimleri.Ozel && row.Dagilimlar.Count > 0, "Dağıtılacak kanalları seçin.");
        var ids = row.Dagilimlar.Select(p => p.KanalId).ToList();
        Require(ids.Distinct().Count() == ids.Count && db.Kanallar.Count(k => ids.Contains(k.Id)) == ids.Count, "Kayıtlı kanalları birer kez seçin.");
        if (row.DagilimTuru == DagilimBicimleri.Esit)
        { Require(row.Dagilimlar.All(p => p.Tutar == 0), "Eşit dağılımda kanal tutarı sıfır gönderilmeli."); return EsitPaylar(ids, row.Tutar); }
        Require(row.Dagilimlar.All(p => p.Tutar > 0 && p.Tutar <= row.Tutar && decimal.Round(p.Tutar, 2) == p.Tutar) && row.Dagilimlar.Sum(p => p.Tutar) == row.Tutar, "Kanal tutarları pozitif olmalı ve hareket tutarına eşitlenmeli.");
        return row.Dagilimlar.OrderBy(p => p.KanalId).ToList();
    }

    private static int CardId(EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        Require(doc.Kaynak != EkstreKaynaklari.Kart || row.KrediKartiId is null || row.KrediKartiId == doc.KartId, "Kart belgesinin kartı değiştirilemez.");
        var card = doc.Kaynak == EkstreKaynaklari.Kart ? doc.KartId : row.KrediKartiId;
        Require(card is > 0, "Ödemenin kartını seçin.");
        return card.Value;
    }

    private static EkstreKayitEntity Apply(KasaDbContext db, EkstreBelgeEntity doc, EkstreSatirYaz row)
    {
        var aciklama = Aciklama(row);
        var result = new EkstreKayitEntity
        {
            BelgeId = doc.Id,
            SatirNo = row.SatirNo,
            Tarih = row.Tarih,
            Aciklama = aciklama,
            Tutar = row.Tutar,
            IslemTuru = row.IslemTuru,
            DagilimTuru = row.DagilimTuru
        };
        if (row.IslemTuru == EkstreIslemTurleri.Eslestir)
        {
            // Yalnız bağ: yeni gider, harcama ya da ödeme yok; kart sürümü ve Sync değişmez.
            result.EslesmeTuru = row.EslesenKayitTuru;
            result.EslesmeId = row.EslesenKayitId;
            if (doc.Kaynak == EkstreKaynaklari.Kart)
                result.KrediKartiId = doc.KartId;
            db.EkstreKayitlar.Add(result);
            db.SaveChanges();
            return result;
        }
        if (row.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider)
        {
            var shares = Adlandir(db, ResolveShares(db, row));
            result.DagilimJson = Json(shares);
            if (row.IslemTuru == EkstreIslemTurleri.Gider)
            {
                var expense = new IslemEntity
                {
                    Tarih = row.Tarih,
                    Cari = aciklama,
                    TutarTl = row.Tutar,
                    Kanal = shares.Count == 1 ? shares[0].Kanal : KanalEtiketleri.GenelKasa,
                    KanalId = shares.Count == 1 ? shares[0].KanalId : null,
                    Tip = GiderTipi.Cari,
                    Not = "PDF hesap hareketi · " + doc.Banka + " · " + doc.HesapAdi
                };
                db.Islemler.Add(expense);
                db.SaveChanges();
                result.IslemId = expense.Id;
            }
        }
        else
        {
            var cardId = CardId(doc, row);
            result.KrediKartiId = cardId;
            var track = FinansTakipEndpoints.ManagedCard(db, cardId);
            if (row.IslemTuru == EkstreIslemTurleri.KartOdemesi)
            {
                var paymentDto = new KartTakipOdemeYaz(Guid.NewGuid(), track.Surum, row.Tarih, row.Tutar, Not: aciklama);
                FinansTakipEndpoints.ValidatePayment(db, cardId, paymentDto);
                var allocations = OdemePaylari(db, cardId, row.Tutar, null);
                result.DagilimJson = Json(OdemeEtkisi(db, cardId, allocations).Dagilimlar);
                var payment = new TakipKartOdemeEntity { KrediKartiId = cardId, Tarih = row.Tarih, Tutar = row.Tutar, Not = aciklama, PaylarJson = Json(allocations) };
                db.TakipKartOdemeler.Add(payment);
                db.SaveChanges();
                result.KartOdemeId = payment.Id;
            }
            else
            {
                var refund = row.IslemTuru == EkstreIslemTurleri.KartIade;
                var shares = refund ? new List<KanalPayYaz>() : ResolveShares(db, row);
                FinansTakipEndpoints.ApplyCardCharge(db, cardId, new KartHarcamaYaz(Guid.NewGuid(), track.Surum, row.Tarih, aciklama,
                    refund ? -row.Tutar : row.Tutar, 1, null, shares, row.KaynakHarcamaId));
                var charge = db.TakipHarcamalar.Where(h => h.KrediKartiId == cardId).OrderByDescending(h => h.Id).First();
                result.KartHarcamaId = charge.Id;
                result.DagilimJson = Json(Adlandir(db, Read<KanalPayYaz>(charge.DagilimJson)));
            }
            track.Surum++;
        }
        db.EkstreKayitlar.Add(result);
        db.SaveChanges();
        Sync(db);
        return result;
    }

    private static KartOdemeOnizlemeDto PaymentEffect(KasaDbContext db, EkstreKayitEntity row)
    {
        var payment = db.TakipKartOdemeler.Single(p => p.Id == row.KartOdemeId);
        return OdemeEtkisi(db, payment.KrediKartiId, Read<KartTaksitPayi>(payment.PaylarJson), payment.Id);
    }
    private static void RefreshPaymentSnapshots(KasaDbContext db, IEnumerable<EkstreKayitEntity> rows)
    {
        foreach (var row in rows.Where(r => r.KartOdemeId != null))
            row.DagilimJson = Json(PaymentEffect(db, row).Dagilimlar);
        db.SaveChanges();
    }

    /// <summary>Benzer kayıt uyarıları. Kart harcaması/iadesi, kart ödemesi ve banka gideri benzerlik ucuyla aynı servisi
    /// (<see cref="BenzerKayitServisi"/>: aynı tutar, ±3 gün, simetrik kaynaklar) kullanır; banka gideri kanal ayırmaz.
    /// Eşleşmeler satır başına tek uyarıda toplanır. ±3 gün penceresi başka yoldan girilmiş aynı paranın (valör farkı) içindir;
    /// aynı belgenin satırları ayrı banka hareketleri olduğundan (günlük sabit masraf gibi) yalnız aynı günde benzer sayılır.
    /// Bu önizlemede önceden uygulanan satırlar satır numarasıyla adlandırılır.</summary>
    private static IEnumerable<string> Duplicates(KasaDbContext db, BenzerKayitServisi similar, EkstreBelgeEntity doc, EkstreSatirYaz row, IReadOnlyDictionary<int, int> batch, IReadOnlySet<int> sameDocument)
    {
        if (row.IslemTuru == EkstreIslemTurleri.Gelir)
        {
            // Gider tarafıyla aynı pencere (±3 gün, valör farkı); aynı belgenin satırları yalnız aynı günde benzer sayılır.
            var first = row.Tarih.AddDays(-BenzerKayitServisi.GunPenceresi);
            var last = row.Tarih.AddDays(BenzerKayitServisi.GunPenceresi);
            var duplicate = db.EkstreKayitlar.Where(k => k.IslemTuru == EkstreIslemTurleri.Gelir && !k.Iptal && k.Tutar == row.Tutar && k.Tarih >= first && k.Tarih <= last)
                    .Select(k => new { k.Id, k.Tarih }).AsEnumerable().Any(k => !sameDocument.Contains(k.Id) || k.Tarih == row.Tarih)
                || db.Krediler.Any(k => k.CekimTarihi >= first && k.CekimTarihi <= last && k.CekilenTutar == row.Tutar)
                || db.HesapHareketler.Any(h => h.Tarih >= first && h.Tarih <= last && h.Tutar == row.Tutar)
                || db.Gelenler.AsNoTracking().AsEnumerable().Any(g => g.DonemStart <= row.Tarih && g.DonemStart.AddDays(7) > row.Tarih && g.TutarTl == row.Tutar);
            if (duplicate)
                yield return "Aynı tutarda, en çok 3 gün farklı tarihte mevcut/az önce seçilen bir kayıt var. Ayrı hareket olduğundan emin olun.";
            yield break;
        }
        var search = row.IslemTuru switch
        {
            EkstreIslemTurleri.KartHarcama or EkstreIslemTurleri.KartIade => new BenzerAramasi(BenzerAramaTurleri.KartHarcama, row.Tarih, row.IslemTuru == EkstreIslemTurleri.KartIade ? -row.Tutar : row.Tutar, CardId(doc, row)),
            EkstreIslemTurleri.KartOdemesi => new BenzerAramasi(BenzerAramaTurleri.KartOdeme, row.Tarih, row.Tutar, CardId(doc, row)),
            _ => new BenzerAramasi(BenzerAramaTurleri.Gider, row.Tarih, row.Tutar)
        };
        if (row.IslemTuru == EkstreIslemTurleri.Gider && (row.Aciklama.Contains("KREDİ", StringComparison.OrdinalIgnoreCase) || row.Aciklama.Contains("KREDI", StringComparison.OrdinalIgnoreCase)))
            yield return "Kredi/kart ödemesi olabilir. Otomatik taksit veya mevcut kart ödemesini ikinci kez gider yazmayın.";
        if (row.IslemTuru == EkstreIslemTurleri.KartHarcama)
        {
            // gap-coklu-giris-cift-sayim-mutabakat-6: bankanın aylık taksit satırı (tutar = taksit tutarı) taksitli harcamanın zaten kart
            // borcunda olan taksididir; yeni harcama olarak işlenirse borç ikinci kez sayılır. Harcama başına tek uyarı, başka satırla
            // eşleşmemiş ve kesimi satıra en yakın taksidi adlandırır.
            var eslesmis = EslesmisHedefler(db, doc.Kaynak);
            foreach (var t in TaksitAdaylari(db, CardId(doc, row), row.Tarih, row.Tutar).Where(t => !eslesmis.Contains((EslesmeTurleri.KartTaksidi, t.Id)))
                .GroupBy(t => t.HarcamaId).Select(g => g.OrderBy(t => Math.Abs(t.KesimTarihi.DayNumber - row.Tarih.DayNumber)).ThenBy(t => t.No).First()).OrderBy(t => t.HarcamaId))
                yield return $"Bu satır #{t.HarcamaId} taksitli harcamanın {t.No}/{t.TaksitSayisi}. taksidi olabilir; atlayın ya da mevcut kayıtla eşleştirin.";
        }
        var records = similar.Bul(search, k => k.EkstreKayitId is not { } kayit || !sameDocument.Contains(kayit) || k.Tarih == row.Tarih);
        if (records.Count == 0)
            yield break;
        var names = records.Select(k => BenzerKayitServisi.Satir(k, k.EkstreKayitId is { } kayit && batch.TryGetValue(kayit, out var satirNo) ? $"bu önizlemede seçilen {satirNo}. satır" : null)).ToList();
        yield return $"Benzer kayıt: {BenzerKayitServisi.Liste(names)}. Ayrı hareket olduğundan emin olun. Aynı hareketse satırı yeni kayıt olarak işlemeyin; 'Mevcut kayıtla eşleştir' ile bağlayın.";
    }

    // Satır açıklaması PDF'ten okunur (düzenlenmiş olabilir): kullanıcı girdisi olarak reddedilmez, okuyucuyla
    // aynı kuralla temizlenir. Bu sürümden önce yüklenmiş belgelerin temizlenmemiş açıklamaları da böylece kaydedilebilir.
    private static string Aciklama(EkstreSatirYaz row) => GirdiDogrulama.Temizle(row.Aciklama ?? "").Trim();

    internal static EkstreBelgeDto Document(KasaDbContext db, int id)
    {
        var d = GetDocument(db, id);
        var rows = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == id).OrderBy(k => k.Id).ToList();
        var channelNames = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        var payments = rows.Where(k => k.KartOdemeId != null && !k.Iptal).Select(k => k.KrediKartiId!.Value).Distinct()
            .SelectMany(card => Kart(db, card).Odemeler).ToDictionary(p => p.Id);
        // İptal gerekçesi satırda, iptal anı denetim izinde (sürüm öncesi iptalde bilinmez).
        var cancelTimes = DenetimOkuma.IptalAnlari<EkstreKayitEntity>(db, rows.Where(k => k.Iptal).Select(k => k.Id).ToList());
        var matched = MatchStates(db, rows.Where(k => !k.Iptal && k.EslesmeTuru != null).ToList());
        return new(d.Id, d.Surum, d.Kaynak, d.Banka, d.HesapAdi, d.KartId, d.DosyaAdi, DateTimeOffset.FromUnixTimeMilliseconds(d.Yuklendi),
            Read<string>(d.UyarilarJson), Read<EkstreOkunanSatir>(d.SatirlarJson), rows
                // İptal edilmiş ödemenin güncel etkisi yoktur (Kart() boş dağılım döner); geçmiş görünümde
                // satır kaydedildiği andaki payları (DagilimJson anlık görüntüsü) gösterir.
                .Select(k => new EkstreKayitDto(k.Id, k.SatirNo, k.Tarih, k.Aciklama, k.Tutar, k.IslemTuru, k.DagilimTuru,
                    !k.Iptal && k.KartOdemeId is { } payment && payments.TryGetValue(payment, out var current) && !current.Iptal ? current.Dagilimlar : Read<TakipKanalPayi>(k.DagilimJson)
                        .Select(p => p.KanalId is { } channel ? p with { Kanal = channelNames.GetValueOrDefault(channel, p.Kanal) } : p).ToList(),
                    k.KrediKartiId, k.IslemId, k.KartHarcamaId, k.KartOdemeId, k.Iptal,
                    k.Iptal ? k.IptalAciklamasi : null, cancelTimes.TryGetValue(k.Id, out var cancelledAt) ? cancelledAt : null,
                    k.EslesmeTuru, k.EslesmeId, matched.TryGetValue(k.Id, out var state) ? state : null)).ToList());
    }
    /// <summary>İptal edilmemiş eşleşmelerin durumu: hedef kayıt duruyorsa 'Eslesti', silinmiş ya da iptal edilmişse 'KayitYok'.
    /// Tür başına tek sorgu.</summary>
    private static Dictionary<int, string> MatchStates(KasaDbContext db, IReadOnlyList<EkstreKayitEntity> rows)
    {
        int[] Ids(string type) => rows.Where(k => k.EslesmeTuru == type).Select(k => k.EslesmeId!.Value).Distinct().ToArray();
        var expenses = Ids(EslesmeTurleri.Gider);
        var charges = Ids(EslesmeTurleri.KartHarcama);
        var installments = Ids(EslesmeTurleri.KartTaksidi);
        var payments = Ids(EslesmeTurleri.KartOdeme);
        var live = new HashSet<(string, int)>();
        if (expenses.Length > 0)
            live.UnionWith(db.Islemler.AsNoTracking().Where(i => expenses.Contains(i.Id)).Select(i => i.Id).ToList().Select(id => (EslesmeTurleri.Gider, id)));
        if (charges.Length > 0)
            live.UnionWith(db.TakipHarcamalar.AsNoTracking().Where(h => charges.Contains(h.Id) && !h.Iptal).Select(h => h.Id).ToList().Select(id => (EslesmeTurleri.KartHarcama, id)));
        if (installments.Length > 0)
            live.UnionWith(db.TakipKartTaksitler.AsNoTracking().Where(t => installments.Contains(t.Id) && db.TakipHarcamalar.Any(h => h.Id == t.HarcamaId && !h.Iptal))
            .Select(t => t.Id).ToList().Select(id => (EslesmeTurleri.KartTaksidi, id)));
        if (payments.Length > 0)
            live.UnionWith(db.TakipKartOdemeler.AsNoTracking().Where(p => payments.Contains(p.Id) && !p.Iptal).Select(p => p.Id).ToList().Select(id => (EslesmeTurleri.KartOdeme, id)));
        return rows.ToDictionary(k => k.Id, k => live.Contains((k.EslesmeTuru!, k.EslesmeId!.Value)) ? EslesmeDurumlari.Eslesti : EslesmeDurumlari.KayitYok);
    }
    private static EkstreBelgeEntity GetDocument(KasaDbContext db, int id)
    {
        var d = db.EkstreBelgeler.SingleOrDefault(d => d.Id == id);
        Require(d is not null, "Belge bulunamadı.", 404);
        return d;
    }
    private static IResult Safe(Func<IResult> run) => FinansTakipEndpoints.Safe(() => { try { return run(); } catch (ImportException e) { return Error(e.Message, e.Status); } });
    private static IResult Error(string message, int status = 400) => Results.Json(new { hata = message }, statusCode: status);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string message, int status = 400) { if (!valid) throw new ImportException(message, status); }
    private sealed class ImportException(string message, int status) : Exception(message) { public int Status { get; } = status; }
}
