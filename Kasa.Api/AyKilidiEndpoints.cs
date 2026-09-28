using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.AylikGiderEndpoints;

namespace Kasa.Api;

public record AyKilidiYaz(Guid IstekId, int Surum, int Yil, int Ay, string Aciklama);
public record AyKilidiDto(int Surum, DateOnly? KilitliSonTarih, IReadOnlyList<AyKilidiOlayDto> Gecmis);
public record AyKilidiOlayDto(int Id, DateOnly? OncekiSonTarih, DateOnly? YeniSonTarih, string Aciklama, DateTimeOffset Zaman);
public sealed class KilitliDonemException(string message) : Exception(message);

public static class AyKilidiEndpoints
{
    public static WebApplication MapAyKilidiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/ay-kilidi").RequireAuthorization("Finans");
        // Okuma: kilit durumu ve geçmişi aynı salt okunur anlık görüntüden; yazma kilidi alınmaz.
        api.MapGet("", (KasaDbContext db) => AlisEndpoints.Oku(db, () => Results.Ok(Read(db))));
        api.MapPost("/kapat", (AyKilidiYaz d, KasaDbContext db, TimeProvider saat) => Change(db, saat, d, false)).RequireAuthorization("Editor");
        api.MapPost("/ac", (AyKilidiYaz d, KasaDbContext db, TimeProvider saat) => Change(db, saat, d, true)).RequireAuthorization("Editor");
        return app;
    }
    private static IResult Change(KasaDbContext db, TimeProvider saat, AyKilidiYaz d, bool reopen) => Run(db, () =>
    {
        Text(d.Aciklama); var month = Month(d.Yil, d.Ay);
        // Kapatmadan önceki bakım (Sync) değişiklikleri de bu isteğin gerekçesini ve kimliğini taşır.
        using var denetim = db.Denetle(d.Aciklama, d.IstekId);
        var kind = reopen ? "AyKilidiAc" : "AyKilidiKapat";
        var digest = FinansHesaplari.Ozet(new { d.Yil, d.Ay, d.Aciklama });
        if (FinansHesaplari.Tekrar(db, d.IstekId, kind, digest, _ => Results.Ok(Read(db))) is { } replay) return replay;
        var state = db.AyKilidi.Single(); Need(state.Surum == d.Surum, "Dönem kilidi değişmiş. Yenileyin.", 409);
        DateOnly? next;
        if (reopen)
        {
            Need(state.KilitliSonTarih is { } last && month <= last, "Seçilen ay zaten açık.", 409);
            var baseline = db.Ayarlar.Select(a => a.TakipBaslangic).First();
            next = month <= new DateOnly(baseline.Year, baseline.Month, 1) || month.Year == 1 && month.Month == 1 ? null : month.AddDays(-1);
        }
        else
        {
            var end = month.AddMonths(1).AddDays(-1);
            var today = saat.IstanbulBugun();
            Need(month < new DateOnly(today.Year, today.Month, 1), "Yalnız tamamlanmış bir ay kapatılabilir.");
            Need(end >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "Takip başlangıcından önceki ay kapatılamaz.");
            Need(state.KilitliSonTarih is null || end > state.KilitliSonTarih, "Seçilen ay zaten kilitli.", 409);
            // Mevcut giderlerin takip bağları kilit sınırı konmadan tamamlanır.
            FinansTakipServisi.Sync(db); next = end;
        }
        var previous = state.KilitliSonTarih; var now = saat.GetUtcNow();
        // Kapatma, yeniden kilitlediği aralıkla kesişen açılış pencerelerini kapatır (kilit sınırı değişmeden okunur).
        IReadOnlyList<int> kapatilan = reopen ? [] : DenetimKilitPenceresi.Oku(db).Kesisen(DenetimKilitPenceresi.Sonraki(previous), next!.Value);
        var olay = new AyKilidiOlayEntity { OncekiSonTarih = previous, YeniSonTarih = next, Aciklama = d.Aciklama.Trim(), Zaman = now };
        db.AyKilidiOlaylar.Add(olay);
        state.KilitliSonTarih = next; state.Surum++;
        FinansHesaplari.IstekKaydet(db, d.IstekId, kind, digest, state.Id); db.SaveChanges();
        // Açma/kapatma merkezi denetim olayıdır: açma, penceresine düşecek değişikliklerin bağlanacağı kimliği (olay.Id) açar.
        KancaDisiOlaylar.AyKilidi(db, olay, reopen, kapatilan, d.IstekId);
        // Aynı transaction'da, yeni kilit sınırı kaydedildikten sonra: kapatılan ayların kanal kümesi (Ortak dağılımı ve rapor
        // satırları; bkz. AyKanalKumesi) ve raporu dondurulur, açılanların rapor görüntüsü silinir (görüntü yalnız kilitli ay için
        // bulunabilir; bkz. AyRaporAnlikGoruntusu). Kanal kümesi açılışta silinmez: açılan ay kendi kümesiyle hesaplanır.
        if (reopen) AyRaporAnlikGoruntusu.KilidiAcildi(db, next);
        else
        {
            AyKanalKumesi.AyKapanirken(db, next!.Value, now);
            AyRaporAnlikGoruntusu.Kilitlendi(db, previous, next.Value, now);
        }
        db.SaveChanges();
        return Results.Ok(Read(db));
    });
    private static AyKilidiDto Read(KasaDbContext db)
    {
        var state = db.AyKilidi.AsNoTracking().Single();
        return new(state.Surum, state.KilitliSonTarih, db.AyKilidiOlaylar.AsNoTracking().OrderByDescending(o => o.Id).Take(100)
            .Select(o => new AyKilidiOlayDto(o.Id, o.OncekiSonTarih, o.YeniSonTarih, o.Aciklama, o.Zaman)).ToList());
    }
}
