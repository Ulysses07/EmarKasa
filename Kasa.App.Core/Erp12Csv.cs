using System.Globalization;
using System.Text;

namespace Kasa.App.Core;

/// <summary>Okunan CSV: başlıklar, veri satırları ve tespit edilen biçim.</summary>
public sealed record CsvTablo(IReadOnlyList<string> Basliklar, IReadOnlyList<IReadOnlyList<string>> Satirlar,
    char Ayirici, string Kodlama, bool BaslikVar);

/// <summary>
/// ERP12 (ya da başka bir muhasebe programı) dışa aktarımı CSV okuyucusu.
/// <list type="bullet">
/// <item>Kodlama: BOM varsa ona göre (UTF-8 / UTF-16); yoksa geçerli UTF-8 ise UTF-8, değilse
///       Windows-1254 (Türkçe Windows/Excel varsayılanı).</item>
/// <item>Ayırıcı: ; , sekme | arasından ilk satırlarda (tırnak dışında) en tutarlı çıkan; eşitlikte ';'.</item>
/// <item>RFC 4180 tırnakları (çift tırnak kaçışı, hücre içi satır sonu) desteklenir; boş satırlar atlanır.</item>
/// <item>İlk satırda hiç tarih/tutar yokken ikinci satırda varsa ilk satır başlıktır; sütun sayısı
///       tutmayan baştaki açıklama satırları atlanır.</item>
/// </list>
/// </summary>
public static class Erp12Csv
{
    public const string BosDosyaMesaji = "Dosya boş ya da okunamadı.";
    private static readonly char[] Adaylar = [';', ',', '\t', '|'];

    public static CsvTablo Oku(byte[] icerik)
    {
        var (metin, kodlama) = Coz(icerik);
        var ayirici = AyiriciBul(metin);
        var satirlar = Ayristir(metin, ayirici);
        if (satirlar.Count == 0) throw new DogrulamaHatasi(BosDosyaMesaji);

        // Baştaki başlık/açıklama satırları (ör. "ERP12 Tediye Listesi") sütun sayısı tutmadığı için atılır.
        var sutunSayisi = satirlar.GroupBy(s => s.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var bas = satirlar.Take(10).ToList().FindIndex(s => s.Count >= sutunSayisi);
        if (bas > 0) satirlar = satirlar.Skip(bas).ToList();
        sutunSayisi = satirlar.Max(s => s.Count);

        // İlk satırda hiç tarih/tutar yokken ikincide varsa ilk satır başlıktır.
        static bool Deger(string h) => TarihOku(h) is not null || TutarOku(h) is not null;
        var ilk = satirlar[0];
        var baslikVar = satirlar.Count > 1 && !ilk.Any(Deger) && satirlar[1].Any(Deger);
        IReadOnlyList<string> basliklar = baslikVar
            ? Enumerable.Range(0, sutunSayisi).Select(i => i < ilk.Count && ilk[i].Length > 0 ? ilk[i] : $"Sütun {i + 1}").ToList()
            : Enumerable.Range(0, sutunSayisi).Select(i => $"Sütun {i + 1}").ToList();
        basliklar = Tekillestir(basliklar);
        var veri = (baslikVar ? satirlar.Skip(1) : satirlar).Select(s => (IReadOnlyList<string>)s).ToList();
        return new CsvTablo(basliklar, veri, ayirici, kodlama, baslikVar);
    }

    /// <summary>Aynı adlı başlıklara " (2)" eklenir (eşleştirme adla saklandığı için).</summary>
    private static List<string> Tekillestir(IReadOnlyList<string> adlar)
    {
        var sonuc = new List<string>();
        foreach (var a in adlar)
        {
            var ad = a;
            for (var n = 2; sonuc.Contains(ad); n++) ad = $"{a} ({n})";
            sonuc.Add(ad);
        }
        return sonuc;
    }

    // ---------------------------------------------------------------- kodlama

    public static (string Metin, string Kodlama) Coz(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (Encoding.UTF8.GetString(b, 3, b.Length - 3), "UTF-8");
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (Encoding.Unicode.GetString(b, 2, b.Length - 2), "UTF-16");
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), "UTF-16");
        try
        {
            return (new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Windows1254().GetString(b), "Windows-1254");
        }
    }

    private static Encoding Windows1254()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return Encoding.Latin1;   // son çare: Türkçe harflerin bir kısmı bozuk görünebilir
        }
    }

    // ---------------------------------------------------------------- ayırıcı ve ayrıştırma

    /// <summary>İlk 20 dolu satırda (tırnak dışı) her adayın sayısı: en çok satırda aynı ve &gt;0 olan kazanır.</summary>
    public static char AyiriciBul(string metin)
    {
        var satirlar = MantiksalSatirlar(metin).Where(s => s.Trim().Length > 0).Take(20).ToList();
        if (satirlar.Count == 0) return ';';
        char en = ';';
        var enPuan = (-1, -1);
        foreach (var a in Adaylar)
        {
            var sayilar = satirlar.Select(s => TirnakDisiSay(s, a)).ToList();
            var mod = sayilar.Where(n => n > 0).GroupBy(n => n).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).FirstOrDefault();
            if (mod is null) continue;
            var puan = (mod.Count(), mod.Key);
            if (puan.Item1 > enPuan.Item1 || (puan.Item1 == enPuan.Item1 && puan.Item2 > enPuan.Item2))
            {
                en = a;
                enPuan = puan;
            }
        }
        return en;
    }

    private static int TirnakDisiSay(string satir, char a)
    {
        int n = 0; bool tirnak = false;
        foreach (var c in satir)
        {
            if (c == '"') tirnak = !tirnak;
            else if (c == a && !tirnak) n++;
        }
        return n;
    }

    /// <summary>Tırnak içindeki satır sonlarını bölmeden satırlara ayırır (ayırıcı tespiti için).</summary>
    private static IEnumerable<string> MantiksalSatirlar(string metin)
    {
        var sb = new StringBuilder();
        bool tirnak = false;
        foreach (var c in metin)
        {
            if (c == '"') tirnak = !tirnak;
            if ((c == '\n' || c == '\r') && !tirnak)
            {
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>RFC 4180: tırnaklı hücre, "" kaçışı, hücre içi satır sonu. Hücreler kırpılır; boş satırlar atlanır.</summary>
    public static List<List<string>> Ayristir(string metin, char ayirici)
    {
        var satirlar = new List<List<string>>();
        var satir = new List<string>();
        var hucre = new StringBuilder();
        bool tirnak = false, tirnakliHucre = false;
        for (int i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (tirnak)
            {
                if (c == '"')
                {
                    if (i + 1 < metin.Length && metin[i + 1] == '"') { hucre.Append('"'); i++; }
                    else tirnak = false;
                }
                else hucre.Append(c);
                continue;
            }
            if (c == '"' && hucre.ToString().Trim().Length == 0 && !tirnakliHucre)
            {
                hucre.Clear();
                tirnak = true;
                tirnakliHucre = true;
            }
            else if (c == ayirici)
            {
                satir.Add(hucre.ToString().Trim());
                hucre.Clear();
                tirnakliHucre = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < metin.Length && metin[i + 1] == '\n') i++;
                satir.Add(hucre.ToString().Trim());
                hucre.Clear();
                tirnakliHucre = false;
                if (satir.Any(h => h.Length > 0)) satirlar.Add(satir);
                satir = new List<string>();
            }
            else hucre.Append(c);
        }
        if (hucre.Length > 0 || satir.Count > 0)
        {
            satir.Add(hucre.ToString().Trim());
            if (satir.Any(h => h.Length > 0)) satirlar.Add(satir);
        }
        return satirlar;
    }

    // ---------------------------------------------------------------- değer okuma

    private static readonly string[] TarihBicimleri =
    [
        "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "yyyy.MM.dd", "dd.MM.yy", "d.M.yy",
        "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "d.M.yyyy HH:mm", "d.M.yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
    ];

    /// <summary>Tarih: gg.aa.yyyy (saatli de), gg/aa/yyyy, yyyy-aa-gg ya da Excel seri sayısı (ör. 46289). Okunamazsa null.</summary>
    public static DateOnly? TarihOku(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact(s, TarihBicimleri, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return DateOnly.FromDateTime(t);
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var seri) && seri is >= 36526 and <= 73050)
            return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(seri));   // Excel: 2000–2099
        return null;
    }

    /// <summary>
    /// Tutar: "1.234,56", "1234,56", "1,234.56", "1234.56", "-1.234,56", "(1.234,56)", "1.234,56-", "₺1.234,56 TL".
    /// Tek ayırıcı ve ardından 3 hane varsa binliktir ("1.234" = 1234, "1,234" = 1234). Kuruşa yuvarlanmaz
    /// (karşılaştırma yuvarlar). Okunamazsa null.
    /// </summary>
    public static decimal? TutarOku(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        var t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace("₺", "");
        if (t.EndsWith("TL", StringComparison.OrdinalIgnoreCase)) t = t[..^2];
        if (t.StartsWith("TL", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        var negatif = false;
        if (t.StartsWith('(') && t.EndsWith(')')) { negatif = true; t = t[1..^1]; }
        if (t.StartsWith('-') || t.StartsWith('−')) { negatif = !negatif; t = t[1..]; }
        else if (t.EndsWith('-')) { negatif = !negatif; t = t[..^1]; }
        if (t.StartsWith('+')) t = t[1..];
        if (t.Length == 0 || t.Any(c => !(char.IsAsciiDigit(c) || c is '.' or ','))) return null;

        var sonNokta = t.LastIndexOf('.');
        var sonVirgul = t.LastIndexOf(',');
        string normal;
        if (sonNokta >= 0 && sonVirgul >= 0)
        {
            // İkisi de var: sondaki ondalıktır, diğeri binlik.
            var ondalik = sonNokta > sonVirgul ? '.' : ',';
            var binlik = ondalik == '.' ? ',' : '.';
            if (t.Count(c => c == ondalik) > 1) return null;
            normal = t.Replace(binlik.ToString(), "").Replace(ondalik, '.');
        }
        else if (sonNokta >= 0 || sonVirgul >= 0)
        {
            var a = sonNokta >= 0 ? '.' : ',';
            var parcalar = t.Split(a);
            if (parcalar.Length > 2 || (parcalar.Length == 2 && parcalar[1].Length == 3 && parcalar[0].Length is >= 1 and <= 3 && parcalar[0] != "0"))
            {
                // "1.234.567" ya da "1.234": binlik (tüm gruplar 3 hane olmalı)
                if (parcalar.Skip(1).Any(p => p.Length != 3) || parcalar[0].Length is 0 or > 3) return null;
                normal = string.Concat(parcalar);
            }
            else normal = t.Replace(a, '.');
        }
        else normal = t;

        if (!decimal.TryParse(normal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return null;
        return negatif ? -d : d;
    }
}
