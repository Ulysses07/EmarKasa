using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket B (raporlar ve ay kapanışı) kurulumu: Program.cs'ye yalnız iki satır eklenir —
/// servisler için <see cref="AddRaporVeAyKapanisi"/>, uç noktalar için <see cref="MapRaporVeAyKapanisi"/>.
/// Tüm uç noktalar korumalı /api grubundadır (oturum zorunlu); yazmalar "Editor" politikasıyla.
/// </summary>
public static class RaporVeAyKapanisiKurulumu
{
    public static IServiceCollection AddRaporVeAyKapanisi(this IServiceCollection services)
    {
        services.AddScoped<RaporServisi>();
        services.AddHttpClient<TcmbKurServisi>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EmarKasa/1.0 (+https://kasa.emarglobal.com)");
        });
        return services;
    }

    public static RouteGroupBuilder MapRaporVeAyKapanisi(this RouteGroupBuilder api)
    {
        // Kilitli aya yazma (işlem, gelen, çek, …) hangi uç noktadan gelirse gelsin anlaşılır bir 409 olur.
        api.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (AyKilitliHatasi ex) { return Results.Conflict(new { hata = ex.Message }); }
        });
        api.MapKasaDokumu();
        api.MapAyKapanisi();
        api.MapAylikYazdir();
        api.MapKurlarVeGrafik();
        api.MapHedefButce();
        api.MapCariOzeti();
        api.MapDisaAktarmaEk();
        return api;
    }
}

/// <summary>Paket B uç noktalarının ortak doğrulama ve yazma yardımcıları (Program.cs'dekilerle aynı kurallar).</summary>
internal static class UcNokta
{
    public static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

    public static string? AyHatasi(int yil, int ay)
    {
        if (ay is < 1 or > 12) return "Ay 1 ile 12 arasında olmalı.";
        if (yil is < 2000 or > 2100) return "Yıl 2000 ile 2100 arasında olmalı.";
        return null;
    }

    public static string? TarihHatasi(DateOnly t)
        => t < new DateOnly(2000, 1, 1) || t > new DateOnly(2100, 12, 31) ? "Tarih 2000 ile 2100 arasında olmalı." : null;

    /// <summary>Tutar: en fazla <paramref name="ondalik"/> ondalık, mutlak değeri 100 milyarı geçmez.</summary>
    public static string? TutarHatasi(decimal tutar, string alan, bool negatifOlabilir = false, int ondalik = 2)
    {
        if (!negatifOlabilir && tutar < 0) return $"{alan} negatif olamaz.";
        if (Math.Abs(tutar) > 100_000_000_000m) return $"{alan} çok büyük (en fazla 100.000.000.000).";
        if (decimal.Round(tutar, ondalik) != tutar) return $"{alan} en fazla {ondalik} ondalık basamak içerebilir.";
        return null;
    }

    /// <summary>
    /// Yazmayı tek transaction'da çalıştırır; kısıt ihlali (tekil index, FK) 409 olur. Kilitli ay
    /// hatası grup filtresinde 409'a çevrilir.
    /// </summary>
    public static IResult Yaz(KasaDbContext db, string cakismaMesaji, Func<IResult> islem)
    {
        try
        {
            using var tx = db.Database.BeginTransaction();
            var sonuc = islem();
            tx.Commit();
            return sonuc;
        }
        catch (Exception ex) when (KisitIhlali(ex))
        {
            return Results.Conflict(new { hata = cakismaMesaji });
        }
    }

    private static bool KisitIhlali(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SqliteException { SqliteErrorCode: 19 }) return true;
        return false;
    }

    /// <summary>Dosya indirme (Content-Disposition: attachment; ad ASCII).</summary>
    public static IResult Dosya(byte[] icerik, string icerikTipi, string dosyaAdi) => Results.File(icerik, icerikTipi, dosyaAdi);
}
