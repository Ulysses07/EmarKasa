using System.Globalization;
using System.Text;

namespace Kasa.App.Core;

/// <summary>
/// "Bunu mu demek istediniz?" için cari adı benzerliği. Adlar Türkçe kurallarla normalleştirilir
/// (büyük/küçük harf, İ/ı, noktalı harfler → Latin karşılığı, noktalama, "Ltd. Şti.", "A.Ş.", "Tic.",
/// "San." gibi şirket ekleri) ve kalan metin Damerau-Levenshtein mesafesiyle karşılaştırılır.
/// </summary>
public static class CariBenzerlik
{
    /// <summary>Karşılaştırmada atılan şirket türü/ek kelimeleri (normalleştirilmiş).</summary>
    private static readonly HashSet<string> Ekler = new(StringComparer.Ordinal)
    {
        "ltd", "sti", "ltdsti", "limited", "sirketi", "sirket", "as", "anonim", "a", "s",
        "tic", "ticaret", "san", "sanayi", "ve", "vs", "koll", "kolektif", "co", "inc",
    };

    /// <summary>Karşılaştırma için normal biçim: küçük harf, Türkçe harfler Latin, noktalama boşluk, ekler atılmış.</summary>
    public static string Normallestir(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad)) return "";
        var kucuk = ad.Trim().ToLower(Kultur.Turkce);
        var sb = new StringBuilder(kucuk.Length);
        foreach (var c in kucuk.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // i̇, â → a
            sb.Append(c switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ç' => 'c',
                'ö' => 'o',
                'ü' => 'u',
                _ when char.IsLetterOrDigit(c) => c,
                _ => ' ',
            });
        }
        var kelimeler = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var anlamli = kelimeler.Where(k => !Ekler.Contains(k)).ToList();
        IEnumerable<string> secilen = anlamli.Count > 0 ? anlamli : kelimeler;
        return string.Join(' ', secilen);
    }

    /// <summary>Damerau-Levenshtein (bitişik harf yer değiştirmesi 1 sayılır; optimal hizalama).</summary>
    public static int Mesafe(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var maliyet = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + maliyet);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>Normalleştirilmiş uzunluğa göre izin verilen en büyük mesafe.</summary>
    public static int Esik(int uzunluk) => uzunluk switch
    {
        <= 3 => 0,
        <= 5 => 1,
        <= 10 => 2,
        _ => 3,
    };

    /// <summary>
    /// <paramref name="ad"/>'a benzeyen kayıtlı adlar (en benzer önce, en fazla <paramref name="enFazla"/>).
    /// Birebir aynı yazılan (Türkçe harf duyarsız) ad listeye girmez: o zaten kayıtlıdır.
    /// </summary>
    public static IReadOnlyList<string> Benzerler(string? ad, IEnumerable<string> adaylar, int enFazla = 3)
    {
        var n = Normallestir(ad);
        if (n.Length == 0) return [];
        var nBitisik = n.Replace(" ", "");
        var sonuc = new List<(string Ad, int Puan)>();
        foreach (var aday in adaylar.Distinct())
        {
            if (string.Compare(aday, ad?.Trim(), Kultur.Turkce, CompareOptions.IgnoreCase) == 0) continue;
            var m = Normallestir(aday);
            if (m.Length == 0) continue;
            var mBitisik = m.Replace(" ", "");
            int? puan = null;
            if (mBitisik == nBitisik) puan = 0;                                // ekler/noktalama/harf farkı
            else
            {
                var mesafe = Mesafe(nBitisik, mBitisik);
                if (mesafe <= Esik(Math.Min(nBitisik.Length, mBitisik.Length))) puan = mesafe;
                else if (Math.Min(nBitisik.Length, mBitisik.Length) >= 4
                         && (m.Split(' ').Contains(n) || n.Split(' ').Contains(m)))
                    puan = 4;                                                  // biri diğerinin tam kelimesi
            }
            if (puan is { } p) sonuc.Add((aday, p));
        }
        return sonuc.OrderBy(s => s.Puan).ThenBy(s => s.Ad, StringComparer.Create(Kultur.Turkce, true))
            .Take(enFazla).Select(s => s.Ad).ToList();
    }
}
