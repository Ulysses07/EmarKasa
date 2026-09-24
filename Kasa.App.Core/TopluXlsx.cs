using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Kasa.App.Core;

/// <summary>
/// En yalın .xlsx okuyucu (paket C · 16): ilk çalışma sayfasının hücre değerlerini tabloya çevirir;
/// yalnız .NET'in kendi ZIP ve XML okuyucularıyla (NuGet yok). Paylaşılan metin, satır içi metin,
/// sayı, mantıksal değer ve tarih biçimli sayı (Excel seri günü → gg.aa.yyyy) okunur; formül
/// hücresinin son hesaplanmış değeri alınır. Biçim, birleştirme, gizli satır vb. yok sayılır.
/// Sayılar Türkçe yazılır ("1500,5", "-250") ki tutar ayrıştırması CSV ile aynı yoldan geçsin.
/// </summary>
public static class TopluXlsx
{
    /// <summary>Okunan en fazla sütun (A..BL); ötesi yok sayılır.</summary>
    public const int EnFazlaSutun = 64;

    /// <summary>Okunan en fazla dolu satır (fazlası önizlemede "en fazla 1000 satır" hatası verir).</summary>
    public const int EnFazlaSatir = TopluMetin.EnFazlaSatir + TopluBaslik.BaslikArama + 1;

    /// <summary>Tek bir XML parçasının açılmış en büyük boyutu (sıkıştırma bombasına karşı).</summary>
    public const long EnBuyukParca = 64L * 1024 * 1024;

    private const string Ana = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Iliski = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PaketIliski = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>İçerik bir ZIP (xlsx) dosyası mı?</summary>
    public static bool XlsxMi(byte[] icerik)
        => icerik.Length >= 4 && icerik[0] == 0x50 && icerik[1] == 0x4B && icerik[2] == 0x03 && icerik[3] == 0x04;

    /// <summary>İçerik eski ikili Excel (.xls, OLE) dosyası mı?</summary>
    public static bool EskiXlsMi(byte[] icerik)
        => icerik.Length >= 4 && icerik[0] == 0xD0 && icerik[1] == 0xCF && icerik[2] == 0x11 && icerik[3] == 0xE0;

    /// <summary>
    /// İlk çalışma sayfasını okur. Bozuk dosyada <see cref="InvalidDataException"/> ya da
    /// <see cref="XmlException"/> fırlatır (çağıran kullanıcı mesajına çevirir).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Oku(byte[] icerik)
    {
        using var zip = new ZipArchive(new MemoryStream(icerik, writable: false), ZipArchiveMode.Read);
        var (sayfaYolu, tarih1904) = IlkSayfa(zip);
        var sayfa = zip.GetEntry(sayfaYolu) ?? throw new InvalidDataException("Çalışma sayfası bulunamadı.");
        var metinler = PaylasilanMetinler(zip);
        var tarihStilleri = TarihStilleri(zip);
        return Satirlar(sayfa, metinler, tarihStilleri, tarih1904);
    }

    // ------------------------------------------------------------------ çalışma kitabı

    /// <summary>İlk sayfanın ZIP içindeki yolu ve 1904 tarih sistemi bayrağı.</summary>
    private static (string Yol, bool Tarih1904) IlkSayfa(ZipArchive zip)
    {
        var kitap = Belge(zip, "xl/workbook.xml");
        if (kitap is null) return (YedekSayfa(zip), false);
        var pr = kitap.Root?.Element(XName.Get("workbookPr", Ana));
        var tarih1904 = (string?)pr?.Attribute("date1904") is "1" or "true";
        var rid = (string?)kitap.Root?.Element(XName.Get("sheets", Ana))?.Elements(XName.Get("sheet", Ana)).FirstOrDefault()
            ?.Attribute(XName.Get("id", Iliski));
        var iliskiler = Belge(zip, "xl/_rels/workbook.xml.rels");
        var hedef = (string?)iliskiler?.Root?.Elements(XName.Get("Relationship", PaketIliski))
            .FirstOrDefault(r => (string?)r.Attribute("Id") == rid)?.Attribute("Target");
        if (rid is null || hedef is null) return (YedekSayfa(zip), tarih1904);
        var yol = hedef.StartsWith('/') ? hedef[1..] : "xl/" + hedef;
        return (zip.GetEntry(yol) is not null ? yol : YedekSayfa(zip), tarih1904);
    }

    private static string YedekSayfa(ZipArchive zip)
        => zip.Entries.Select(e => e.FullName)
               .Where(a => a.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && a.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
               .OrderBy(a => a.Length).ThenBy(a => a, StringComparer.Ordinal).FirstOrDefault()
           ?? throw new InvalidDataException("Çalışma sayfası bulunamadı.");

    /// <summary>Paylaşılan metinler (&lt;si&gt; başına bir metin; zengin metin parçaları birleşir, fonetik atılır).</summary>
    private static List<string> PaylasilanMetinler(ZipArchive zip)
    {
        var liste = new List<string>();
        var giris = zip.GetEntry("xl/sharedStrings.xml");
        if (giris is null) return liste;
        using var r = Okuyucu(giris);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                liste.Add(MetinTopla(r));
        return liste;
    }

    /// <summary>Tarih biçimli hücre stilleri (cellXfs sırası).</summary>
    private static HashSet<int> TarihStilleri(ZipArchive zip)
    {
        var sonuc = new HashSet<int>();
        var stiller = Belge(zip, "xl/styles.xml");
        if (stiller?.Root is not { } kok) return sonuc;
        var ozel = kok.Element(XName.Get("numFmts", Ana))?.Elements(XName.Get("numFmt", Ana))
            .ToDictionary(n => (int?)n.Attribute("numFmtId") ?? -1, n => (string?)n.Attribute("formatCode") ?? "")
            ?? new Dictionary<int, string>();
        var i = 0;
        foreach (var xf in kok.Element(XName.Get("cellXfs", Ana))?.Elements(XName.Get("xf", Ana)) ?? [])
        {
            var id = (int?)xf.Attribute("numFmtId") ?? 0;
            if (TarihBicimi(id, ozel.GetValueOrDefault(id))) sonuc.Add(i);
            i++;
        }
        return sonuc;
    }

    /// <summary>Sayı biçimi tarih mi? Yerleşik tarih kimlikleri ya da gün/yıl içeren özel biçim.</summary>
    public static bool TarihBicimi(int numFmtId, string? kod)
    {
        if (numFmtId is >= 14 and <= 17 or 22 or >= 27 and <= 36 or >= 50 and <= 58) return true;
        if (string.IsNullOrEmpty(kod)) return false;
        var sade = new System.Text.StringBuilder();
        var tirnak = false;
        var koseli = false;
        for (var k = 0; k < kod.Length; k++)
        {
            var c = kod[k];
            if (c == '"') { tirnak = !tirnak; continue; }
            if (tirnak) continue;
            if (c == '[') { koseli = true; continue; }
            if (c == ']') { koseli = false; continue; }
            if (koseli) continue;
            if (c == '\\') { k++; continue; }
            sade.Append(char.ToLowerInvariant(c));
        }
        var s = sade.ToString();
        return s.Contains('d') || s.Contains('y');
    }

    // ------------------------------------------------------------------ sayfa

    private static List<IReadOnlyList<string>> Satirlar(ZipArchiveEntry sayfa, List<string> metinler, HashSet<int> tarihStilleri, bool tarih1904)
    {
        var tablo = new List<IReadOnlyList<string>>();
        using var r = Okuyucu(sayfa);
        List<string>? satir = null;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "row")
            {
                satir = new List<string>();
                if (r.IsEmptyElement) { satir = null; continue; }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row" && satir is not null)
            {
                while (satir.Count > 0 && satir[^1].Length == 0) satir.RemoveAt(satir.Count - 1);
                if (satir.Count > 0)
                {
                    tablo.Add(satir);
                    if (tablo.Count >= EnFazlaSatir) break;
                }
                satir = null;
            }
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "c" && satir is not null)
            {
                var sutun = SutunNo((string?)r.GetAttribute("r")) ?? satir.Count;
                var tur = r.GetAttribute("t");
                var stil = int.TryParse(r.GetAttribute("s"), NumberStyles.None, CultureInfo.InvariantCulture, out var st) ? st : 0;
                var (deger, satirIci) = HucreIcerigi(r);
                if (sutun >= EnFazlaSutun) continue;
                var metin = Donustur(tur, deger, satirIci, stil, metinler, tarihStilleri, tarih1904);
                while (satir.Count <= sutun) satir.Add("");
                satir[sutun] = metin;
            }
        }
        return tablo;
    }

    /// <summary>&lt;c&gt; öğesinin &lt;v&gt; değeri ve satır içi metni (&lt;is&gt;); okuyucu hücrenin kapanışında kalır.</summary>
    private static (string? Deger, string? SatirIci) HucreIcerigi(XmlReader r)
    {
        if (r.IsEmptyElement) return (null, null);
        string? deger = null, satirIci = null;
        var derinlik = r.Depth;
        r.Read();
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == derinlik))
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "v" && !r.IsEmptyElement)
            {
                deger = r.ReadElementContentAsString();   // okuyucu </v>'den sonraki düğümde
                continue;
            }
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "is") satirIci = MetinTopla(r);
            r.Read();
        }
        return (deger, satirIci);
    }

    /// <summary>
    /// Bulunulan öğenin (si / is) altındaki &lt;t&gt; metinlerini birleştirir; fonetik (&lt;rPh&gt;) atılır.
    /// Okuyucu öğenin kapanışında kalır.
    /// </summary>
    private static string MetinTopla(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var sb = new System.Text.StringBuilder();
        var derinlik = r.Depth;
        r.Read();
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == derinlik))
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh") { r.Skip(); continue; }
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t" && !r.IsEmptyElement)
            {
                sb.Append(r.ReadElementContentAsString());
                continue;
            }
            r.Read();
        }
        return sb.ToString();
    }

    private static string Donustur(string? tur, string? deger, string? satirIci, int stil, List<string> metinler,
        HashSet<int> tarihStilleri, bool tarih1904)
    {
        switch (tur)
        {
            case "s":
                return int.TryParse(deger, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < metinler.Count
                    ? metinler[i] : "";
            case "inlineStr":
                return satirIci ?? "";
            case "str" or "e" or "d":
                return deger ?? "";
            case "b":
                return deger == "1" ? "DOĞRU" : "YANLIŞ";
        }
        if (string.IsNullOrEmpty(deger)) return "";
        if (!double.TryParse(deger, NumberStyles.Float, CultureInfo.InvariantCulture, out var sayi)) return deger;
        if (tarihStilleri.Contains(stil) && Tarih(sayi, tarih1904) is { } t) return t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        // double → decimal dönüşümü 15 anlamlı haneye yuvarlar: formüllerin kayan nokta artığı (0,30000000000000004) atılır.
        try { return ((decimal)sayi).ToString(Kultur.Turkce); }
        catch (OverflowException) { return deger; }
    }

    /// <summary>Excel seri günü → tarih (1900 sistemi; 1904 sisteminde 1462 gün kayar). Aralık dışıysa null.</summary>
    public static DateOnly? Tarih(double seri, bool tarih1904 = false)
    {
        if (tarih1904) seri += 1462;
        if (seri < 1 || seri > 2_958_465) return null;   // 1900-01-01 … 9999-12-31
        try { return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(seri))); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>"AB12" → 27 (0 tabanlı sütun); harf yoksa null.</summary>
    public static int? SutunNo(string? hucreAdi)
    {
        if (string.IsNullOrEmpty(hucreAdi)) return null;
        var n = 0;
        var harf = 0;
        foreach (var c in hucreAdi)
        {
            if (c is >= 'A' and <= 'Z') n = n * 26 + (c - 'A' + 1);
            else if (c is >= 'a' and <= 'z') n = n * 26 + (c - 'a' + 1);
            else break;
            if (++harf > 3) return null;
        }
        return harf == 0 ? null : n - 1;
    }

    // ------------------------------------------------------------------ yardımcılar

    private static XDocument? Belge(ZipArchive zip, string yol)
    {
        var giris = zip.GetEntry(yol);
        if (giris is null) return null;
        using var r = Okuyucu(giris);
        return XDocument.Load(r);
    }

    private static XmlReader Okuyucu(ZipArchiveEntry giris)
    {
        if (giris.Length > EnBuyukParca) throw new InvalidDataException("Excel dosyasının bir parçası çok büyük.");
        var ayar = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };
        return XmlReader.Create(new SinirliAkis(giris.Open(), EnBuyukParca), ayar);
    }

    /// <summary>En fazla belirli bayt okutan akış (ZIP başlığındaki boyut yalan olsa bile).</summary>
    private sealed class SinirliAkis(Stream ic, long sinir) : Stream
    {
        private long _okunan;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = ic.Read(buffer, offset, count);
            _okunan += n;
            if (_okunan > sinir) throw new InvalidDataException("Excel dosyasının bir parçası çok büyük.");
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _okunan; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) ic.Dispose();
            base.Dispose(disposing);
        }
    }
}
