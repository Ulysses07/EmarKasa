using System.Globalization;
using System.Text;

namespace Kasa.Api;

/// <summary>
/// Türkçe Excel'in çift tıkla doğrudan açtığı CSV: UTF-8 (BOM'lu), ';' ayırıcı, CRLF satır sonu.
/// Sayılar virgül ondalıklı ve binlik ayırıcısız yazılır (Excel sayı olarak okur, toplanabilir),
/// tarihler gg.aa.yyyy. Metin hücreleri gerekirse tırnaklanır ve formül enjeksiyonuna karşı
/// korunur: =, +, -, @, sekme ya da CR ile başlayan metnin başına ' eklenir (Excel onu formül
/// olarak çalıştırmaz).
/// </summary>
public sealed class CsvYazici
{
    public const char Ayirici = ';';
    public const string IcerikTipi = "text/csv; charset=utf-8";

    private readonly StringBuilder _sb = new();

    /// <summary>Hazır (biçimlenmiş) hücrelerden bir satır ekler.</summary>
    public CsvYazici Satir(params string[] hucreler)
    {
        _sb.AppendJoin(Ayirici, hucreler).Append("\r\n");
        return this;
    }

    /// <summary>Başlık satırı: her ad metin hücresi olarak yazılır.</summary>
    public CsvYazici Baslik(params string[] adlar) => Satir(Array.ConvertAll(adlar, a => Metin(a)));

    /// <summary>BOM + UTF-8 içerik.</summary>
    public byte[] Baytlar()
    {
        var govde = Encoding.UTF8.GetBytes(_sb.ToString());
        var bom = Encoding.UTF8.GetPreamble();
        var sonuc = new byte[bom.Length + govde.Length];
        bom.CopyTo(sonuc, 0);
        govde.CopyTo(sonuc, bom.Length);
        return sonuc;
    }

    /// <summary>Metin hücresi: formül enjeksiyonu koruması + gerekiyorsa RFC 4180 tırnaklama.</summary>
    public static string Metin(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        return s.IndexOfAny([Ayirici, '"', '\r', '\n']) >= 0 || s[0] == ' ' || s[^1] == ' '
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    /// <summary>Sayı hücresi: 1234,50 / -1234,50 (binlik ayırıcı yok; Excel sayı olarak okur).</summary>
    public static string Sayi(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Tarih hücresi: 05.09.2026.</summary>
    public static string Tarih(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Dosya adında tarih parçası: 2026-09-05.</summary>
    public static string DosyaTarihi(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Kullanıcı metnini dosya adına uygun ASCII parçaya çevirir: Türkçe harfler sadeleşir
    /// (ç→c, ğ→g, ı/İ→i, ö→o, ş→s, ü→u), küçük harfe iner, harf/rakam dışı her şey '-' olur.
    /// "PERAKENDE Şube" → "perakende-sube". En fazla <paramref name="en"/> karakter.
    /// </summary>
    public static string DosyaAdiParcasi(string? s, int en = 40)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            var a = c switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'I' or 'İ' or 'i' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => char.ToLowerInvariant(c),
            };
            if (a is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(a);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= en) break;
        }
        return sb.ToString().Trim('-');
    }
}
