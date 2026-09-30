using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Dışa aktarımın, XML 1.0'da yazılamayan karakter taşıyan eski kayıtlarda da
/// geçerli dosya üretmesi (host-auth-6, ops-4).
/// </summary>
public class RaporDosyasiTests
{
    private static readonly DateOnly Gun = new(2026, 3, 10);
    // Öznitelik bloğu UTF-8 olduğundan eşsiz vekiller InlineData ile taşınamaz; metinler burada kurulur.
    private static readonly string YalnizYuksekVekil = "\uD83D";
    private static readonly string YalnizDusukVekil = "\uDE00";
    private static readonly string Pizza = char.ConvertFromUtf32(0x1F355);

    [Fact]
    public void Temizleyici_gecersiz_karakterleri_yer_tutucuya_cevirir_gecerlileri_korur()
    {
        Assert.Equal("", RaporDosyasi.DisaAktarimMetni(null));
        Assert.Equal("Fatura 12 · Çiğdem", RaporDosyasi.DisaAktarimMetni("Fatura 12 · Çiğdem"));
        Assert.Equal("a\tb\r\nc", RaporDosyasi.DisaAktarimMetni("a\tb\r\nc"));
        Assert.Equal("A\uFFFDB", RaporDosyasi.DisaAktarimMetni("A\u000BB"));
        Assert.Equal("\uFFFD\uFFFD\uFFFD\uFFFD", RaporDosyasi.DisaAktarimMetni("\u0000\u0001\u000C\u001F"));
        Assert.Equal("x\uFFFDy\uFFFDz", RaporDosyasi.DisaAktarimMetni("x\uFFFEy\uFFFFz"));
        Assert.Equal("kahve " + Pizza, RaporDosyasi.DisaAktarimMetni("kahve " + Pizza));
        Assert.Equal("a\uFFFDb", RaporDosyasi.DisaAktarimMetni("a" + YalnizYuksekVekil + "b"));
        Assert.Equal("a\uFFFD", RaporDosyasi.DisaAktarimMetni("a" + YalnizYuksekVekil));
        Assert.Equal("\uFFFDa", RaporDosyasi.DisaAktarimMetni(YalnizDusukVekil + "a"));
        // Ters sıradaki çift (düşük + yüksek) geçerli bir kod noktası değildir.
        Assert.Equal("\uFFFD\uFFFD", RaporDosyasi.DisaAktarimMetni(YalnizDusukVekil + YalnizYuksekVekil));
    }

    [Fact]
    public void Xlsx_denetim_karakterli_metinlerle_gecerli_zip_ve_xml_uretir()
    {
        var rows = new[]
        {
            Satir("A\u000BB", "Fatura\u0001 12\uFFFF" + YalnizYuksekVekil + " son", "MEZAT\u001F"),
            Satir("Pizzacı", "dilim " + Pizza, "PERAKENDE"),
        };

        var dosya = Assert.IsType<FileContentHttpResult>(RaporDosyasi.Olustur(rows, Gun, Gun, "MEZAT\u0002", "xlsx"));

        var metinler = XlsxMetinleri(dosya.FileContents.ToArray());
        Assert.Contains("A\uFFFDB", metinler);
        Assert.Contains("Fatura\uFFFD 12\uFFFD\uFFFD son", metinler);
        Assert.Contains("MEZAT\uFFFD", metinler);
        Assert.Contains("dilim " + Pizza, metinler);
        Assert.Contains("Pizzacı", metinler);
        Assert.DoesNotContain(metinler, m => m.Any(c => c < ' ' && c is not ('\t' or '\r' or '\n')));
    }

    [Fact]
    public void Csv_ayni_temizleyiciyi_kullanir_formul_korumasi_surer()
    {
        var rows = new[]
        {
            Satir("=2+2\u0001", "\u000B@SUM(A1)", "MEZAT"),
            Satir("\t=1+1", "-5+3", "MEZAT"),
            Satir("Sade\uFFFF", "not" + YalnizDusukVekil, "MEZAT\u0007"),
        };

        var dosya = Assert.IsType<FileContentHttpResult>(RaporDosyasi.Olustur(rows, Gun, Gun, null, "csv"));
        var csv = Encoding.UTF8.GetString(dosya.FileContents.Span);

        Assert.Contains("\"'=2+2\uFFFD\"", csv);
        Assert.Contains("\"\uFFFD@SUM(A1)\"", csv);
        Assert.Contains("\"'\t=1+1\"", csv);
        Assert.Contains("\"'-5+3\"", csv);
        Assert.Contains("\"Sade\uFFFD\"", csv);
        Assert.Contains("\"not\uFFFD\"", csv);
        Assert.Contains("\"MEZAT\uFFFD\"", csv);
        Assert.DoesNotContain(csv, c => c < ' ' && c is not ('\t' or '\r' or '\n'));
    }

    [Fact]
    public async Task Disari_aktar_veritabanindaki_denetim_karakterli_giderle_200_doner()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        using (var scope = f.Services.CreateScope())
        {
            // Doğrulamadan önce kaydedilmiş (ya da API dışından gelmiş) kayıtları temsil eder.
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.AddRange(
                new IslemEntity
                {
                    Tarih = Gun,
                    Cari = "Firma\u000B",
                    TutarTl = 12.34m,
                    Kanal = KanalEtiketleri.Ortak,
                    Tip = GiderTipi.Cari,
                    Not = "a\u0001b\u000Bc\uFFFFd" + YalnizYuksekVekil + "e"
                },
                new IslemEntity { Tarih = Gun, Cari = "=2+2", TutarTl = 5m, Kanal = KanalEtiketleri.Ortak, Tip = GiderTipi.Cari, Not = "düz" });
            db.SaveChanges();
        }
        string url = $"/api/disari-aktar?baslangic={Gun:yyyy-MM-dd}&bitis={Gun:yyyy-MM-dd}&bicim=";

        var xlsx = await c.GetAsync(url + "xlsx", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var metinler = XlsxMetinleri(await xlsx.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Firma\uFFFD", metinler);
        Assert.Contains("a\uFFFDb\uFFFDc\uFFFDd\uFFFDe", metinler);
        Assert.Contains("=2+2", metinler);

        var csv = await c.GetAsync(url + "csv", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var csvMetni = await csv.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"Firma\uFFFD\"", csvMetni);
        Assert.Contains("\"'=2+2\"", csvMetni);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(url + "html", TestContext.Current.CancellationToken)).StatusCode);
    }

    private static IslemOkuDto Satir(string cari, string? not, string kanal)
        => new(1, Gun, cari, 10m, kanal, null, GiderTipi.Cari, not, null);

    /// <summary>
    /// Zip'teki her parçayı karakter denetimi açık XmlReader ile sonuna kadar okur,
    /// çalışma sayfasındaki satır içi metinleri döndürür.
    /// </summary>
    private static List<string> XlsxMetinleri(byte[] icerik)
    {
        using var zip = new ZipArchive(new MemoryStream(icerik), ZipArchiveMode.Read);
        XDocument? sayfa = null;
        foreach (var parca in zip.Entries)
        {
            using var akis = parca.Open();
            using var okuyucu = XmlReader.Create(akis, new XmlReaderSettings { CheckCharacters = true, DtdProcessing = DtdProcessing.Prohibit });
            var belge = XDocument.Load(okuyucu);
            if (parca.FullName == "xl/worksheets/sheet1.xml")
                sayfa = belge;
        }
        Assert.NotNull(sayfa);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return sayfa.Descendants(ns + "t").Select(t => t.Value).ToList();
    }
}
