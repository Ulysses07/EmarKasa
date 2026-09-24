using Kasa.Api.Data;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Endpoints/ altındaki uç noktaların ortak yardımcıları. Program.cs'teki yerel fonksiyonların
/// (Hata, Yaz, TutarHatasi, TarihHatasi, AyHatasi) aynı kurallı kopyalarıdır; yerel
/// fonksiyonlar başka dosyadan çağrılamadığı için burada tekrar edilir.
/// </summary>
internal static class UcNokta
{
    public static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

    /// <summary>
    /// Yazmayı tek transaction'da çalıştırır; kısıt ihlali (tekil index, FK) 409 döner. Kilitli ay
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

    public static bool KisitIhlali(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SqliteException { SqliteErrorCode: 19 }) return true; // SQLITE_CONSTRAINT
        return false;
    }

    /// <summary>Tutar: en fazla <paramref name="ondalik"/> ondalık, mutlak değeri 100 milyarı geçmez.</summary>
    public static string? TutarHatasi(decimal tutar, string alan, bool negatifOlabilir = false, int ondalik = 2)
    {
        if (!negatifOlabilir && tutar < 0) return $"{alan} negatif olamaz.";
        if (Math.Abs(tutar) > 100_000_000_000m) return $"{alan} çok büyük (en fazla 100.000.000.000).";
        if (decimal.Round(tutar, ondalik) != tutar) return $"{alan} en fazla {ondalik} ondalık basamak içerebilir.";
        return null;
    }

    public static string? TarihHatasi(DateOnly t)
        => t < new DateOnly(2000, 1, 1) || t > new DateOnly(2100, 12, 31) ? "Tarih 2000 ile 2100 arasında olmalı." : null;

    public static string? AyHatasi(int yil, int ay)
    {
        if (ay is < 1 or > 12) return "Ay 1 ile 12 arasında olmalı.";
        if (yil is < 2000 or > 2100) return "Yıl 2000 ile 2100 arasında olmalı.";
        return null;
    }

    /// <summary>Dosya indirme (Content-Disposition: attachment; ad ASCII).</summary>
    public static IResult Dosya(byte[] icerik, string icerikTipi, string dosyaAdi) => Results.File(icerik, icerikTipi, dosyaAdi);
}
