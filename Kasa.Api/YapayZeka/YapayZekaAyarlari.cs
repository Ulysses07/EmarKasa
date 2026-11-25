using System.Globalization;

namespace Kasa.Api.YapayZeka;

/// <summary>
/// Yapay zekâ ayarları (YapayZeka:Etkin, YapayZeka:Anahtar, YapayZeka:AylikSinirUsd; docs/deploy/yapay-zeka.md). Kapalıysa ya da ayarı eksikse
/// hiçbir dış bağlantı kurulmaz. Model ve fiyatı kodda sabittir: fiyatı bilinmeyen modelle harcama sınırı tutulamaz.
/// </summary>
public sealed class YapayZekaAyarlari
{
    public const string Model = "claude-haiku-5-5";
    /// <summary>Bir çağrıda en çok farklı satır ve geçmiş örnek.</summary>
    public const int EnCokSatir = 60, EnCokOrnek = 60;
    /// <summary>Yanıtın (düşünme dahil) jeton tavanı; ayrılan maliyet buna göre hesaplanır.</summary>
    public const int EnCokCiktiJetonu = 16_000;
    /// <summary>İstek metninin karakter tavanı: en kötü durumda da 100.000 jeton fiyat eşiğinin altında kalır.</summary>
    public const int EnCokGirdiKarakteri = 60_000;
    private const decimal VarsayilanSinir = 2m, EnYuksekSinir = 100m;

    public bool Etkin { get; }
    public string? Anahtar { get; }
    public decimal AylikSinirUsd { get; }
    /// <summary>Kapalıysa nedeni (kullanıcıya gösterilir); açıksa null.</summary>
    public string? KapaliNedeni { get; }

    public YapayZekaAyarlari(IConfiguration yapilandirma)
    {
        var b = yapilandirma.GetSection("YapayZeka");
        Etkin = string.Equals(b["Etkin"]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        Anahtar = b["Anahtar"]?.Trim() is { Length: > 0 } anahtar ? anahtar : null;
        var sinir = b["AylikSinirUsd"]?.Trim();
        var gecerli = string.IsNullOrEmpty(sinir) || decimal.TryParse(sinir, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
        AylikSinirUsd = string.IsNullOrEmpty(sinir) || !gecerli ? VarsayilanSinir : decimal.Parse(sinir, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        KapaliNedeni = !Etkin ? "Yapay zekâ sunucuda kapalı."
            : Anahtar is null ? "Yapay zekâ açık ama sunucuda anahtarı yok (YapayZeka:Anahtar)."
            : !gecerli || AylikSinirUsd is <= 0 or > EnYuksekSinir ? $"Aylık sınır (YapayZeka:AylikSinirUsd) 0 ile {EnYuksekSinir:0} $ arasında bir sayı olmalı; ondalık ayıracı nokta."
            : null;
    }

    /// <summary>Haiku 5.5 fiyatı, $ / milyon jeton (25.11.2026, platform.claude.com/docs/en/about-claude/pricing): 100.000 jetona kadar istek ve üstü.</summary>
    private static (decimal Girdi, decimal Cikti, decimal OnbellekYazma, decimal OnbellekOkuma) Fiyat(long girdiToplami) =>
        girdiToplami <= 100_000 ? (0.10m, 0.50m, 0.125m, 0.01m) : (0.50m, 2.50m, 0.625m, 0.05m);

    internal static decimal Maliyet(long girdi, long cikti, long onbellekYazma, long onbellekOkuma)
    {
        var f = Fiyat(girdi + onbellekYazma + onbellekOkuma);
        return decimal.Round((girdi * f.Girdi + cikti * f.Cikti + onbellekYazma * f.OnbellekYazma + onbellekOkuma * f.OnbellekOkuma) / 1_000_000m, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>Çağrıdan önce ayrılan en yüksek maliyet: jeton başına en az 1 karakter varsayılır, girdi önbelleğe yazma fiyatıyla sayılır.</summary>
    internal static decimal EnYuksekMaliyet(int girdiKarakteri) => Maliyet(0, EnCokCiktiJetonu, Math.Min(girdiKarakteri + 2_000, 100_000), 0);
}
