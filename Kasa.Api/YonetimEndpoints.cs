using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class YonetimEndpoints
{
    /// <summary>Desteklenen en eski masaüstü istemci sürümü (/api/surum "minimumIstemci"): daha eski istemci güncelleme ister
    /// (GuvenlikViewModel). Sunucu sürümünden (<see cref="SunucuSurumu"/>, KasaSurumu) bağımsızdır; yalnız eski istemcinin
    /// artık desteklenmediğine karar verilince elle yükseltilir.</summary>
    public const string MinimumIstemci = "2.4.1";

    public static WebApplication MapYonetimEndpoints(this WebApplication app)
    {
        app.MapGet("/api/surum", (IConfiguration cfg) =>
        {
            var windows = IstemciYayinAyarlari.Oku(cfg, "windows");
            var ios = IstemciYayinAyarlari.Oku(cfg, "ios");
            return Results.Ok(new
            {
                // Eski istemcilerin ilk dört alanı korunur; platform göndermeyen istemci Windows kabul edilir.
                surum = SunucuSurumu.Deger,
                minimumIstemci = windows.MinimumIstemci,
                indirmeAdresi = GuvenliIndirme(cfg["Kasa:IndirmeAdresi"]),
                notlar = "Geri yükleme ve güvenlik günlüğü bütünlüğü iyileştirildi; güncel Windows uygulaması kayıt değişiklikleri için gerekli.",
                windows,
                ios,
            });
        });
        // Son geri yüklemenin anı ve raporu (SistemDurumu) sonda, opsiyonel: eski istemci yok sayar.
        app.MapGet("/api/yedek/durum", (YedekServisi yedek, KasaDbContext db) =>
        {
            var geri = db.SistemDurumu.AsNoTracking().Where(s => s.Id == 1).Select(s => new { s.SonGeriYukleme, s.GeriYuklemeRaporu }).FirstOrDefault();
            return Results.Ok(yedek.Durum() with { SonGeriYukleme = geri?.SonGeriYukleme, GeriYuklemeRaporu = GeriYuklemeIsleyici.RaporuOku(geri?.GeriYuklemeRaporu) });
        }).RequireAuthorization("Editor");
        // Ayrı ve sıkı hız politikası: kullanıcı + IP başına saatte 5 (üretim); 'guvenlik' kovasını tüketmez.
        app.MapPost("/api/yedek", async (KasaDbContext db, YedekServisi yedek, HttpContext http) =>
        {
            // Elle yedek ayrı adla yazılır ve yalnız elle yedeklerle döner; otomatik geçmişi silemez. Sunucudaki kopya belge içeriği
            // taşımaz (belgeler yedek aynasında); indirilen dosya kendi kendine yeterlidir: belgeler/<özet> girdileri eklenerek
            // diske yazılmadan akıtılır. Yedek diskinde yer yoksa hiçbir dosya yazılmaz: 507 ve Türkçe 'hata'.
            string path;
            try
            { path = await yedek.Olustur(db, YedekTuru.Elle, http.RequestAborted); }
            catch (YedekDiskAlaniYetersizException ex) { return Results.Json(new { hata = ex.Message }, statusCode: StatusCodes.Status507InsufficientStorage); }
            return Results.Stream(govde => yedek.KendiKendineYeterliYaz(path, govde, http.RequestAborted), "application/zip", Path.GetFileName(path));
        }).RequireAuthorization("Editor").RequireRateLimiting(HizSinirlari.Yedek);
        app.MapGet("/api/disari-aktar", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? bicim, IslemListeServisi servis) =>
        {
            if (baslangic is null || bitis is null || bitis < baslangic || bitis.Value.DayNumber - baslangic.Value.DayNumber > 3660)
                return Results.BadRequest(new { hata = "En fazla 10 yıllık geçerli bir başlangıç ve bitiş tarihi seçin." });
            if (bicim is not ("csv" or "html" or "xlsx"))
                return Results.BadRequest(new { hata = "CSV, Excel veya yazdırılabilir rapor seçin." });
            var rows = servis.Liste(baslangic, bitis, kanal, null);
            return RaporDosyasi.Olustur(rows, baslangic.Value, bitis.Value, kanal, bicim);
        }).RequireAuthorization("Finans");
        return app;
    }
    private static string? GuvenliIndirme(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
