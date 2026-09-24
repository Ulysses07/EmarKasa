using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Ek (fiş/fatura fotoğrafı, PDF) istemci kuralları — sunucudakilerin aynası. Sunucu dosyanın
/// içeriğini (imzasını) ayrıca doğrular; burada kullanıcıya erken ve anlaşılır uyarı verilir.
/// </summary>
public static class EkKurallari
{
    public const long EnFazlaBoyut = 10L * 1024 * 1024;
    public const int IslemBasinaEnFazla = 10;
    public static readonly IReadOnlyList<string> IzinliUzantilar = [".jpg", ".jpeg", ".png", ".webp", ".heic", ".pdf"];

    public const string TurMesaji = "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.";
    public static string BoyutMesaji(string ad) => $"'{ad}' 10 MB'tan büyük; eklenemez.";
    public static string BosMesaji(string ad) => $"'{ad}' boş; eklenemez.";
    public static readonly string SayiMesaji = $"Bir işleme en fazla {IslemBasinaEnFazla} ek eklenebilir.";

    /// <summary>Dosya eklenebilir mi; değilse Türkçe neden.</summary>
    public static string? Hata(SecilenDosya d)
    {
        var uz = Path.GetExtension(d.Ad).ToLowerInvariant();
        if (uz.Length > 0 && !IzinliUzantilar.Contains(uz)) return TurMesaji;
        if (d.Boyut > EnFazlaBoyut) return BoyutMesaji(d.Ad);
        if (d.Boyut == 0 || d.Icerik.Length == 0) return BosMesaji(d.Ad);
        return null;
    }

    /// <summary>"350 KB", "1,2 MB".</summary>
    public static string BoyutMetni(long bayt) => bayt < 1024 * 1024
        ? $"{Math.Max(1, (bayt + 1023) / 1024)} KB"
        : (bayt / (1024m * 1024m)).ToString("0.#", Kultur.Turkce) + " MB";
}

/// <summary>Belge türlerinin Türkçe adları (çipler, listeler).</summary>
public static class BelgeMetin
{
    public const string Belirtilmedi = "Belirtilmedi";

    public static readonly IReadOnlyList<BelgeTuru?> Turler =
        [null, BelgeTuru.EFatura, BelgeTuru.EArsiv, BelgeTuru.Fis, BelgeTuru.Makbuz, BelgeTuru.Belgesiz];

    public static string TurAdi(BelgeTuru? t) => t switch
    {
        BelgeTuru.EFatura => "e-Fatura",
        BelgeTuru.EArsiv => "e-Arşiv",
        BelgeTuru.Fis => "Fiş",
        BelgeTuru.Makbuz => "Makbuz",
        BelgeTuru.Belgesiz => "Belgesiz",
        _ => Belirtilmedi,
    };

    public static BelgeTuru? TurDegeri(string ad) => Turler.FirstOrDefault(t => TurAdi(t) == ad);

    /// <summary>Listelerde kısa özet: "e-Fatura · F-12 · fatura bekleniyor" (boşsa "").</summary>
    public static string Ozet(BelgeBilgisi b)
    {
        var parcalar = new List<string>();
        if (b.Tur is not null) parcalar.Add(TurAdi(b.Tur));
        if (!string.IsNullOrWhiteSpace(b.No)) parcalar.Add(b.No!);
        if (b.FaturaBekleniyor) parcalar.Add("fatura bekleniyor");
        return string.Join(" · ", parcalar);
    }
}
