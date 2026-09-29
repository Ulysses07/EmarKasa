using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kasa.App.Core;

/// <summary>Para girişi metni ↔ decimal. Kural web ui-core.js cents() ile aynıdır: gruplanmamış rakam,
/// ondalık ayırıcı nokta ya da virgül, en çok iki ondalık. '25.000' / '1.234,56' gibi gruplanmış yazım
/// tahmin edilmez, reddedilir. Web'den farkı yalnız temizlik: boşluk, ₺ ve 'TL' atılır, boş metin 0 sayılır,
/// baştaki '-' kabul edilir (eksiye izin vermeyen alanları VM kuralları reddeder).</summary>
public static partial class ParaAyristirici
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Sunucudaki GirdiDogrulama.EnBuyukTutar ile aynı üst sınır.</summary>
    public const decimal EnBuyuk = 999_999_999_999.99m;

    /// <summary>Geçersiz metnin VM'deki karşılığı. Sunucu aralığının çok dışında olduğundan GirdiDogrulama.Para
    /// bunu her alanda reddeder; VM'ler ise API'yi hiç çağırmadan GecersizMesaji gösterir.</summary>
    public const decimal Gecersiz = -999_999_999_999_999m;

    public const string BicimHatasi = "Tutarı binlik ayırıcı kullanmadan, en çok iki ondalıkla yazın (ör. 25000 veya 25000,50).";
    public const string SinirHatasi = "Tutar izin verilen sınırı aşıyor.";
    public const string GecersizMesaji = "Tutar alanlarından biri geçersiz: binlik ayırıcı kullanmadan, en çok iki ondalıkla yazın.";
    public const string GecersizGosterim = "Tutar geçersiz";

    [GeneratedRegex("^-?[0-9]+(?:[.,][0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Kalip();

    public static bool Coz(string? metin, out decimal tutar, out string? hata)
    {
        tutar = 0m; hata = null;
        var temiz = Temizle(metin);
        if (temiz.Length == 0) return true;
        if (!Kalip().IsMatch(temiz)) { hata = BicimHatasi; return false; }
        if (!decimal.TryParse(temiz.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            || Math.Abs(d) > EnBuyuk) { hata = SinirHatasi; return false; }
        tutar = d; return true;
    }

    private static string Temizle(string? metin)
    {
        if (string.IsNullOrEmpty(metin)) return "";
        var sb = new StringBuilder(metin.Length);
        foreach (var c in metin) if (!char.IsWhiteSpace(c) && c != '₺') sb.Append(c);
        var s = sb.ToString();
        return s.EndsWith("TL", StringComparison.OrdinalIgnoreCase) ? s[..^2] : s;
    }

    /// <summary>Girişe yazılacak metin: gruplamasız tr-TR ('1500,5'), 0 için boş. İkiden fazla ondalık
    /// yuvarlanmaz; öyle bir değer geri ayrıştırılırken hataya düşer, sessizce başka tutara dönmez.</summary>
    public static string Bicimle(decimal tutar) => tutar == 0m ? "" : tutar.ToString("0.############################", Tr);

    public static bool GecerliMi(decimal tutar) => tutar != Gecersiz;
    public static bool HepsiGecerli(params decimal[] tutarlar) => tutarlar.All(GecerliMi);

    /// <summary>Özet/toplam gösterimi: geçersiz girdi sayı olarak gösterilmez.</summary>
    public static string Goster(decimal tutar) => GecerliMi(tutar) ? Bicim.Tl(tutar) : GecersizGosterim;

    /// <summary>Gövde/önizleme yolları için: geçersiz tutar varsa API çağrılmadan <see cref="DogrulamaHatasi"/> verir
    /// (TemelViewModel bu mesajı olduğu gibi gösterir).</summary>
    public static void Dogrula(params decimal[] tutarlar)
    {
        if (!HepsiGecerli(tutarlar)) throw new DogrulamaHatasi(GecersizMesaji);
    }
}
