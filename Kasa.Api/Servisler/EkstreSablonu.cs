using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Kasa.Api.Servisler;

/// <summary>Örnek Excel (docs/specs/2026-11-25-excel-yukleme.md): "Hareketler" sayfası (başlık, iki örnek satır, hücre doğrulamaları) ve
/// "Nasıl doldurulur" sayfası. Örnek satırlar "ÖRNEK" ile başlar; silinmeden yüklenen dosya reddedilir.</summary>
public static class EkstreSablonu
{
    public const string DosyaAdi = "kasa-hareket-ornegi.xlsx";
    public const string IcerikTuru = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int SonSatir = 2001;

    private static readonly string[] Aciklamalar =
    [
        "Emar Kasa · hareket yükleme örneği",
        "1. 'Hareketler' sayfasındaki iki ÖRNEK satırını silin, kendi hareketlerinizi başlığın altına yazın. Başlıkları değiştirmeyin.",
        "2. Tarih: 31.01.2027 gibi. Açıklama boş olamaz.",
        "3. Giriş hesaba giren, Çıkış hesaptan çıkan paradır; her satırda yalnız biri dolu olur. Tutarı TL olarak, en çok iki kuruş hanesiyle yazın.",
        "4. Kart ekstresinde Çıkış harcama, Giriş karta ödeme ya da iadedir.",
        "5. Bakiye isteğe bağlıdır; yazarsanız giriş ve çıkışlar bakiyeyle doğrulanır.",
        "6. Bir satır bile hatalıysa dosya işleme alınmaz; hatalar satır numarasıyla gösterilir. Düzeltip yeniden yükleyin.",
        "7. Bankadan indirdiğiniz Excel'i de doğrudan yükleyebilirsiniz: Tarih, Açıklama ve Borç/Alacak ya da Tutar sütunları yeterlidir.",
    ];

    public static byte[] Olustur(DateOnly bugun)
    {
        XElement Metin(string ad, string deger, int stil = 0) => new(Ns + "c", new XAttribute("r", ad), new XAttribute("s", stil), new XAttribute("t", "inlineStr"), new XElement(Ns + "is", new XElement(Ns + "t", deger)));
        XElement Sayi(string ad, decimal deger, int stil) => new(Ns + "c", new XAttribute("r", ad), new XAttribute("s", stil), new XElement(Ns + "v", deger.ToString(CultureInfo.InvariantCulture)));
        XElement Satir(int no, params XElement[] hucreler) => new(Ns + "row", new XAttribute("r", no), hucreler);
        var gun = (decimal)bugun.ToDateTime(TimeOnly.MinValue).ToOADate();
        var hareketler = new XElement(Ns + "worksheet",
            new XElement(Ns + "sheetViews", new XElement(Ns + "sheetView", new XAttribute("workbookViewId", 0), new XElement(Ns + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
            new XElement(Ns + "cols", new[] { 14, 50, 16, 16, 24 }.Select((g, i) => new XElement(Ns + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", g), new XAttribute("customWidth", 1), new XAttribute("style", i == 0 ? 2 : i == 1 ? 0 : 3)))),
            new XElement(Ns + "sheetData",
                Satir(1, Metin("A1", "Tarih", 1), Metin("B1", "Açıklama", 1), Metin("C1", "Giriş (TL)", 1), Metin("D1", "Çıkış (TL)", 1), Metin("E1", "Bakiye (isteğe bağlı)", 1)),
                Satir(2, Sayi("A2", gun, 2), Metin("B2", "ÖRNEK: POS tahsilatı (bu satırı silin)"), Sayi("C2", 10000m, 3)),
                Satir(3, Sayi("A3", gun, 2), Metin("B3", "ÖRNEK: Yurtiçi Kargo (bu satırı silin)"), Sayi("D3", 1250.5m, 3))),
            new XElement(Ns + "dataValidations", new XAttribute("count", 2),
                Dogrulama("date", "greaterThanOrEqual", $"A2:A{SonSatir}", "36526", "Tarih", "Tarihi 31.01.2027 gibi yazın."),
                Dogrulama("decimal", "greaterThanOrEqual", $"C2:D{SonSatir}", "0", "Tutar", "Tutarı eksi olmadan, sayı olarak yazın.")));
        var yardim = new XElement(Ns + "worksheet",
            new XElement(Ns + "cols", new XElement(Ns + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 120), new XAttribute("customWidth", 1))),
            new XElement(Ns + "sheetData", Aciklamalar.Select((m, i) => Satir(i + 1, Metin($"A{i + 1}", m, i == 0 ? 1 : 0)))));
        using var sonuc = new MemoryStream();
        using (var zip = new ZipArchive(sonuc, ZipArchiveMode.Create, true))
        {
            void Parca(string yol, string icerik)
            { using var w = new StreamWriter(zip.CreateEntry(yol).Open(), new UTF8Encoding(false)); w.Write(icerik); }
            const string Paket = "http://schemas.openxmlformats.org/package/2006/", Belge = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", Tur = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
            Parca("[Content_Types].xml", $"<Types xmlns=\"{Paket}content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + $"<Override PartName=\"/xl/workbook.xml\" ContentType=\"{Tur}sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"{Tur}worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"{Tur}worksheet+xml\"/>"
                + $"<Override PartName=\"/xl/styles.xml\" ContentType=\"{Tur}styles+xml\"/></Types>");
            Parca("_rels/.rels", $"<Relationships xmlns=\"{Paket}relationships\"><Relationship Id=\"rId1\" Type=\"{Belge}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Parca("xl/workbook.xml", $"<workbook xmlns=\"{Ns}\" xmlns:r=\"{Belge}\"><sheets><sheet name=\"Hareketler\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Nasıl doldurulur\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
            Parca("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{Paket}relationships\"><Relationship Id=\"rId1\" Type=\"{Belge}/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"{Belge}/worksheet\" Target=\"worksheets/sheet2.xml\"/>"
                + $"<Relationship Id=\"rId3\" Type=\"{Belge}/styles\" Target=\"styles.xml\"/></Relationships>");
            // Biçimler: 0 düz, 1 kalın başlık, 2 tarih (gün.ay.yıl), 3 para (#,##0.00).
            Parca("xl/styles.xml", $"<styleSheet xmlns=\"{Ns}\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd\\.mm\\.yyyy\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs>"
                + "<cellXfs count=\"4\"><xf/><xf fontId=\"1\" applyFont=\"1\"/><xf numFmtId=\"164\" applyNumberFormat=\"1\"/><xf numFmtId=\"4\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            Parca("xl/worksheets/sheet1.xml", hareketler.ToString(SaveOptions.DisableFormatting));
            Parca("xl/worksheets/sheet2.xml", yardim.ToString(SaveOptions.DisableFormatting));
        }
        return sonuc.ToArray();
    }

    private static XElement Dogrulama(string tur, string islec, string aralik, string deger, string baslik, string ileti) => new(Ns + "dataValidation",
        new XAttribute("type", tur), new XAttribute("operator", islec), new XAttribute("allowBlank", 1), new XAttribute("showErrorMessage", 1), new XAttribute("errorTitle", baslik), new XAttribute("error", ileti),
        new XAttribute("sqref", aralik), new XElement(Ns + "formula1", deger));
}
