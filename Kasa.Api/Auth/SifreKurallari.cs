namespace Kasa.Api.Auth;

/// <summary>Editör ve izleyici için ortak yeni şifre kuralı. Yalnız belirlerken/değiştirirken uygulanır;
/// mevcut (kuraldan önce kaydedilmiş kısa) şifrelerle giriş sürer. Web ve masaüstü aynı ön kontrolü yapar.</summary>
public static class SifreKurallari
{
    public const int EnAz = 12;
    public const int EnCok = 1024;

    public static bool Gecerli(string? sifre) => !string.IsNullOrWhiteSpace(sifre) && sifre.Length is >= EnAz and <= EnCok;

    public static IResult? YeniSifreHatasi(string? sifre, string alan, string etiket)
        => Gecerli(sifre)
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]> { [alan] = [$"{etiket} {EnAz}–{EnCok} karakter olmalıdır."] });
}

/// <summary>
/// Kuraldan önce konmuş kısa izleyici şifresi hash'ten anlaşılamaz; başarılı izleyici girişinde fark edilir, bir
/// kez uyarı loglanır ve Ayarlar'da editöre gösterilir. Bellekte tutulur: süreç yeniden başlayınca sonraki
/// izleyici girişine kadar bilinmez. Şifre değişince hash değiştiği için işaret kendiliğinden kalkar.
/// </summary>
public sealed class IzleyiciSifreDurumu(ILogger<IzleyiciSifreDurumu> log)
{
    private volatile string? _kisaHash;

    public void GirisYapildi(string hash, string sifre)
    {
        if (SifreKurallari.Gecerli(sifre) || _kisaHash == hash) return;
        _kisaHash = hash;
        log.LogWarning("İzleyici şifresi {EnAz}–{EnCok} karakter kuralına uymuyor (kuraldan önce belirlenmiş). Ayarlar'dan kurala uygun yeni bir izleyici şifresi belirleyin.",
            SifreKurallari.EnAz, SifreKurallari.EnCok);
    }

    /// <summary>Kayıtlı hash, kurala uymadığı girişte görülen hash ile aynıysa true.</summary>
    public bool KisaMi(string? kayitliHash) => kayitliHash is not null && _kisaHash == kayitliHash;
}
