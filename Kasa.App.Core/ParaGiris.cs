using System.Globalization;

namespace Kasa.App.Core;

/// <summary>Para giriş kutusunun ayrıştırma sonucu.</summary>
/// <param name="Gecerli">Metin geçerli bir tutar mı.</param>
/// <param name="Tutar">Geçerliyse tutar (boş giriş = 0).</param>
/// <param name="Hata">Geçersizse kullanıcıya gösterilecek kısa açıklama.</param>
public readonly record struct ParaGirisSonucu(bool Gecerli, decimal Tutar, string? Hata)
{
    public static ParaGirisSonucu Tamam(decimal t) => new(true, t, null);
    public static ParaGirisSonucu Gecersiz(string hata) => new(false, 0m, hata);
}

/// <summary>
/// Kullanıcının yazdığı TL tutarını Türkçe kurallarla ayrıştırır (saf fonksiyon; MAUI
/// dönüştürücüsü bunu çağırır).
/// <list type="bullet">
/// <item>Virgül ondalık ayırıcıdır: "12,5" → 12,5; "1.500,50" → 1500,5.</item>
/// <item>Yalnız nokta varsa ve noktadan sonraki her grup tam 3 haneyse nokta binlik ayırıcıdır:
/// "1.500" → 1500, "1.500.000" → 1500000.</item>
/// <item>Tek nokta ve ardından 1–2 hane ondalıktır: "12.5" → 12,5; "12.50" → 12,5.</item>
/// <item>Negatif, 2'den fazla ondalık, belirsiz/bozuk gruplama ve harf içeren giriş geçersizdir;
/// asla sessizce 0'a dönmez.</item>
/// <item>Boş giriş 0'dır (alan boş bırakılabilir).</item>
/// </list>
/// </summary>
public static class ParaGiris
{
    /// <summary>Kabul edilen en büyük tutar (motorun kuruş hesabında taşmayı önler).</summary>
    public const decimal EnBuyuk = 999_999_999_999.99m;

    public const string HataGecersiz = "Geçersiz tutar. Örnek: 1.500 veya 1.500,50";
    public const string HataNegatif = "Tutar negatif olamaz.";
    public const string HataKurus = "En fazla 2 ondalık hane (kuruş) girilebilir.";
    public const string HataCokBuyuk = "Tutar çok büyük.";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ParaGirisSonucu Ayristir(string? metin)
    {
        var s = Temizle(metin);
        if (s.Length == 0) return ParaGirisSonucu.Tamam(0m);

        if (s.StartsWith('-') || s.StartsWith('−') || (s.StartsWith('(') && s.EndsWith(')')))
            return ParaGirisSonucu.Gecersiz(HataNegatif);
        if (s.StartsWith('+')) s = s[1..];
        if (s.Length == 0) return ParaGirisSonucu.Gecersiz(HataGecersiz);

        foreach (var c in s)
            if (!(c is >= '0' and <= '9' || c == '.' || c == ','))
                return ParaGirisSonucu.Gecersiz(HataGecersiz);

        string tamKisim, ondalik;
        var virgul = s.IndexOf(',');
        if (virgul >= 0)
        {
            if (s.IndexOf(',', virgul + 1) >= 0) return ParaGirisSonucu.Gecersiz(HataGecersiz); // "1,234,567"
            tamKisim = s[..virgul];
            ondalik = s[(virgul + 1)..];
            if (ondalik.Contains('.')) return ParaGirisSonucu.Gecersiz(HataGecersiz);            // "1,234.56"
            // "12," (yazarken ara durum) → ondalik boş → 12
            if (!BinlikGrubuGecerli(tamKisim, out tamKisim)) return ParaGirisSonucu.Gecersiz(HataGecersiz);
        }
        else if (s.Contains('.'))
        {
            var gruplar = s.Split('.');
            if (gruplar.Length >= 2 && gruplar[0].Length is >= 1 and <= 3 && gruplar.Skip(1).All(g => g.Length == 3))
            {
                tamKisim = string.Concat(gruplar);   // "1.500" / "1.500.000" → binlik
                ondalik = "";
            }
            else if (gruplar.Length == 2 && gruplar[1].Length is >= 1 and <= 2)
            {
                tamKisim = gruplar[0];               // "12.5" / "12.50" / ".5" → ondalık
                ondalik = gruplar[1];
            }
            else if (gruplar.Length == 2 && gruplar[1].Length == 0 && gruplar[0].Length > 0)
            {
                tamKisim = gruplar[0];               // "12." (yazarken ara durum) → 12
                ondalik = "";
            }
            else if (gruplar.Length == 2 && gruplar[1].Length > 3 && gruplar[1].All(char.IsAsciiDigit))
                return ParaGirisSonucu.Gecersiz(HataKurus);
            else
                return ParaGirisSonucu.Gecersiz(HataGecersiz);
        }
        else
        {
            tamKisim = s;
            ondalik = "";
        }

        if (ondalik.Length > 2) return ParaGirisSonucu.Gecersiz(HataKurus);
        if (tamKisim.Length == 0 && ondalik.Length == 0) return ParaGirisSonucu.Gecersiz(HataGecersiz);
        if (tamKisim.TrimStart('0').Length > 12) return ParaGirisSonucu.Gecersiz(HataCokBuyuk);

        var normal = (tamKisim.Length == 0 ? "0" : tamKisim) + (ondalik.Length > 0 ? "." + ondalik : "");
        if (!decimal.TryParse(normal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            return ParaGirisSonucu.Gecersiz(HataGecersiz);
        if (d > EnBuyuk) return ParaGirisSonucu.Gecersiz(HataCokBuyuk);
        return ParaGirisSonucu.Tamam(d);
    }

    /// <summary>
    /// Eksi tutara izin veren ayrıştırma; yalnız alacak bakiyesi olabilen alanlar içindir (kart ekstresi:
    /// fazla ödenen kart "-250,00" gösterir). Baştaki "-" ya da "−" atılır, kalanı <see cref="Ayristir"/>
    /// kurallarıyla okunur. Tek başına "-" ya da iki işaret geçersizdir.
    /// </summary>
    public static ParaGirisSonucu AyristirIsaretli(string? metin)
    {
        var s = Temizle(metin);
        if (!(s.StartsWith('-') || s.StartsWith('−'))) return Ayristir(s);
        var kalan = s[1..];
        if (kalan.Length == 0 || kalan[0] is '-' or '−' or '+' or '(') return ParaGirisSonucu.Gecersiz(HataGecersiz);
        var r = Ayristir(kalan);
        return r.Gecerli && r.Tutar != 0m ? ParaGirisSonucu.Tamam(-r.Tutar) : r;   // "-0" eksi sıfır olmasın
    }

    /// <summary>Tutarı giriş kutusunda gösterilecek metne çevirir (0 = boş).</summary>
    public static string Bicimle(decimal tutar) => tutar == 0m ? string.Empty : tutar.ToString("0.##", Tr);

    /// <summary>Boşlukları (NBSP dahil), ₺ ve "TL" son/ön ekini kaldırır.</summary>
    private static string Temizle(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return string.Empty;
        var s = new string(metin.Where(c => !char.IsWhiteSpace(c) && c != ' ' && c != ' ').ToArray());
        s = s.Replace("₺", "");
        if (s.EndsWith("TL", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
        else if (s.StartsWith("TL", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s;
    }

    /// <summary>
    /// Virgülden önceki kısımda nokta varsa binlik gruplamayı doğrular ("1.234" geçerli,
    /// "12.34" / "1.2345" geçersiz) ve noktaları kaldırır.
    /// </summary>
    private static bool BinlikGrubuGecerli(string tam, out string temiz)
    {
        temiz = tam;
        if (!tam.Contains('.')) return true;
        var gruplar = tam.Split('.');
        if (gruplar[0].Length is < 1 or > 3 || gruplar.Skip(1).Any(g => g.Length != 3)) return false;
        temiz = string.Concat(gruplar);
        return true;
    }
}
