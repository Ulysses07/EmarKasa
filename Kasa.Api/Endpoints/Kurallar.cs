using Kasa.Api.Data;

namespace Kasa.Api.Endpoints;

// Program.cs'teki doğrulama ve yazma yardımcıları (üst düzey programın yerel fonksiyonları)
// uç dosyalarına bu temsilcilerle verilir: kurallar tek yerde kalır, iki kopya oluşmaz.

/// <summary>Program.Yaz: tek transaction + kısıt ihlalinde anlaşılır 409.</summary>
public delegate IResult YazIslemi(KasaDbContext db, string cakismaMesaji, Func<IResult> islem);

/// <summary>Program.CekHatasi: POST/PUT /api/cekler doğrulaması (metinleri kırpar).</summary>
public delegate string? CekDogrulama(KasaDbContext db, CekEntity e);

/// <summary>Program.TekrarlayanAyHatasi: onay/atla ayı başlangıçtan önce ya da ileri bir ay olamaz.</summary>
public delegate string? TekrarlayanAyDogrulama(TekrarlayanGiderEntity t, DateOnly ay, DateOnly bugun);

/// <summary>Program.KayitliKalem: kayıtlı gider kalemi yazımı (Türkçe büyük/küçük harf duyarsız), yoksa null.</summary>
public delegate string? KalemBulucu(KasaDbContext db, string ad);

internal static class Yanit
{
    public static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });
    public static IResult Cakisma(string mesaj) => Results.Conflict(new { hata = mesaj });
}
