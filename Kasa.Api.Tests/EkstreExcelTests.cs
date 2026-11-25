using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;

namespace Kasa.Api.Tests;

/// <summary>Excel ile hareket yükleme (docs/specs/2026-11-25-excel-yukleme.md): tablo bulma, hatalı dosyanın reddi, yön kanıtları, bakiye
/// doğrulaması, örnek dosya ve uçlar.</summary>
public class EkstreExcelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Bugun => KasaWebFactory.VarsayilanBugun;

    /// <summary>Excel hücre hatası.</summary>
    private sealed record Hata(string Kod);

    /// <summary>Bellekte .xlsx: metin, sayı (decimal/int/double), tarih (DateOnly, tarih biçimli) ve hata hücreleri; null boş hücredir.</summary>
    private static byte[] Xlsx(params (string Ad, object?[][] Satirlar)[] sayfalar)
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

    private static byte[] Tek(params object?[][] satirlar) => Xlsx(("Hareketler", satirlar));
    private static object?[] S(params object?[] hucreler) => hucreler;
    private static DateOnly G(int gun) => new(2026, 11, gun);
    private static EkstreOkumaSonucu Banka(params object?[][] satirlar) => EkstreExcelOkuyucu.Oku(Tek(satirlar), EkstreKaynaklari.Banka);
    private static EkstreOkumaSonucu Kart(params object?[][] satirlar) => EkstreExcelOkuyucu.Oku(Tek(satirlar), EkstreKaynaklari.Kart);
    private static string Red(params object?[][] satirlar) => Assert.Throws<ExcelOkumaException>(() => Banka(satirlar)).Message;
    private static string[] Hatalar(params object?[][] satirlar) => Red(satirlar).Split('\n')[1..];
    private static readonly object?[] IkiSutun = S("Tarih", "Açıklama", "Borç", "Alacak", "Bakiye");
    private static readonly object?[] TekSutun = S("İşlem Tarihi", "Açıklama", "Tutar (TL)");

    // ---- Tablo ----

    [Fact]
    public void Banka_tablosu_ust_bilginin_altinda_bulunur_yon_sutundan_bakiye_dogrular_ozet_atlanir()
    {
        var s = Banka(S("T. X BANKASI"), S("Hesap No", "TR12"), S("Para Birimi: TL"), S(), S("Tarih", "Valör", "Açıklama", "Borç", "Alacak", "Bakiye", "Dekont No"),
            S("Devir", null, null, null, null, 10_000m),
            S(G(3), G(4), "KARGO FİRMASI", 1250.5m, null, 8749.5m, "D1"), S(G(4), G(4), "POS BLOKE ÇÖZÜM", null, 2500, 11249.5m, "D2"), S(null, null, null, null, null, null),
            S(null, null, "Dipnot: bilgilendirme amaçlıdır"), S("TOPLAM", null, null, 1250.5m, 2500m, 11249.5m));
        Assert.Equal([(1, 1250.5m, "Cikis", "Gider"), (2, 2500m, "Giris", "Gelir")], s.Satirlar.Select(r => (r.No, r.Tutar!.Value, r.Yon, r.OnerilenIslem)));
        Assert.All(s.Satirlar, r => Assert.Equal((YonKaynaklari.Sutun, "TRY", 0, 1), (r.YonKaynagi, r.ParaBirimi, r.Uyarilar.Count, r.Sayfa)));
        Assert.Equal((G(3), "KARGO FİRMASI"), (s.Satirlar[0].Tarih!.Value, s.Satirlar[0].Aciklama));
        Assert.Equal("Excel satır 7: 03.11.2026 | 04.11.2026 | KARGO FİRMASI | 1250.5 | 8749.5 | D1", s.Satirlar[0].KaynakSatir);
        Assert.Equal("Excel: 'Hareketler' sayfası okundu. Sütunlar: Tarih A, Valör B, Açıklama C, Çıkış D, Giriş E, Bakiye F.", s.Uyarilar[0]);
        Assert.Equal(("Özet satırı", "Excel satır 11: TOPLAM | 1250.5 | 2500 | 11249.5"), (s.Atlananlar.Single().Neden, s.Atlananlar[0].Metin));
    }

    [Fact]
    public void Islem_turu_aciklamanin_basina_eklenir_valor_tek_tarih_olabilir_yinelenen_baslik_atlanir()
    {
        var s = Banka(S("VALÖR TARİHİ", "İşlem Türü", "Açıklama", "Giriş", "Çıkış"), S(G(3), "GELEN FAST", "AHMET YILMAZ", 100, null), S("Valör Tarihi", "İşlem Türü", "Açıklama", "Giriş", "Çıkış"),
            S(G(4), "HAVALE", "havale mehmet", 50, null), S(G(5), "MASRAF", "", null, 5));
        Assert.Equal(["GELEN FAST AHMET YILMAZ", "havale mehmet", "MASRAF"], s.Satirlar.Select(r => r.Aciklama));
    }

    [Fact]
    public void Baslik_bulunamazsa_ne_goruldugu_soylenir_iki_sayfa_ve_cift_sutun_reddedilir()
    {
        var ileti = Red(S("Ad", "Soyad", "Telefon"), S("a", "b", "c"));
        Assert.StartsWith("Hareket tablosu bulunamadı.", ileti);
        Assert.Contains("Görülen başlıklar: Ad, Soyad, Telefon, a, b, c.", ileti);
        var iki = Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.Oku(Xlsx(("Ocak", [TekSutun, S(G(1), "x", -1)]), ("Not", [S("boş")]), ("Şubat", [TekSutun, S(G(2), "y", -1)])), "Banka")).Message;
        Assert.Equal("Birden çok sayfada hareket tablosu var: Ocak, Şubat. Yalnız bir sayfa bırakıp yeniden yükleyin.", iki);
        Assert.Contains("aynı sütun birden çok kez var (Tutar C, İşlem Tutarı D)", Red(S("Tarih", "Açıklama", "Tutar", "İşlem Tutarı"), S(G(1), "x", 1, 1)));
        Assert.Equal("Başlığın altında hareket satırı yok.", Red(TekSutun, S(), S("dipnot")));
    }

    // ---- Hatalı dosya işleme alınmaz ----

    [Fact]
    public void Butun_hatalar_excel_satir_numarasiyla_doner_hicbir_satir_okunmaz()
    {
        Assert.Equal(
        [
            "Satır 3, Tarih (A3): tarih boş.",
            "Satır 4, Tarih (A4): \"31.02.2026\" tarih olarak okunamadı (31.01.2027 gibi yazın).",
            "Satır 5, Açıklama (B5): açıklama boş.",
            "Satır 6: Giriş ve Çıkış ikisi birden dolu; yalnız biri yazılmalı.",
            "Satır 7: Giriş ve Çıkış ikisi de boş ya da sıfır.",
            "Satır 8, Çıkış (C8): \"12,500\" tutar olarak okunamadı (1234,56 ya da 1.234,56 gibi yazın).",
            "Satır 9, Çıkış (C9): \"1.005\" kuruştan küçük hane içeriyor.",
            "Satır 10, Giriş (D10): hücrede Excel hatası var.",
            "Satır 11, Giriş (D11): Giriş eksi olamaz.",
            "Satır 12, Bakiye (E12): \"yok\" tutar olarak okunamadı.",
            "Satır 13, Tarih (A13): \"1999-01-01\" tarih olarak okunamadı (31.01.2027 gibi yazın).",
        ], Hatalar(IkiSutun, S(G(1), "iyi", 1, null, null), S(null, "tarihsiz", 1), S("31.02.2026", "x", 1), S(G(1), " ", 1), S(G(1), "x", 1, 2), S(G(1), "x", 0, null), S(G(1), "x", "12,500"),
            S(G(1), "x", 1.005m), S(G(1), "x", null, new Hata("#REF!")), S(G(1), "x", null, -5), S(G(1), "x", 1, null, "yok"), S("1999-01-01", "x", 1)));
        Assert.Equal(["Satır 2, Tutar (C2): tutar boş.", "Satır 3, Tutar (C3): tutar sıfır.", "Satır 4, Tutar (C4): \"beş\" tutar olarak okunamadı."],
            Hatalar(TekSutun, S(G(1), "x", null), S(G(1), "x", 0), S(G(1), "x", "beş")));
        Assert.StartsWith("Excel dosyasında 1 hata var; düzeltilmeden işleme alınmadı.\n", Red(TekSutun, S(G(1), "x", null)));
    }

    [Fact]
    public void En_cok_elli_hata_listelenir_kalani_sayilir()
    {
        var hatalar = Hatalar([TekSutun, .. Enumerable.Range(1, 60).Select(_ => S(G(1), "x", "?"))]);
        Assert.Equal(51, hatalar.Length);
        Assert.Equal("… ve 10 hata daha.", hatalar[^1]);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("1.234", 1234)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1.234,56 TL", 1234.56)]
    [InlineData("TRY 15,00", 15)]
    [InlineData("₺ 15", 15)]
    [InlineData("1.234.567,00", 1234567)]
    public void Metin_tutarlar(string metin, double beklenen) =>
        Assert.Equal((decimal)beklenen, Banka(TekSutun, S(G(1), "x", metin)).Satirlar.Single().Tutar);

    [Theory]
    [InlineData("-1.234,56", "Cikis")]
    [InlineData("1.234,56-", "Cikis")]
    [InlineData("(1.234,56)", "Cikis")]
    [InlineData("−1.234,56", "Cikis")]
    [InlineData("+1.234,56", "Giris")]
    [InlineData("1.234,56 B", "Cikis")]
    [InlineData("1.234,56 A", "Giris")]
    public void Isaretli_tutarin_yonu(string metin, string yon)
    {
        var r = Banka(TekSutun, S(G(1), "FİRMA", metin)).Satirlar.Single();
        Assert.Equal((1234.56m, yon), (r.Tutar, r.Yon));
        Assert.Equal(metin.EndsWith('A') || metin.EndsWith('B') ? YonKaynaklari.Sutun : YonKaynaklari.Isaret, r.YonKaynagi);
    }

    [Theory]
    [InlineData("25.11.2026")]
    [InlineData("25/11/2026 14:35")]
    [InlineData("2026-11-25T08:00:00")]
    [InlineData("25-11-2026 - 14:35:22")]
    [InlineData("2026/11/25")]
    public void Metin_tarihler(string metin) => Assert.Equal(G(25), Banka(TekSutun, S(metin, "x", -1)).Satirlar.Single().Tarih);

    [Fact]
    public void Bicimsiz_seri_tarih_okunur() => Assert.Equal(G(25), Banka(TekSutun, S(G(25).ToDateTime(TimeOnly.MinValue).ToOADate(), "x", -1)).Satirlar.Single().Tarih);

    // ---- Giriş mi çıkış mı ----

    [Fact]
    public void Tek_tutar_sutununda_eksi_varsa_arti_giristir_hepsi_artiysa_aciklama_ya_da_belirsiz()
    {
        Assert.Equal([("Giris", YonKaynaklari.Isaret), ("Cikis", YonKaynaklari.Isaret)], Banka(TekSutun, S(G(1), "FİRMA A", 100), S(G(1), "FİRMA B", -50)).Satirlar.Select(r => (r.Yon, r.YonKaynagi)));
        var s = Banka(TekSutun, S(G(1), "FİRMA A", 100), S(G(1), "HAVALE KOMİSYONU", 5)).Satirlar;
        Assert.Equal([("Belirsiz", null), ("Cikis", YonKaynaklari.Sozcuk)], s.Select(r => (r.Yon, r.YonKaynagi)));
        Assert.Contains("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.", s[0].Uyarilar);
        Assert.Equal(EkstreIslemTurleri.Atla, s[0].OnerilenIslem);
    }

    [Fact]
    public void Tek_tutar_sutunu_bakiyeden_cozulur_yon_sutunu_kesindir()
    {
        var s = Banka(S("Tarih", "Açıklama", "Tutar", "Bakiye"), S(G(1), "AÇILIŞ BAKİYESİ", null, 1000), S(G(1), "FİRMA A", 100, 1100), S(G(2), "FİRMA B", 40, 1060), S(G(3), "FİRMA C", 60, 1000));
        Assert.Equal([("Giris", YonKaynaklari.Bakiye), ("Cikis", YonKaynaklari.Bakiye), ("Cikis", YonKaynaklari.Bakiye)], s.Satirlar.Select(r => (r.Yon, r.YonKaynagi)));
        Assert.Equal("Devir satırı", s.Atlananlar.Single().Neden);
        var yon = Banka(S("Tarih", "Açıklama", "Tutar", "Borç/Alacak"), S(G(1), "x", 100, "B"), S(G(1), "y", 100, "Alacak"), S(G(1), "z", 100, "-")).Satirlar;
        Assert.Equal(["Cikis", "Giris", "Cikis"], yon.Select(r => r.Yon));
        Assert.All(yon, r => Assert.Equal(YonKaynaklari.Sutun, r.YonKaynagi));
        Assert.Equal(["Satır 2, Yön (D2): \"?\" yön olarak okunamadı (Giriş/Çıkış ya da A/B yazın).", "Satır 3, Yön (D3): yön boş."],
            Hatalar(S("Tarih", "Açıklama", "Tutar", "Yön"), S(G(1), "x", 1, "?"), S(G(1), "x", 1, null)));
    }

    [Fact]
    public void Bakiyeyle_celisen_belge_reddedilir_tek_celisen_satir_belirsiz_kalir_tutmayan_bakiye_soylenir()
    {
        Assert.StartsWith("Giriş/çıkış bakiyeyle çelişiyor: 3 satırın 3 tanesinde bakiye ters yönde değişiyor (Excel satırı 3, 4, 5).",
            Red(IkiSutun, S(G(1), "a", 10, null, 100), S(G(2), "b", 20, null, 120), S(G(3), "c", 30, null, 150), S(G(4), "d", null, 25, 125)));
        var s = Banka(IkiSutun, S(G(1), "a", null, 100, 1000), S(G(2), "b", null, 50, 1050), S(G(3), "c", 30, null, 1020), S(G(4), "d", 40, null, 1060), S(G(5), "e", 60, null, 1000));
        Assert.Equal(["Giris", "Giris", "Cikis", "Belirsiz", "Cikis"], s.Satirlar.Select(r => r.Yon));
        Assert.Contains(s.Satirlar[3].Uyarilar, u => u.StartsWith("Yazan yön bakiyeyle çelişiyor (bakiye artmış)"));
        var eksik = Banka(IkiSutun, S(G(1), "a", null, 100, 1000), S(G(2), "b", null, 50, 1050), S(G(3), "c", 30, null, 900), S(G(4), "d", 40, null, 860), S(G(5), "e", 60, null, 800));
        Assert.Contains(eksik.Uyarilar, u => u.StartsWith("1 satırda bakiye farkı tutarla tutmuyor (Excel satırı 4)."));
        Assert.Contains("Bakiye farkı bu satırın tutarıyla tutmuyor; Excel'den kontrol edin.", eksik.Satirlar[2].Uyarilar);
        Assert.Equal("Cikis", eksik.Satirlar[2].Yon);
    }

    [Fact]
    public void Borc_sutunu_eksi_yazilabilir_virman_ve_kart_odemesi_onerisi_pdf_ile_ayni()
    {
        var s = Banka(IkiSutun, S(G(1), "FİRMA", -100, null), S(G(1), "VİRMAN VADELİ", 5, null), S(G(1), "KREDİ KARTI ÖDEMESİ", 7, null)).Satirlar;
        Assert.Equal([EkstreIslemTurleri.Gider, EkstreIslemTurleri.Atla, EkstreIslemTurleri.KartOdemesi], s.Select(r => r.OnerilenIslem));
        Assert.Equal(("Cikis", 100m, "Transfer"), (s[0].Yon, s[0].Tutar!.Value, s[1].Sinif));
    }

    // ---- Kart ----

    [Fact]
    public void Kart_ekstresi_isaret_odeme_ve_iadeden_cozulur_taksit_ve_iade_pdf_kurali()
    {
        var s = Kart(S("Harcama Tarihi", "İşyeri", "Taksit", "Tutar TL"), S(G(1), "MIGROS", null, 250), S(G(2), "TRENDYOL", "2/6", 100), S(G(3), "KART BORCU ÖDEMESİ", null, -1000), S(G(4), "İADE ZARA", null, -75)).Satirlar;
        Assert.Equal([("Cikis", EkstreIslemTurleri.KartHarcama), ("Cikis", EkstreIslemTurleri.Atla), ("Giris", EkstreIslemTurleri.KartOdemesi), ("Giris", EkstreIslemTurleri.KartIade)], s.Select(r => (r.Yon, r.OnerilenIslem)));
        Assert.Contains(s[1].Uyarilar, u => u.StartsWith("Taksitli işlemin 2/6. taksidi."));
        // Eksi harcama yazan banka: ödeme artıysa alacak artıdır.
        var ters = Kart(S("Tarih", "Açıklama", "Tutar"), S(G(1), "MIGROS", -250), S(G(3), "KART BORCU ÖDEMESİ", 1000)).Satirlar;
        Assert.Equal(["Cikis", "Giris"], ters.Select(r => r.Yon));
        // Kanıt yoksa işaretli satır tahmin edilmez.
        var belirsiz = Kart(S("Tarih", "Açıklama", "Tutar"), S(G(1), "MIGROS", -250), S(G(1), "ZARA", 20)).Satirlar;
        Assert.All(belirsiz, r => Assert.Equal("Belirsiz", r.Yon));
        Assert.Equal(["Cikis", "Giris"], Kart(S("Tarih", "Açıklama", "Harcama", "Alacak"), S(G(1), "MIGROS", 250, null), S(G(2), "KART BORCU ÖDEMESİ", null, 100)).Satirlar.Select(r => r.Yon));
    }

    [Fact]
    public void Doviz_satiri_kaydedilemez_olarak_isaretlenir_para_birimi_yazmiyorsa_belirsizdir()
    {
        var s = Banka(S("Tarih", "Açıklama", "Tutar", "Para Birimi"), S(G(1), "x", -1, "TL"), S(G(1), "y", -1, "USD"), S(G(1), "z", -1, null)).Satirlar;
        Assert.Equal(["TRY", "USD", "Belirsiz"], s.Select(r => r.ParaBirimi));
        Assert.Contains("Bu satır farklı para biriminde; TL içe aktarmaya uygun değil.", s[1].Uyarilar);
        Assert.Equal(["USD"], Banka(S("Hesap Cinsi: Vadesiz", "Döviz Cinsi: USD"), S("Tarih", "Açıklama", "Tutar"), S(G(1), "x", -1)).Satirlar.Select(r => r.ParaBirimi));
        var iki = Kart(S("Tarih", "Açıklama", "Tutar (TL)", "Tutar (USD)"), S(G(1), "MIGROS", 10, null), S(G(1), "AMAZON", null, 5)).Satirlar;
        Assert.Equal([("TRY", 10m), ("USD", 5m)], iki.Select(r => (r.ParaBirimi, r.Tutar!.Value)));
    }

    // ---- Dosya ----

    [Fact]
    public void Gercek_xls_okunur()
    {
        // Excel 16'nın kaydettiği BIFF8 dosya: Tarih, Aciklama, Borc, Alacak, Bakiye (metin hücreler).
        var s = EkstreExcelOkuyucu.Oku(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ornek", "banka.xls")), "Banka").Satirlar;
        Assert.Equal([(G(3), "KARGO", 1250.5m, "Cikis"), (G(4), "POS BLOKE", 2500m, "Giris")], s.Select(r => (r.Tarih!.Value, r.Aciklama, r.Tutar!.Value, r.Yon)));
    }

    [Fact]
    public void Bozuk_dosya_ve_zip_bombasi_iletiyle_reddedilir()
    {
        Assert.StartsWith("Excel okunamadı.", Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.Oku([.. "PK\u0003\u0004"u8, .. new byte[100]], "Banka")).Message);
        Assert.StartsWith("Excel okunamadı.", Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.Oku([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[600]], "Banka")).Message);
        using var m = new MemoryStream();
        using (var zip = new ZipArchive(m, ZipArchiveMode.Create, true))
        {
            using var a = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            var blok = new byte[1024 * 1024];
            for (var i = 0; i < 101; i++)
                a.Write(blok);
        }
        Assert.StartsWith("Excel dosyasının içeriği çok büyük", Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.Oku(m.ToArray(), "Banka")).Message);
        Assert.True(EkstreExcelOkuyucu.WebSayfasiMi("﻿  <html><table>"u8));
        Assert.False(EkstreExcelOkuyucu.ExcelMi("<html>"u8));
    }

    [Fact]
    public void Ornek_dosya_silinmemis_orneklerle_reddedilir_doldurulunca_okunur()
    {
        var sablon = EkstreSablonu.Olustur(Bugun);
        Assert.Equal("Excel dosyasında 2 hata var; düzeltilmeden işleme alınmadı.\nSatır 2: örnek satır; silip yeniden yükleyin.\nSatır 3: örnek satır; silip yeniden yükleyin.",
            Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.Oku(sablon, "Banka")).Message);
        var s = Banka(S("Tarih", "Açıklama", "Giriş (TL)", "Çıkış (TL)", "Bakiye (isteğe bağlı)"), S(Bugun, "POS tahsilatı", 10000, null, 11000), S(Bugun, "Yurtiçi Kargo", null, 1250.5m, 9749.5m));
        Assert.Equal([("Giris", "TRY"), ("Cikis", "TRY")], s.Satirlar.Select(r => (r.Yon, r.ParaBirimi)));
        Assert.All(s.Satirlar, r => Assert.Empty(r.Uyarilar));
    }

    // ---- Uçlar ----

    private static async Task<HttpResponseMessage> Yukle(HttpClient c, byte[] dosya, string ad)
    {
        using var form = new MultipartFormDataContent { { new StringContent("Banka"), "kaynak" }, { new StringContent("Akbank"), "banka" }, { new StringContent("Ana hesap"), "hesapAdi" }, { new ByteArrayContent(dosya), "dosya", ad } };
        return await c.PostAsync("/api/ekstre-aktar/yukle", form, Ct);
    }
    private static async Task<string> Hata_(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("hata").GetString()!;

    [Fact]
    public async Task Excel_yuklenir_kaydedilir_indirilir_yeniden_okunur_hatali_dosya_hicbir_sey_kaydetmez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(Bugun.Year, 1, 1), kasaAcilisDevri = 1000m }, Ct)).EnsureSuccessStatusCode();
        var hatali = await Yukle(c, Tek(TekSutun, S(Bugun, "x", "beş")), "hareket.xlsx");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, hatali.StatusCode);
        Assert.Equal("Excel dosyasında 1 hata var; düzeltilmeden işleme alınmadı.\nSatır 2, Tutar (C2): \"beş\" tutar olarak okunamadı.", await Hata_(hatali));
        Assert.Empty((await c.GetFromJsonAsync<List<EkstreBelgeOzetDto>>("/api/ekstre-aktar", Ct))!);

        var dosya = Tek(TekSutun, S(Bugun, "KARGO FİRMASI", -100));
        var yanit = await Yukle(c, dosya, "hareket.pdf");
        Assert.True(yanit.IsSuccessStatusCode, await yanit.Content.ReadAsStringAsync(Ct));
        var d = (await yanit.Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!;
        Assert.Equal(("hareket.pdf.xlsx", 1), (d.DosyaAdi, d.Satirlar.Count));
        Assert.Equal(d.Id, (await (await Yukle(c, dosya, "baska-ad.xlsx")).Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!.Id);
        using var indir = await c.GetAsync($"/api/ekstre-aktar/{d.Id}/dosya", Ct);
        Assert.Equal((EkstreSablonu.IcerikTuru, "attachment"), (indir.Content.Headers.ContentType!.MediaType, indir.Content.Headers.ContentDisposition!.DispositionType));
        Assert.Equal(dosya, await indir.Content.ReadAsByteArrayAsync(Ct));
        var oku = await c.PostAsJsonAsync($"/api/ekstre-aktar/{d.Id}/yeniden-oku", new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum), Ct);
        d = (await oku.Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!;

        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [new EkstreSatirYaz(1, Bugun, "KARGO FİRMASI", 100m, "Gider", "Genel", [])]);
        var onizleme = (await (await c.PostAsJsonAsync($"/api/ekstre-aktar/{d.Id}/onizleme", istek, Ct)).Content.ReadFromJsonAsync<EkstreOnizlemeDto>(Ct))!;
        Assert.Equal((-100m, false), (onizleme.KasaEtkisi, onizleme.TekrarOnayGerekli));
        (await c.PostAsJsonAsync($"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti }, Ct)).EnsureSuccessStatusCode();
        var islem = Assert.Single((await c.GetFromJsonAsync<List<JsonElement>>("/api/islemler", Ct))!);
        Assert.Equal("Excel hesap hareketi · Akbank · Ana hesap", islem.GetProperty("not").GetString());
    }

    [Fact]
    public async Task Html_ve_baska_dosya_iletiyle_reddedilir_ornek_dosya_iner()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        Assert.Equal("Bu dosya gerçek Excel değil (web sayfası). Excel'de açıp .xlsx olarak kaydedin ve yeniden yükleyin.", await Hata_(await Yukle(c, "<html><table><tr><td>1</td></tr></table>"u8.ToArray(), "a.xls")));
        Assert.Equal("Geçerli PDF ya da Excel (.xlsx, .xls) dosyası seçin.", await Hata_(await Yukle(c, "merhaba"u8.ToArray(), "a.xlsx")));
        using var r = await c.GetAsync("/api/ekstre-aktar/sablon", Ct);
        Assert.Equal((HttpStatusCode.OK, EkstreSablonu.IcerikTuru, EkstreSablonu.DosyaAdi), (r.StatusCode, r.Content.Headers.ContentType!.MediaType, r.Content.Headers.ContentDisposition!.FileNameStar));
        Assert.True(EkstreExcelOkuyucu.ExcelMi(await r.Content.ReadAsByteArrayAsync(Ct)));
        using var anonim = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/ekstre-aktar/sablon", Ct)).StatusCode);
    }
}
