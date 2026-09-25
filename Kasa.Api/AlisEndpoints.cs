using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class AlisEndpoints
{
    private const decimal TutarSiniri = 999_999_999_999.99m;

    public static WebApplication MapAlisEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/alis").RequireAuthorization("Alis");
        api.MapGet("/kanallar", (KasaDbContext db) => db.Kanallar.AsNoTracking().OrderBy(k => k.Sira)
            .Select(k => new AlisKanalDto(k.Id, k.Ad, k.Aktif)).ToList());
        api.MapGet("", (ClaimsPrincipal user, KasaDbContext db) =>
        {
            if (!Editor(user) && AliciId(user) is null) return Results.Forbid();
            using var transaction = db.Database.BeginTransaction();
            var query = Query(db).AsNoTracking();
            if (!Editor(user)) query = query.Where(a => a.AliciId == AliciId(user));
            var kartAdlari = db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
            var result = query.OrderByDescending(a => a.Tarih).ThenByDescending(a => a.Id).ToList().Select(a => AlisHesaplari.ToDto(a, kartAdlari)).ToList();
            transaction.Commit();
            return Results.Ok(result);
        });
        api.MapPost("", (AlisYaz dto, ClaimsPrincipal user, KasaDbContext db) => Mutate(db, () =>
        {
            if (!Editor(user) && AliciId(user) is null) return Results.Forbid();
            if (dto.Surum != 0) return Conflict("Yeni alış için sürüm 0 olmalı.");
            if (Validate(dto, db) is { } hata) return hata;
            var alis = new AlisEntity { AliciId = Editor(user) ? null : AliciId(user) };
            if (SetFields(db, alis, dto) is { } tedarikciHatasi) return tedarikciHatasi;
            db.Alislar.Add(alis);
            db.SaveChanges();
            return Results.Created($"/api/alis/{alis.Id}", ReadDto(db, alis.Id));
        }));
        api.MapPut("/{id:int}", (int id, AlisYaz dto, ClaimsPrincipal user, KasaDbContext db) => Mutate(db, () =>
        {
            var alis = Owned(db, id, user);
            if (alis is null) return Results.NotFound();
            if (alis.Surum != dto.Surum) return VersionConflict();
            if (alis.Durum == AlisDurumlari.Onaylandi || (!Editor(user) && alis.Durum != AlisDurumlari.Taslak))
                return Conflict("Bu alış düzenlenemez. Onaylı alış önce açıklamayla iade edilmelidir.");
            if (Validate(dto, db) is { } hata) return hata;
            if (dto.Kalemler.Sum(k => k.Tutar) < alis.Odemeler.Sum(o => o.Islem.TutarTl))
                return Conflict("Alış toplamı, kaydedilmiş ödemelerin altına indirilemez.");
            db.AlisKalemler.RemoveRange(alis.Kalemler);
            if (SetFields(db, alis, dto) is { } tedarikciHatasi) return tedarikciHatasi;
            alis.Surum++;
            db.SaveChanges();
            return Results.Ok(ReadDto(db, id));
        }));
        api.MapPost("/{id:int}/gonder", (int id, AlisDurumYaz dto, ClaimsPrincipal user, KasaDbContext db) => Mutate(db, () =>
        {
            var alis = Owned(db, id, user);
            if (alis is null) return Results.NotFound();
            if (alis.Surum != dto.Surum) return VersionConflict();
            if (alis.Durum != AlisDurumlari.Taslak) return Conflict("Yalnız taslak alış incelemeye gönderilebilir.");
            alis.Durum = AlisDurumlari.Incelemede;
            alis.Surum++;
            db.SaveChanges();
            return Results.Ok(ReadDto(db, id));
        }));
        api.MapPost("/{id:int}/onayla", (int id, AlisDurumYaz dto, KasaDbContext db) => Mutate(db, () =>
        {
            var alis = Query(db).SingleOrDefault(a => a.Id == id);
            if (alis is null) return Results.NotFound();
            if (alis.Durum == AlisDurumlari.Onaylandi) return Results.Ok(ReadDto(db, id));
            if (alis.Surum != dto.Surum) return VersionConflict();
            if (alis.Durum != AlisDurumlari.Incelemede) return Conflict("Yalnız incelemedeki alış onaylanabilir.");
            var v = new GirdiDogrulama();
            v.Metin(dto.Not, "not", 2000, zorunlu: false);
            v.Kontrol(alis.Kalemler.Any(k => k.Tutar > 0), "kalemler", "En az bir pozitif alış kalemi gerekir.");
            v.Kontrol(alis.Kalemler.All(k => k.Dagilimlar.Sum(d => d.Tutar) == k.Tutar), "dagilimlar", "Her kalemin kanal dağılımı kalem tutarına tam eşit olmalı.");
            if (v.Sonuc() is { } hata) return hata;
            alis.Durum = AlisDurumlari.Onaylandi;
            alis.EditorNotu = dto.Not?.Trim();
            alis.Surum++;
            db.SaveChanges();
            return Results.Ok(ReadDto(db, id));
        })).RequireAuthorization("Editor");
        api.MapPost("/{id:int}/iade", (int id, AlisDurumYaz dto, KasaDbContext db) => Mutate(db, () =>
        {
            var alis = Query(db).SingleOrDefault(a => a.Id == id);
            if (alis is null) return Results.NotFound();
            if (alis.Surum != dto.Surum) return VersionConflict();
            if (alis.Durum is not (AlisDurumlari.Incelemede or AlisDurumlari.Onaylandi))
                return Conflict("Yalnız incelemedeki veya onaylı alış iade edilebilir.");
            var v = new GirdiDogrulama();
            v.Metin(dto.Not, "not", 2000);
            if (v.Sonuc() is { } hata) return hata;
            alis.Durum = AlisDurumlari.Taslak;
            alis.EditorNotu = dto.Not!.Trim();
            alis.Surum++;
            db.SaveChanges();
            return Results.Ok(ReadDto(db, id));
        })).RequireAuthorization("Editor");
        api.MapPost("/{id:int}/odemeler", (int id, AlisOdemeYaz dto, KasaDbContext db) => Mutate(db, () => Pay(db, id, dto)))
            .RequireAuthorization("Editor");
        api.MapPut("/{id:int}/odemeler/{odemeId:int}", (int id, int odemeId, AlisOdemeDuzelt dto, KasaDbContext db) => Mutate(db, () => AlisOdemeIslemleri.Duzelt(db, id, odemeId, dto))).RequireAuthorization("Editor");
        api.MapPost("/{id:int}/odemeler/{odemeId:int}/iptal", (int id, int odemeId, AlisOdemeIptal dto, KasaDbContext db) => Mutate(db, () => AlisOdemeIslemleri.Iptal(db, id, odemeId, dto))).RequireAuthorization("Editor");
        return app;
    }

    private static IResult Pay(KasaDbContext db, int id, AlisOdemeYaz dto)
    {
        var v = new GirdiDogrulama();
        v.Kontrol(dto.IstekId != Guid.Empty, "istekId", "Tekrarları önlemek için geçerli bir istek kimliği gerekir.");
        v.Tarih(dto.Tarih, "tarih"); v.Para(dto.Tutar, "tutar");
        v.Kontrol(dto.Tutar > 0, "tutar", "Ödeme tutarı sıfırdan büyük olmalı.");
        v.Metin(dto.Not, "not", 2000, zorunlu: false);
        v.Kontrol(dto.HesapId is null, "hesapId", "Ödeme doğrudan kanal ve genel kasaya kaydedilir; ayrı hesap seçilmez.");
        if (v.Sonuc() is { } hata) return hata;

        var digest = Digest(id, dto);
        var replay = db.FinansIstekler.AsNoTracking().SingleOrDefault(o => o.IstekId == dto.IstekId);
        if (replay is not null)
            return replay.Tur == "AlisOdeme" && replay.SonucId == id && replay.Ozet == digest
                ? Results.Ok(ReadDto(db, id))
                : Conflict("Bu istek kimliği farklı bir ödeme için kullanılmış.");

        var alis = Query(db).SingleOrDefault(a => a.Id == id);
        if (alis is null) return Results.NotFound();
        if (alis.Surum != dto.Surum) return VersionConflict();
        v.Kontrol(dto.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "tarih", "Ödeme tarihi takip başlangıcından önce olamaz.");
        v.Kart(db, dto.KrediKartiId);
        if (dto.KrediKartiId is { } cardId)
            v.Kontrol(!db.TakipKartlar.Any(k => k.KrediKartiId == cardId && dto.Tarih < k.Baslangic), "tarih", "Kart harcaması kart takip başlangıcından önce olamaz.");
        if (v.Sonuc() is { } alanHatasi) return alanHatasi;
        if (alis.Odemeler.Sum(o => o.Islem.TutarTl) + dto.Tutar > alis.Kalemler.Sum(k => k.Tutar))
            return Conflict("Ödemeler alış toplamını aşamaz.");

        IslemEntity islem;
        if (dto.MevcutIslemId is { } islemId)
        {
            var existing = db.Islemler.Include(i => i.HesapHareketi).SingleOrDefault(i => i.Id == islemId);
            if (existing is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mevcutIslemId"] = ["Kayıtlı bir gider seçin."] });
            if (existing.Tip is not (GiderTipi.Cari or GiderTipi.KrediKarti))
                return Conflict("Yalnız cari veya kredi kartı gideri alışa bağlanabilir.");
            if (existing.Tarih != dto.Tarih || existing.TutarTl != dto.Tutar || existing.KrediKartiId != dto.KrediKartiId)
                return Conflict("Seçilen giderin tarih, tutar ve kart bilgileri ödeme ile eşleşmiyor.");
            if (db.AlisOdemeler.Any(o => o.IslemId == islemId)) return Conflict("Bu gider zaten bir alışa bağlı.");
            if (db.KrediTaksitOdemeler.Any(o => o.IslemId == islemId)) return Conflict("Kredi taksidi alışa bağlanamaz.");
            if (existing.HesapHareketi is { } h && dto.HesapId != h.HesapId) return Conflict("Giderin bağlı olduğu hesap ödeme hesabıyla eşleşmiyor.");
            islem = existing;
        }
        else
        {
            islem = new IslemEntity
            {
                Tarih = dto.Tarih, Cari = alis.Tedarikci, TutarTl = dto.Tutar,
                Kanal = Kanallar.DagilimBekliyor, KanalId = null,
                Tip = dto.KrediKartiId is null ? GiderTipi.Cari : GiderTipi.KrediKarti,
                KrediKartiId = dto.KrediKartiId, Not = dto.Not?.Trim()
            };
        }
        alis.Odemeler.Add(new AlisOdemeEntity { Islem = islem, IstekId = dto.IstekId, IstekOzeti = digest });
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "AlisOdeme", digest, id);
        alis.Surum++;
        db.SaveChanges();
        FinansTakipServisi.Sync(db);
        return Results.Ok(ReadDto(db, id));
    }

    private static IResult? Validate(AlisYaz dto, KasaDbContext db)
    {
        var v = new GirdiDogrulama();
        v.Tarih(dto.Tarih, "tarih"); v.Metin(dto.Tedarikci, "tedarikci");
        v.Metin(dto.Not, "not", 2000, zorunlu: false);
        v.Kontrol(dto.TedarikciId is null, "tedarikciId", "Cari kartı tutulmaz; ödeme yapılan yeri açıklama olarak yazın.");
        v.Kontrol(dto.Vade is null, "vade", "Vade takibi ERP12'de tutulur.");
        v.Kontrol(dto.Kalemler is not null && dto.Kalemler.Count <= 100, "kalemler", "Kalem listesi zorunludur ve en fazla 100 kalem içerebilir.");
        if (v.Sonuc() is { } erkenHata) return erkenHata;
        var channelIds = db.Kanallar.Select(k => k.Id).ToHashSet();
        decimal total = 0;
        for (var i = 0; i < dto.Kalemler!.Count; i++)
        {
            var line = dto.Kalemler[i];
            if (line is null) { v.Kontrol(false, $"kalemler[{i}]", "Kalem boş olamaz."); continue; }
            var key = $"kalemler[{i}]";
            v.Metin(line.Aciklama, key + ".aciklama", 500); v.Para(line.Tutar, key + ".tutar");
            v.Kontrol(line.Miktar is null && line.BirimFiyat is null, key + ".tutar", "Kasa dağılımı için mal açıklaması ve toplam tutarı girin.");
            if (line.Tutar >= 0 && line.Tutar <= TutarSiniri) total += line.Tutar;
            v.Kontrol(line.Dagilimlar is not null && line.Dagilimlar.Count <= 100, key + ".dagilimlar", "Dağılım listesi zorunludur ve en fazla 100 kanal içerebilir.");
            if (line.Dagilimlar is null || line.Dagilimlar.Count > 100) continue;
            var selected = new HashSet<int>();
            for (var j = 0; j < line.Dagilimlar.Count; j++)
            {
                var part = line.Dagilimlar[j];
                if (part is null) { v.Kontrol(false, key + ".dagilimlar", "Dağılım boş olamaz."); continue; }
                v.Kontrol(channelIds.Contains(part.KanalId), key + $".dagilimlar[{j}].kanalId", "Kayıtlı bir kanal seçin.");
                v.Kontrol(selected.Add(part.KanalId), key + ".dagilimlar", "Aynı kanal bir kalemde birden fazla seçilemez.");
                v.Para(part.Tutar, key + $".dagilimlar[{j}].tutar");
            }
        }
        v.Para(total, "toplam");
        return v.Sonuc();
    }

    private static IResult? SetFields(KasaDbContext db, AlisEntity alis, AlisYaz dto)
    {
        // Firma adı yalnız hareket açıklamasıdır; ERP12'den bağımsız cari kartı üretilmez.
        alis.Tarih = dto.Tarih; alis.Tedarikci = dto.Tedarikci.Trim(); alis.Not = dto.Not?.Trim();
        alis.Kalemler = dto.Kalemler.Select(k => new AlisKalemEntity
        {
            Aciklama = k.Aciklama.Trim(), Tutar = k.Tutar, Miktar = k.Miktar, BirimFiyat = k.BirimFiyat,
            Dagilimlar = k.Dagilimlar.Select(d => new AlisDagilimEntity { KanalId = d.KanalId, Tutar = d.Tutar }).ToList()
        }).ToList();
        return null;
    }

    internal static IQueryable<AlisEntity> Query(KasaDbContext db) => db.Alislar.Include(a => a.AliciKaydi).Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).ThenInclude(d => d.KanalKaydi)
        .Include(a => a.Odemeler).ThenInclude(o => o.Islem).ThenInclude(i => i.HesapHareketi).AsSplitQuery();

    private static AlisEntity? Owned(KasaDbContext db, int id, ClaimsPrincipal user) =>
        Query(db).SingleOrDefault(a => a.Id == id && (Editor(user) || (a.AliciId != null && a.AliciId == AliciId(user))));
    private static bool Editor(ClaimsPrincipal user) => user.IsInRole("editor");
    private static int? AliciId(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue("alici_id"), out var id) && id > 0 ? id : null;
    internal static AlisDto ReadDto(KasaDbContext db, int id) => AlisHesaplari.ToDto(Query(db).AsNoTracking().Single(a => a.Id == id),
        db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad));
    internal static IResult Conflict(string message) => Results.Conflict(new { hata = message });
    private static IResult VersionConflict() => Conflict("Alış başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.");

    internal static IResult Mutate(KasaDbContext db, Func<IResult> action)
    {
        try
        {
            // SQLite'ın normal transaction'ı BEGIN IMMEDIATE kullanır: başlık ve
            // ödeme toplamı aynı yazma kilidi altında okunur; Surum ayrıca EF token'ıdır.
            using var transaction = db.Database.BeginTransaction();
            var result = action();
            if (result is IStatusCodeHttpResult status && status.StatusCode >= 400) return result;
            transaction.Commit();
            return result;
        }
        catch (DbUpdateConcurrencyException) { return VersionConflict(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19 && e.Message.Contains("Kilitli ay", StringComparison.Ordinal))
        { return Conflict("Bu tarih kilitli dönemde. Değişiklik için ilgili ayı gerekçeyle açın."); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 or 5 or 6 })
        { return Conflict("Bağlı kayıt değişmiş veya başka bir ödeme kaydedilmiş. Listeyi yenileyin."); }
        catch (SqliteException e) when (e.SqliteErrorCode is 19 or 5 or 6)
        { return Conflict("Başka bir kayıt işlemiyle çakışma oldu. Listeyi yenileyip tekrar deneyin."); }
    }

    private static string Digest(int alisId, AlisOdemeYaz dto)
    {
        var payload = JsonSerializer.Serialize(new
        {
            alisId, tarih = dto.Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tutar = dto.Tutar.ToString("0.00", CultureInfo.InvariantCulture), dto.KrediKartiId,
            dto.MevcutIslemId, not = dto.Not?.Trim()
        });
        if (dto.HesapId is { } hesap) payload += "|hesap:" + hesap.ToString(CultureInfo.InvariantCulture);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
