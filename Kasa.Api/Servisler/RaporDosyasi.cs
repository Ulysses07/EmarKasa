using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace Kasa.Api.Servisler;

public static class RaporDosyasi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    public static IResult Olustur(IReadOnlyList<IslemOkuDto> rows, DateOnly start, DateOnly end, string? channel, string format)
    {
        var name = $"kasa-giderler-{start:yyyyMMdd}-{end:yyyyMMdd}";
        if (format == "csv")
        {
            var csv = new StringBuilder("Tarih;Ödeme yapılan yer;Kanal;Tür;Tutar (TL);Alış No;Açıklama\r\n");
            foreach (var r in rows) csv.AppendLine(string.Join(';', Csv(r.Tarih.ToString("yyyy-MM-dd")), Csv(r.Cari), Csv(r.Kanal), Csv(r.Tip.ToString()), r.TutarTl.ToString("0.00", Tr), r.AlisId?.ToString() ?? "", Csv(r.Not)));
            csv.Append("TOPLAM;;;;").Append(rows.Sum(r => r.TutarTl).ToString("0.00", Tr)).AppendLine(";;");
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", name + ".csv");
        }
        if (format == "xlsx") return Results.File(Excel(rows, start, end, channel), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
        var html = new StringBuilder("<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><title>Emar Kasa · Gider raporu</title><style>body{font:14px system-ui;color:#182d23;margin:32px}h1{font-size:24px}table{width:100%;border-collapse:collapse}td,th{padding:9px;text-align:left;border-bottom:1px solid #ddd}.money{text-align:right;white-space:nowrap}tfoot{font-weight:bold}@media print{.print-help{display:none}tr{break-inside:avoid}thead{display:table-header-group}}</style><h1>Emar Kasa · Gider raporu</h1>");
        html.Append($"<p>{start:dd.MM.yyyy} – {end:dd.MM.yyyy} · Kanal: {H(channel ?? "Tümü")}</p>");
        html.Append("<p class=\"print-help\">Tarayıcının Yazdır menüsünden PDF olarak kaydedebilirsiniz.</p><p>Kanal filtresi, seçilen kanalla ilişkili giderlerin tamamını gösterir. Çok kanallı giderin tutarı tek kez listelenir.</p><table><thead><tr><th>Tarih</th><th>Ödeme yapılan yer</th><th>Kanal</th><th>Açıklama</th><th class=\"money\">Tutar (TL)</th></tr></thead><tbody>");
        foreach (var r in rows) html.Append($"<tr><td>{r.Tarih:dd.MM.yyyy}</td><td>{H(r.Cari)}</td><td>{H(r.Kanal)}</td><td>{H(r.Not)}</td><td class=\"money\">{r.TutarTl.ToString("N2", Tr)}</td></tr>");
        html.Append($"</tbody><tfoot><tr><td colspan=\"4\">Toplam ({rows.Count} kayıt)</td><td class=\"money\">{rows.Sum(r => r.TutarTl).ToString("N2", Tr)}</td></tr></tfoot></table></html>");
        return Results.Content(html.ToString(), "text/html; charset=utf-8", Encoding.UTF8);
    }

    // Formül hücresi olabilecek kullanıcı metnini Excel/CSV açılışında etkisizleştir.
    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && ("=+-@".Contains(value.TrimStart().FirstOrDefault()) || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static byte[] Excel(IReadOnlyList<IslemOkuDto> rows, DateOnly start, DateOnly end, string? channel)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XElement Text(string value) => new(ns + "c", new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
        XElement Number(decimal value) => new(ns + "c", new XAttribute("s", "1"), new XElement(ns + "v", value.ToString(CultureInfo.InvariantCulture)));
        var data = new XElement(ns + "sheetData",
            new XElement(ns + "row", Text("Emar Kasa · Gider raporu")),
            new XElement(ns + "row", Text($"{start:yyyy-MM-dd} / {end:yyyy-MM-dd}"), Text(channel ?? "Tüm kanallar")),
            new XElement(ns + "row", Text("Kanal filtresi eşleşen giderin tam tutarını gösterir.")),
            new XElement(ns + "row", new[] { "Tarih", "Ödeme yapılan yer", "Kanal", "Tür", "Tutar (TL)", "Alış No", "Açıklama" }.Select(Text)));
        foreach (var r in rows) data.Add(new XElement(ns + "row", Text(r.Tarih.ToString("yyyy-MM-dd")), Text(r.Cari), Text(r.Kanal), Text(r.Tip.ToString()), Number(r.TutarTl), Text(r.AlisId?.ToString() ?? ""), Text(r.Not ?? "")));
        data.Add(new XElement(ns + "row", Text("TOPLAM"), Text(""), Text(""), Text(""), Number(rows.Sum(r => r.TutarTl))));
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true))
        {
            void Entry(string path, string contents) { using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(contents); }
            Entry("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Entry("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Giderler\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"2\"><xf/><xf numFmtId=\"4\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            Entry("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", data).ToString(SaveOptions.DisableFormatting));
        }
        return result.ToArray();
    }
}
