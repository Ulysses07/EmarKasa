using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>
/// "Hızlı ve hatasız giriş" (paket C) uçları. Hesap kurallarına dokunmaz: yalnız uyarır, mevcut
/// doğrulamalarla (<c>IslemHatasi</c>, <c>CariHatasi</c>) toplu yazar ve okuma kolaylıkları sunar.
/// Program.cs'deki yerel doğrulayıcılar parametre olarak gelir ki kurallar tek yerde kalsın.
/// </summary>
public static partial class HizliGirisEndpoints
{
    public static RouteGroupBuilder MapHizliGirisEndpoints(this RouteGroupBuilder api,
        Func<KasaDbContext, IslemEntity, string?> islemHatasi,
        Func<KasaDbContext, string, int?, string?> cariHatasi)
    {
        // Kaydetmeden önce uyarılar: yalnız bilgi verir, kaydı engellemez.
        api.MapPost("/islemler/uyarilar", (IslemUyariIstegi istek, KasaDbContext db, TimeProvider saat) =>
            Results.Ok(IslemUyarilari.Denetle(db, istek, Saat.Bugun(saat))))
            .RequireAuthorization("Editor");

        // Carinin (ya da sabit gider kaleminin) son işlemi: yeni işlemde kanal/tip önerisi. Yoksa 204.
        api.MapGet("/islemler/son", (string? cari, KasaDbContext db) =>
        {
            var ad = cari?.Trim() ?? "";
            if (ad.Length == 0) return Hata("Cari boş olamaz.");
            if (ad.Length > 200) return Hata("Cari adı en fazla 200 karakter olabilir.");
            // İşlemler kayıtlı yazımla tutulur: yazılan + kayıtlı cari/kalem yazımları aranır.
            var adaylar = new List<string> { ad };
            foreach (var a in db.Cariler.AsNoTracking().Select(c => c.Ad).AsEnumerable()
                         .Concat(db.GiderKalemleri.AsNoTracking().Select(k => k.Ad).AsEnumerable()))
                if (Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad) && !adaylar.Contains(a)) adaylar.Add(a);
            var son = db.Islemler.AsNoTracking()
                .Where(i => adaylar.Contains(i.Cari))
                .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id)
                .FirstOrDefault();
            return son is null
                ? Results.NoContent()
                : Results.Ok(new IslemOneriDto(son.Cari, son.Tarih, son.TutarTl, son.Kanal, son.Tip, son.KrediKartiId));
        });

        TopluUclari(api, islemHatasi, cariHatasi);
        GelenUclari(api);

        // Bir kaydın son silinme satırı: "Silindi · Geri al" şeridi geri alınacak geçmiş satırını buradan bulur.
        api.MapGet("/gecmis/son-silme", (string? tur, int? kayitId, KasaDbContext db, TimeProvider saat) =>
        {
            if (string.IsNullOrWhiteSpace(tur) || kayitId is null) return Hata("tur ve kayitId gerekli.");
            var d = db.Degisiklikler.AsNoTracking()
                .Where(x => x.Tur == tur && x.KayitId == kayitId && x.Eylem == Eylemler.Silindi)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
            if (d is null) return Results.NotFound();
            var simdi = saat.GetUtcNow().UtcDateTime;
            return Results.Ok(new DegisiklikDto(
                d.Id, DateTime.SpecifyKind(d.ZamanUtc, DateTimeKind.Utc), d.Rol, d.Tur, d.KayitId, d.Eylem, d.Ozet,
                d.EskiJson, d.YeniJson, d.GeriAlindi,
                d.GeriAlmaZamaniUtc is { } g ? DateTime.SpecifyKind(g, DateTimeKind.Utc) : null,
                GeriAlinabilir: GecmisKurallari.GeriAlmaEngeli(d, simdi) is null));
        });

        return api;
    }

    private static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

    private static bool KisitIhlali(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SqliteException { SqliteErrorCode: 19 }) return true; // SQLITE_CONSTRAINT
        return false;
    }

    private static DateOnly TakipBaslangici(KasaDbContext db)
        => db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).Select(a => a.TakipBaslangic).First();

    private static string Para(decimal d) => d.ToString("#,##0.00", Metin.Tr) + " ₺";
}
