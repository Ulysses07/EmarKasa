using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// 04 · Grafik verisi ve aylık kur/endeks tablosu. Okuma her iki rol; tabloyu editör düzenler.
/// "TCMB'den doldur" yalnız USD/TRY ve EUR/TRY'yi doldurur; TÜFE ve altın hiçbir zaman uydurulmaz.
/// </summary>
public static class KurEndpoints
{
    /// <summary>Kur/endeks üst sınırı (yanlış basamak girişine karşı).</summary>
    public const decimal EnBuyukDeger = 10_000_000m;

    public static KurDto Dto(KurEntity k) => new(k.Ay, k.TufeEndeksi, k.UsdTry, k.EurTry, k.AltinGramTry);

    internal static string? DegerHatasi(decimal? d, string alan)
    {
        if (d is not { } v) return null;
        if (v <= 0) return $"{alan} sıfırdan büyük olmalı.";
        if (v > EnBuyukDeger) return $"{alan} çok büyük.";
        if (decimal.Round(v, 4) != v) return $"{alan} en fazla 4 ondalık basamak içerebilir.";
        return null;
    }

    public static RouteGroupBuilder MapKurlarVeGrafik(this RouteGroupBuilder api)
    {
        api.MapGet("/kurlar", (KasaDbContext db) =>
            db.Kurlar.AsNoTracking().OrderByDescending(k => k.Ay).ToList().Select(Dto).ToList());

        // Ayın satırını yazar (Ay'ın günü önemsizdir, ay başına çekilir). Tüm değerler boşsa satır silinir.
        api.MapPut("/kurlar", (KurDto dto, KasaDbContext db) =>
        {
            if (UcNokta.TarihHatasi(dto.Ay) is string th) return UcNokta.Hata(th);
            if ((DegerHatasi(dto.TufeEndeksi, "TÜFE endeksi") ?? DegerHatasi(dto.UsdTry, "USD/TRY")
                 ?? DegerHatasi(dto.EurTry, "EUR/TRY") ?? DegerHatasi(dto.AltinGramTry, "Gram altın")) is string h)
                return UcNokta.Hata(h);
            var ay = AyBicimi.AyBasi(dto.Ay);
            return UcNokta.Yaz(db, "Kur satırı aynı anda başka bir yerden kaydedildi; tekrar deneyin.", () =>
            {
                var e = db.Kurlar.FirstOrDefault(k => k.Ay == ay);
                bool bos = dto.TufeEndeksi is null && dto.UsdTry is null && dto.EurTry is null && dto.AltinGramTry is null;
                if (bos)
                {
                    if (e is not null) { db.Kurlar.Remove(e); db.SaveChanges(); }
                    return Results.Ok(new KurDto(ay, null, null, null, null));
                }
                if (e is null) { e = new KurEntity { Ay = ay }; db.Kurlar.Add(e); }
                e.TufeEndeksi = dto.TufeEndeksi; e.UsdTry = dto.UsdTry; e.EurTry = dto.EurTry; e.AltinGramTry = dto.AltinGramTry;
                db.SaveChanges();
                return Results.Ok(Dto(e));
            });
        }).RequireAuthorization("Editor");

        // TCMB'den doldur: ayın iş günlerindeki döviz satış kurlarının ortalaması (USD, EUR).
        api.MapPost("/kurlar/tcmb", async (KurTcmbIstekDto dto, KasaDbContext db, TcmbKurServisi tcmb, CancellationToken iptal) =>
        {
            if (UcNokta.TarihHatasi(dto.Ay) is string th) return UcNokta.Hata(th);
            var ay = AyBicimi.AyBasi(dto.Ay);
            TcmbAyOrtalamasi o;
            try { o = await tcmb.AyOrtalamasiAsync(ay, iptal); }
            catch (TcmbHatasi ex)
            {
                return ex.Baglanti
                    ? Results.Json(new { hata = ex.Message }, statusCode: StatusCodes.Status502BadGateway)
                    : UcNokta.Hata(ex.Message);
            }
            return UcNokta.Yaz(db, "Kur satırı aynı anda başka bir yerden kaydedildi; tekrar deneyin.", () =>
            {
                var e = db.Kurlar.FirstOrDefault(k => k.Ay == ay);
                if (e is null) { e = new KurEntity { Ay = ay }; db.Kurlar.Add(e); }
                e.UsdTry = o.UsdTry; e.EurTry = o.EurTry;
                db.SaveChanges();
                return Results.Ok(new KurTcmbSonucDto(Dto(e), o.GunSayisi, o.IlkGun, o.SonGun));
            });
        }).RequireAuthorization("Editor");

        // Seçilen aya kadar 24 ay: kanal geliri ve ay sonucu (nominal TL) + ayın kur/endeks satırı.
        api.MapGet("/rapor/grafik", (int yil, int ay, RaporServisi rapor) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string h) return UcNokta.Hata(h);
            return Results.Ok(rapor.Grafik(yil, ay));
        });
        return api;
    }
}
