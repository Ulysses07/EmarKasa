using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Kasa.Api.Endpoints;

/// <summary>
/// İşlem ekleri (fiş/fatura fotoğrafı, PDF) ve işlemin belge alanlarının hızlı güncellemesi.
/// Okuma: oturum açmış herkes; yükleme/silme/güncelleme: editör.
/// </summary>
public static class BelgeEndpoints
{
    public static RouteGroupBuilder MapBelgeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/islemler/{id:int}/ekler", (int id, KasaDbContext db) =>
        {
            if (!db.Islemler.Any(i => i.Id == id)) return Results.NotFound();
            return Results.Ok(db.IslemEkleri.AsNoTracking().Where(e => e.IslemId == id)
                .OrderBy(e => e.Id).AsEnumerable().Select(Dto).ToList());
        });

        // Tek istekte tek dosya: multipart/form-data, alan adı "dosya".
        api.MapPost("/islemler/{id:int}/ekler", async (int id, HttpRequest istek, KasaDbContext db, BelgeDeposu depo,
            TimeProvider saat, CancellationToken iptal) =>
        {
            if (istek.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sinir)
                sinir.MaxRequestBodySize = BelgeDeposu.EnFazlaBoyut + 1024 * 1024;
            if (!db.Islemler.Any(i => i.Id == id)) return Results.NotFound();
            if (!istek.HasFormContentType) return UcNokta.Hata("Dosya multipart/form-data olarak gönderilmeli.");

            IFormCollection form;
            try
            {
                form = await istek.ReadFormAsync(new FormOptions
                {
                    MultipartBodyLengthLimit = BelgeDeposu.EnFazlaBoyut + 64 * 1024,
                    ValueCountLimit = 16,
                }, iptal);
            }
            catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
            {
                return UcNokta.Hata(BoyutMesaji);
            }
            if (form.Files.Count != 1) return UcNokta.Hata("Tek istekte tek dosya gönderin.");
            var dosya = form.Files[0];
            if (dosya.Length == 0) return UcNokta.Hata("Dosya boş.");
            if (dosya.Length > BelgeDeposu.EnFazlaBoyut) return UcNokta.Hata(BoyutMesaji);

            var ad = BelgeDeposu.AdTemizle(dosya.FileName);
            var bas = new byte[16];
            int okunan;
            await using (var s = dosya.OpenReadStream())
                okunan = await s.ReadAtLeastAsync(bas, bas.Length, throwOnEndOfStream: false, iptal);
            if (BelgeDeposu.TurBelirle(bas.AsSpan(0, okunan)) is not { } tur) return UcNokta.Hata(TurMesaji);
            if (Path.HasExtension(ad))
            {
                if (!BelgeDeposu.UzantiUyuyor(ad, tur.Uzanti))
                    return UcNokta.Hata(BelgeDeposu.IzinliUzantilar.Contains(Path.GetExtension(ad).TrimStart('.').ToLowerInvariant())
                        ? "Dosyanın içeriği uzantısıyla uyuşmuyor." : TurMesaji);
            }
            else ad = BelgeDeposu.AdTemizle(ad + "." + tur.Uzanti);

            // Hızlı ön kontrol (kesin kontrol aşağıda transaction içinde).
            if (db.IslemEkleri.Count(e => e.IslemId == id) >= BelgeDeposu.IslemBasinaEnFazla) return UcNokta.Hata(SayiMesaji);

            string depoAdi;
            await using (var s = dosya.OpenReadStream())
                depoAdi = await depo.YazAsync(s, tur.Uzanti, iptal);

            IResult? sonuc = null;
            try
            {
                sonuc = UcNokta.Yaz(db, "Ek kaydedilemedi; tekrar deneyin.", () =>
                {
                    if (!db.Islemler.Any(i => i.Id == id)) return Results.NotFound();
                    if (db.IslemEkleri.Count(e => e.IslemId == id) >= BelgeDeposu.IslemBasinaEnFazla) return UcNokta.Hata(SayiMesaji);
                    var e = new IslemEkiEntity
                    {
                        IslemId = id, OrijinalAd = ad, DepoAdi = depoAdi, IcerikTipi = tur.IcerikTipi,
                        Boyut = dosya.Length, YuklemeZamaniUtc = saat.GetUtcNow().UtcDateTime,
                    };
                    db.IslemEkleri.Add(e);
                    db.SaveChanges();
                    return Results.Created($"/api/ekler/{e.Id}", Dto(e));
                });
                return sonuc;
            }
            finally
            {
                // Kayıt oluşmadıysa (doğrulama, çakışma ya da hata) dosya diskte yetim kalmasın.
                if (sonuc is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status201Created }) depo.Sil(depoAdi);
            }
        }).RequireAuthorization("Editor").DisableAntiforgery();

        // İndirme: her zaman "attachment" (tarayıcıda çalıştırılmaz), önbelleğe alınmaz.
        api.MapGet("/ekler/{id:int}", (int id, KasaDbContext db, BelgeDeposu depo, HttpContext http) =>
        {
            var e = db.IslemEkleri.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (e is null || depo.Yol(e.DepoAdi) is not { } yol || !File.Exists(yol)) return Results.NotFound();
            http.Response.Headers[HeaderNames.CacheControl] = "no-store";
            return TypedResults.PhysicalFile(yol, e.IcerikTipi, e.OrijinalAd);
        });

        api.MapDelete("/ekler/{id:int}", (int id, KasaDbContext db, BelgeDeposu depo) =>
        {
            string? silinecek = null;
            var sonuc = UcNokta.Yaz(db, "Ek silinemedi; tekrar deneyin.", () =>
            {
                var e = db.IslemEkleri.Find(id);
                if (e is null) return Results.NotFound();
                db.IslemEkleri.Remove(e);
                db.SaveChanges();
                silinecek = e.DepoAdi;
                return Results.NoContent();
            });
            // Dosya, kayıt silindikten (commit) SONRA silinir; silinemezse gece temizliği kaldırır.
            if (silinecek is not null && sonuc is IStatusCodeHttpResult { StatusCode: StatusCodes.Status204NoContent }) depo.Sil(silinecek);
            return sonuc;
        }).RequireAuthorization("Editor");

        // Yalnız belge alanları (fatura takibinden "fatura geldi" gibi hızlı güncelleme).
        api.MapPut("/islemler/{id:int}/belge", (int id, BelgeYazDto dto, KasaDbContext db) =>
            UcNokta.Yaz(db, "İşlem güncellenemedi; tekrar deneyin.", () =>
        {
            var e = db.Islemler.Find(id);
            if (e is null) return Results.NotFound();
            var gelen = new IslemEntity { BelgeTuru = dto.BelgeTuru, BelgeNo = dto.BelgeNo, FaturaBekleniyor = dto.FaturaBekleniyor };
            if (BelgeKurallari.Hata(gelen) is string hata) return UcNokta.Hata(hata);
            BelgeKurallari.Kopyala(gelen, e);
            db.SaveChanges();
            return Results.Ok(e);
        })).RequireAuthorization("Editor");

        return api;
    }

    private static EkDto Dto(IslemEkiEntity e) => new(e.Id, e.IslemId, e.OrijinalAd, e.IcerikTipi, e.Boyut, e.YuklemeZamaniUtc);

    private const string BoyutMesaji = "Dosya en fazla 10 MB olabilir.";
    private const string TurMesaji = "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.";
    private static readonly string SayiMesaji = $"Bir işleme en fazla {BelgeDeposu.IslemBasinaEnFazla} ek eklenebilir.";
}
