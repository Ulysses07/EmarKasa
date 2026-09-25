using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public record AylikGiderSablonYaz(Guid IstekId, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, DateOnly GecerliAy, bool Aktif = true);
public record AylikGiderSablonDto(int Id, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, DateOnly GecerliAy, bool Aktif);
public record AylikGiderAyDto(int Yil, int Ay, decimal PlanlananToplam, decimal OdenenToplam, IReadOnlyList<AylikGiderSatirDto> Kayitlar);
public record AylikGiderSatirDto(int SablonId, int SablonSurum, string Ad, string Tur, decimal Tutar, DateOnly PlanlananTarih, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Durum, int? OdemeId = null, DateOnly? OdemeTarihi = null, int? IslemId = null);
public record AylikGiderOdemeYaz(Guid IstekId, int Surum, int Yil, int Ay, DateOnly Tarih, string? Not = null);
public record AylikGiderIptalYaz(Guid IstekId, string Aciklama);

public static class AylikGiderEndpoints
{
    public static WebApplication MapAylikGiderEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/aylik-giderler").RequireAuthorization("Finans");
        api.MapGet("/sablonlar", (KasaDbContext db) => Run(db, () => Results.Ok(db.AylikGiderRevizyonlar.AsNoTracking().ToList()
            .GroupBy(r => r.SablonId).Select(g => Template(db, g.MaxBy(r => r.Surum)!)).OrderBy(r => r.Ad).ToList())));
        api.MapPost("/sablonlar", (AylikGiderSablonYaz dto, KasaDbContext db) => SaveTemplate(db, 0, dto)).RequireAuthorization("Editor");
        api.MapPut("/sablonlar/{id:int}", (int id, AylikGiderSablonYaz dto, KasaDbContext db) => SaveTemplate(db, id, dto)).RequireAuthorization("Editor");
        api.MapGet("", (int yil, int ay, KasaDbContext db) => Run(db, () =>
        {
            var month = Month(yil, ay);
            var revisions = Revisions(db, month);
            var payments = db.AylikGiderOdemeler.AsNoTracking().Where(p => p.Ay == month && !p.Iptal).ToDictionary(p => p.SablonId);
            var rows = revisions.Where(r => r.Aktif || payments.ContainsKey(r.SablonId))
                .Select(r => payments.TryGetValue(r.SablonId, out var paid) ? Paid(db, paid) : Row(db, r, month)).OrderBy(r => r.PlanlananTarih).ThenBy(r => r.Ad).ToList();
            return Results.Ok(new AylikGiderAyDto(yil, ay, rows.Sum(r => r.Tutar), rows.Where(r => r.Durum == "Odendi").Sum(r => r.Tutar), rows));
        }));
        api.MapPost("/{sablonId:int}/ode", (int sablonId, AylikGiderOdemeYaz dto, KasaDbContext db) => Run(db, () =>
        {
            var digest = FinansHesaplari.Ozet(new { sablonId, dto.Yil, dto.Ay, dto.Tarih, dto.Not });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "AylikGiderOdeme", digest, id => Results.Ok(Paid(db, db.AylikGiderOdemeler.Single(p => p.Id == id)))) is { } replay) return replay;
            var month = Month(dto.Yil, dto.Ay);
            var revision = Revisions(db, month).SingleOrDefault(r => r.SablonId == sablonId);
            Need(revision is not null && revision.Aktif, "Bu ay için aktif şablon bulunamadı.", 404);
            Need(revision!.Surum == dto.Surum, "Şablon değişmiş. Ay listesini yenileyin.", 409);
            Need(!db.AylikGiderOdemeler.Any(p => p.SablonId == sablonId && p.Ay == month && !p.Iptal), "Bu şablonun bu aya ait ödemesi zaten kayıtlı.", 409);
            Need(dto.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First() && dto.Tarih <= FinansTakipServisi.Bugun, "Ödeme tarihi takip başlangıcı ile bugün arasında olmalı.");
            Need(dto.Tarih >= month, "Ödeme plan ayından önce olamaz.");
            Need((dto.Not?.Length ?? 0) <= 2000, "Not en fazla 2000 karakter olabilir.");
            AyKilidiKurallari.TarihAcik(db, month); AyKilidiKurallari.TarihAcik(db, dto.Tarih);
            var shares = FinansTakipServisi.Adlandir(db, FinansTakipServisi.Read<KanalPayYaz>(revision.DagilimJson));
            var expense = new IslemEntity { Tarih = dto.Tarih, Cari = revision.Ad, TutarTl = revision.Tutar, Tip = GiderTipi.SabitGider,
                KanalId = shares.Count == 1 ? shares[0].KanalId : null,
                Kanal = shares.Count == 0 ? "Genel kasa" : string.Join(" / ", shares.Select(s => s.Kanal)), Not = dto.Not?.Trim() };
            db.Islemler.Add(expense); db.SaveChanges();
            var payment = new AylikGiderOdemeEntity { SablonId = sablonId, RevizyonId = revision.Id, Ay = month, Tarih = dto.Tarih, Tutar = revision.Tutar, IslemId = expense.Id };
            db.AylikGiderOdemeler.Add(payment); db.SaveChanges();
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "AylikGiderOdeme", digest, payment.Id); db.SaveChanges();
            return Results.Ok(Paid(db, payment));
        })).RequireAuthorization("Editor");
        api.MapPost("/odemeler/{odemeId:int}/iptal", (int odemeId, AylikGiderIptalYaz dto, KasaDbContext db) => Run(db, () =>
        {
            Text(dto.Aciklama);
            var digest = FinansHesaplari.Ozet(new { odemeId, dto.Aciklama });
            if (FinansHesaplari.Tekrar(db, dto.IstekId, "AylikGiderIptal", digest, id => Results.Ok(Paid(db, db.AylikGiderOdemeler.Single(p => p.Id == id)))) is { } replay) return replay;
            var p = db.AylikGiderOdemeler.SingleOrDefault(p => p.Id == odemeId); Need(p is not null, "Ödeme bulunamadı.", 404);
            Need(!p!.Iptal, "Ödeme zaten iptal edilmiş.", 409); AyKilidiKurallari.TarihAcik(db, p.Tarih); AyKilidiKurallari.TarihAcik(db, p.Ay);
            db.AylikGiderDegisikligi = true;
            if (p.IslemId is { } expenseId) db.Islemler.Remove(db.Islemler.Single(i => i.Id == expenseId));
            p.IslemId = null; p.Iptal = true; p.IptalAciklamasi = dto.Aciklama.Trim();
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "AylikGiderIptal", digest, p.Id); db.SaveChanges();
            return Results.Ok(Paid(db, p));
        })).RequireAuthorization("Editor");
        return app;
    }

    private static IResult SaveTemplate(KasaDbContext db, int id, AylikGiderSablonYaz d) => Run(db, () =>
    {
        var digest = FinansHesaplari.Ozet(new { id, d.Ad, d.Tur, d.Tutar, d.OdemeGunu, d.DagilimTuru, d.Dagilimlar, d.GecerliAy, d.Aktif });
        if (FinansHesaplari.Tekrar(db, d.IstekId, "AylikGiderSablon", digest, key => Results.Ok(Template(db, db.AylikGiderRevizyonlar.Where(r => r.SablonId == key).OrderByDescending(r => r.Surum).First()))) is { } replay) return replay;
        Text(d.Ad); Need(d.Tur is "Kira" or "Maas" or "Fatura" or "Diger", "Geçerli gider türü seçin.");
        Need(d.Tutar > 0 && d.Tutar <= 999_999_999_999.99m && decimal.Round(d.Tutar, 2) == d.Tutar, "Pozitif, kuruş hassasiyetinde tutar girin.");
        Need(d.OdemeGunu is >= 1 and <= 31, "Ödeme günü 1–31 olmalı.");
        var today = FinansTakipServisi.Bugun; var current = new DateOnly(today.Year, today.Month, 1);
        Need(d.GecerliAy.Day == 1 && d.GecerliAy >= current && d.GecerliAy.Year <= 9990, "Geçerlilik cari veya ileri ayın ilk günü olmalı.");
        Need(d.DagilimTuru is "Genel" or "Esit" or "Ozel", "Geçerli dağılım türü seçin.");
        Need(d.Dagilimlar is not null && d.Dagilimlar.Count <= 100 && d.Dagilimlar.All(p => p is not null), "En fazla 100 kanal seçin.");
        var parts = d.Dagilimlar!; var ids = parts.Select(p => p.KanalId).ToList();
        Need(ids.Distinct().Count() == ids.Count && db.Kanallar.Count(k => ids.Contains(k.Id)) == ids.Count, "Kayıtlı ve tekil kanallar seçin.");
        Need(d.DagilimTuru == "Genel" ? parts.Count == 0 : parts.Count > 0, "Genel giderde kanal seçmeyin; diğer türlerde kanal seçin.");
        List<KanalPayYaz> shares = [];
        if (d.DagilimTuru == "Esit") shares = FinansTakipServisi.EsitPaylar(ids, d.Tutar);
        if (d.DagilimTuru == "Ozel")
        {
            Need(parts.All(p => p.Tutar > 0 && p.Tutar <= d.Tutar && decimal.Round(p.Tutar, 2) == p.Tutar) && parts.Sum(p => p.Tutar) == d.Tutar, "Kanal tutarları pozitif ve toplamı gider tutarına eşit olmalı.");
            shares = parts.OrderBy(p => p.KanalId).ToList();
        }
        AylikGiderSablonEntity template;
        if (id == 0) { Need(d.Surum == 0, "Yeni şablon sürümü 0 olmalı."); template = new(); db.AylikGiderSablonlar.Add(template); db.SaveChanges(); }
        else
        {
            template = db.AylikGiderSablonlar.SingleOrDefault(s => s.Id == id)!; Need(template is not null, "Şablon bulunamadı.", 404);
            Need(template.Surum == d.Surum, "Şablon değişmiş. Yenileyin.", 409);
            Need(!db.AylikGiderRevizyonlar.Any(r => r.SablonId == id && r.GecerliAy > d.GecerliAy), "Yeni sürüm son planlanan geçerlilik ayından önce olamaz."); template.Surum++;
        }
        var revision = new AylikGiderRevizyonEntity { SablonId = template.Id, Surum = template.Surum, Ad = d.Ad.Trim(), Tur = d.Tur, Tutar = d.Tutar,
            OdemeGunu = d.OdemeGunu, GecerliAy = d.GecerliAy, DagilimTuru = d.DagilimTuru, DagilimJson = FinansTakipServisi.Json(shares), Aktif = d.Aktif };
        db.AylikGiderRevizyonlar.Add(revision); FinansHesaplari.IstekKaydet(db, d.IstekId, "AylikGiderSablon", digest, template.Id); db.SaveChanges();
        return Results.Ok(Template(db, revision));
    });
    private static List<AylikGiderRevizyonEntity> Revisions(KasaDbContext db, DateOnly month) => db.AylikGiderRevizyonlar.AsNoTracking().Where(r => r.GecerliAy <= month).ToList()
        .GroupBy(r => r.SablonId).Select(g => g.OrderByDescending(r => r.GecerliAy).ThenByDescending(r => r.Surum).First()).ToList();
    private static AylikGiderSablonDto Template(KasaDbContext db, AylikGiderRevizyonEntity r) => new(r.SablonId, r.Surum, r.Ad, r.Tur, r.Tutar, r.OdemeGunu, r.DagilimTuru,
        FinansTakipServisi.Adlandir(db, FinansTakipServisi.Read<KanalPayYaz>(r.DagilimJson)), r.GecerliAy, r.Aktif);
    private static AylikGiderSatirDto Row(KasaDbContext db, AylikGiderRevizyonEntity r, DateOnly month) => new(r.SablonId, r.Surum, r.Ad, r.Tur, r.Tutar,
        FinansTakipServisi.Gun(month, r.OdemeGunu), r.DagilimTuru, FinansTakipServisi.Adlandir(db, FinansTakipServisi.Read<KanalPayYaz>(r.DagilimJson)), "Planlandi");
    private static AylikGiderSatirDto Paid(KasaDbContext db, AylikGiderOdemeEntity p) => Row(db, db.AylikGiderRevizyonlar.AsNoTracking().Single(r => r.Id == p.RevizyonId), p.Ay)
        with { Durum = p.Iptal ? "Iptal" : "Odendi", OdemeId = p.Id, OdemeTarihi = p.Tarih, IslemId = p.IslemId };
    internal static DateOnly Month(int year, int month) { Need(year is >= 1 and <= 9990 && month is >= 1 and <= 12, "Geçerli ay seçin."); return new(year, month, 1); }
    internal static void Text(string? value) => Need(!string.IsNullOrWhiteSpace(value) && value.Length <= 2000, "Ad/açıklama zorunlu ve en fazla 2000 karakter olmalı.");
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool test, string message, int status = 400) { if (!test) throw new AylikGiderHatasi(message, status); }
    internal static IResult Run(KasaDbContext db, Func<IResult> action)
    {
        try { return AlisEndpoints.Mutate(db, action); }
        catch (AylikGiderHatasi e) { return Results.Json(new { hata = e.Message }, statusCode: e.Status); }
        finally { db.AylikGiderDegisikligi = false; }
    }
    private sealed class AylikGiderHatasi(string message, int status) : Exception(message) { public int Status => status; }
}
