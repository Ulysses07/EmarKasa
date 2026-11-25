using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Kasa.TestOrtak;

/// <summary>Testlerin Excel dosyaları (bellekte, Excel kurulu olmadan).</summary>
internal static class TestExcel
{
    /// <summary>Excel hücre hatası.</summary>
    public sealed record Hata(string Kod);

    /// <summary>Bellekte .xlsx: metin, sayı (decimal/int/double), tarih (DateOnly, tarih biçimli) ve hata hücreleri; null boş hücredir.</summary>
    public static byte[] Xlsx(params (string Ad, object?[][] Satirlar)[] sayfalar)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XElement Hucre(object? v, int satir, int sutun)
        {
            var ad = new XAttribute("r", $"{(char)('A' + sutun)}{satir}");
            return v switch
            {
                null => new(ns + "c", ad),
                DateOnly t => new(ns + "c", ad, new XAttribute("s", 1), new XElement(ns + "v", t.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))),
                Hata h => new(ns + "c", ad, new XAttribute("t", "e"), new XElement(ns + "v", h.Kod)),
                string s => new(ns + "c", ad, new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", s))),
                IFormattable f => new(ns + "c", ad, new XElement(ns + "v", f.ToString(null, CultureInfo.InvariantCulture))),
                _ => throw new ArgumentException(v.ToString()),
            };
        }
        using var sonuc = new MemoryStream();
        using (var zip = new ZipArchive(sonuc, ZipArchiveMode.Create, true))
        {
            void Parca(string yol, string icerik)
            { using var w = new StreamWriter(zip.CreateEntry(yol).Open(), new UTF8Encoding(false)); w.Write(icerik); }
            const string P = "http://schemas.openxmlformats.org/package/2006/", R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", T = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
            Parca("[Content_Types].xml", $"<Types xmlns=\"{P}content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"{T}sheet.main+xml\"/></Types>");
            Parca("_rels/.rels", $"<Relationships xmlns=\"{P}relationships\"><Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Parca("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"{R}\"><sheets>{string.Concat(sayfalar.Select((s, i) => $"<sheet name=\"{s.Ad}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))}</sheets></workbook>");
            Parca("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{P}relationships\">{string.Concat(sayfalar.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{R}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))}<Relationship Id=\"rId99\" Type=\"{R}/styles\" Target=\"styles.xml\"/></Relationships>");
            Parca("xl/styles.xml", $"<styleSheet xmlns=\"{ns}\"><fonts count=\"1\"><font/></fonts><fills count=\"1\"><fill/></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"2\"><xf/><xf numFmtId=\"14\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            for (var i = 0; i < sayfalar.Length; i++)
                Parca($"xl/worksheets/sheet{i + 1}.xml", new XElement(ns + "worksheet", new XElement(ns + "sheetData", sayfalar[i].Satirlar.Select((s, n) => new XElement(ns + "row", new XAttribute("r", n + 1), s.Select((v, k) => Hucre(v, n + 1, k)))))).ToString(SaveOptions.DisableFormatting));
        }
        return sonuc.ToArray();
    }
}
