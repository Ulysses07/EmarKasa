namespace Kasa.Api.Auth;

/// <summary>Editör ve izleyici için ortak yeni şifre kuralı. Yalnız belirlerken/değiştirirken uygulanır;
/// mevcut (kuraldan önce kaydedilmiş kısa) şifrelerle giriş sürer. Web ve masaüstü aynı ön kontrolü yapar.</summary>
public static class SifreKurallari
{
    public const int EnAz = 12;
    public const int EnCok = 1024;

    public static IResult? YeniSifreHatasi(string? sifre, string alan, string etiket)
        => string.IsNullOrWhiteSpace(sifre) || sifre.Length is < EnAz or > EnCok
            ? Results.ValidationProblem(new Dictionary<string, string[]> { [alan] = [$"{etiket} {EnAz}–{EnCok} karakter olmalıdır."] })
            : null;
}
