using Kasa.Api.Data;
using Kasa.Api.Servisler;

namespace Kasa.Api;

public static class YonetimEndpoints
{
    public static WebApplication MapYonetimEndpoints(this WebApplication app)
    {
        app.MapGet("/api/surum", (IConfiguration cfg) => Results.Ok(new
        {
            surum = "2.3.0", minimumIstemci = "2.3.0",
            indirmeAdresi = GuvenliIndirme(cfg["Kasa:IndirmeAdresi"]),
            notlar = "Kart ekstresi ve banka hesap hareketi PDF yükleme, seçilen hareketleri önizleyerek işleme ve tekrar kayıt kontrolü."
        }));
        app.MapGet("/api/yedek/durum", (YedekServisi yedek) => Results.Ok(yedek.Durum())).RequireAuthorization("Editor");
        app.MapPost("/api/yedek", async (KasaDbContext db, YedekServisi yedek, HttpContext http) =>
        {
            var path = await yedek.Olustur(db, http.RequestAborted);
            return Results.File(path, "application/zip", Path.GetFileName(path));
        }).RequireAuthorization("Editor").RequireRateLimiting("guvenlik");
        app.MapGet("/api/disari-aktar", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? bicim, IslemListeServisi servis) =>
        {
            if (baslangic is null || bitis is null || bitis < baslangic || bitis.Value.DayNumber - baslangic.Value.DayNumber > 3660)
                return Results.BadRequest(new { hata = "En fazla 10 yıllık geçerli bir başlangıç ve bitiş tarihi seçin." });
            if (bicim is not ("csv" or "html" or "xlsx")) return Results.BadRequest(new { hata = "CSV, Excel veya yazdırılabilir rapor seçin." });
            var rows = servis.Liste(baslangic, bitis, kanal, null);
            return RaporDosyasi.Olustur(rows, baslangic.Value, bitis.Value, kanal, bicim);
        }).RequireAuthorization("Finans");
        return app;
    }
    private static string? GuvenliIndirme(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
