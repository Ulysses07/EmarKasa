using System.Text;

namespace Kasa.App.Core.Tests;

/// <summary>ERP12 CSV okuyucu: kodlama, ayırıcı, tırnak, başlık ve değer okuma (Paket F, madde 42).</summary>
public class Erp12CsvTests
{
    private static byte[] Utf8(string s, bool bom = false)
        => bom ? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(s)] : Encoding.UTF8.GetBytes(s);

    private static byte[] Win1254(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1254).GetBytes(s);
    }

    private const string Ornek = "Tarih;Cari Ünvanı;Açıklama;Tutar\r\n12.09.2026;Yılmaz Gıda Ltd. Şti.;Eylül ödemesi;1.234,56\r\n13.09.2026;Kaya Ambalaj;;500,00\r\n";

    [Fact]
    public void Utf8_bomlu_noktali_virgullu_basliklı()
    {
        var t = Erp12Csv.Oku(Utf8(Ornek, bom: true));

        Assert.Equal("UTF-8", t.Kodlama);
        Assert.Equal(';', t.Ayirici);
        Assert.True(t.BaslikVar);
        Assert.Equal(["Tarih", "Cari Ünvanı", "Açıklama", "Tutar"], t.Basliklar);
        Assert.Equal(2, t.Satirlar.Count);
        Assert.Equal("Yılmaz Gıda Ltd. Şti.", t.Satirlar[0][1]);
        Assert.Equal("", t.Satirlar[1][2]);
    }

    [Fact]
    public void Bomsuz_utf8_de_utf8_okunur()
    {
        var t = Erp12Csv.Oku(Utf8(Ornek));
        Assert.Equal("UTF-8", t.Kodlama);
        Assert.Equal("Cari Ünvanı", t.Basliklar[1]);
    }

    [Fact]
    public void Windows_1254_turkce_harfleri_bozulmadan_okunur()
    {
        var t = Erp12Csv.Oku(Win1254(Ornek));

        Assert.Equal("Windows-1254", t.Kodlama);
        Assert.Equal("Cari Ünvanı", t.Basliklar[1]);
        Assert.Equal("Açıklama", t.Basliklar[2]);
        Assert.Equal("Yılmaz Gıda Ltd. Şti.", t.Satirlar[0][1]);
    }

    [Fact]
    public void Utf16_bomlu_okunur()
    {
        byte[] b = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(Ornek)];
        var t = Erp12Csv.Oku(b);
        Assert.Equal("UTF-16", t.Kodlama);
        Assert.Equal("Yılmaz Gıda Ltd. Şti.", t.Satirlar[0][1]);
    }

    [Theory]
    [InlineData(',')]
    [InlineData('\t')]
    [InlineData('|')]
    [InlineData(';')]
    public void Ayirici_tespit_edilir(char a)
    {
        var metin = $"Tarih{a}Cari{a}Tutar\n2026-09-12{a}Yılmaz{a}100.50\n2026-09-13{a}Kaya{a}200\n";
        var t = Erp12Csv.Oku(Utf8(metin));
        Assert.Equal(a, t.Ayirici);
        Assert.Equal(["Tarih", "Cari", "Tutar"], t.Basliklar);
        Assert.Equal("100.50", t.Satirlar[0][2]);
    }

    [Fact]
    public void Virgul_ondalikli_tutar_tirnak_icindeyse_ayirici_virgul_bozulmaz()
    {
        var metin = "Tarih,Cari,Tutar\n12.09.2026,\"Yılmaz Gıda, Ltd.\",\"1.234,56\"\n13.09.2026,Kaya,\"500,00\"\n";
        var t = Erp12Csv.Oku(Utf8(metin));

        Assert.Equal(',', t.Ayirici);
        Assert.Equal("Yılmaz Gıda, Ltd.", t.Satirlar[0][1]);
        Assert.Equal(1234.56m, Erp12Csv.TutarOku(t.Satirlar[0][2]));
    }

    [Fact]
    public void Tirnak_kacisi_ve_hucre_ici_satir_sonu()
    {
        var metin = "Tarih;Cari;Tutar\r\n12.09.2026;\"Ali \"\"Usta\"\" \r\nTesisat\";100\r\n";
        var t = Erp12Csv.Oku(Utf8(metin));

        Assert.Single(t.Satirlar);
        Assert.Equal("Ali \"Usta\" \r\nTesisat", t.Satirlar[0][1]);
    }

    [Fact]
    public void Bastaki_baslik_satirlari_ve_bos_satirlar_atlanir()
    {
        var metin = "ERP12 Tediye Listesi\r\n01.09.2026 - 30.09.2026\r\n\r\nTarih;Cari;Tutar\r\n12.09.2026;Yılmaz;100\r\n\r\n13.09.2026;Kaya;200\r\n";
        var t = Erp12Csv.Oku(Utf8(metin));

        Assert.True(t.BaslikVar);
        Assert.Equal(["Tarih", "Cari", "Tutar"], t.Basliklar);
        Assert.Equal(2, t.Satirlar.Count);
    }

    [Fact]
    public void Basliksiz_dosyada_sutun_adlari_uretilir()
    {
        var t = Erp12Csv.Oku(Utf8("12.09.2026;Yılmaz;100\n13.09.2026;Kaya;200\n"));
        Assert.False(t.BaslikVar);
        Assert.Equal(["Sütun 1", "Sütun 2", "Sütun 3"], t.Basliklar);
        Assert.Equal(2, t.Satirlar.Count);
    }

    [Fact]
    public void Ayni_adli_ve_bos_basliklar_tekillesir()
    {
        var t = Erp12Csv.Oku(Utf8("Tarih;Tutar;Tutar;\n12.09.2026;100;200;x\n"));
        Assert.Equal(["Tarih", "Tutar", "Tutar (2)", "Sütun 4"], t.Basliklar);
    }

    [Fact]
    public void Bos_dosya_anlasilir_hata()
    {
        Assert.Equal(Erp12Csv.BosDosyaMesaji, Assert.Throws<DogrulamaHatasi>(() => Erp12Csv.Oku([])).Message);
        Assert.Equal(Erp12Csv.BosDosyaMesaji, Assert.Throws<DogrulamaHatasi>(() => Erp12Csv.Oku(Utf8("\r\n\r\n ; ; \r\n"))).Message);
    }

    [Theory]
    [InlineData("12.09.2026", 2026, 9, 12)]
    [InlineData("2.9.2026", 2026, 9, 2)]
    [InlineData("12/09/2026", 2026, 9, 12)]
    [InlineData("12-09-2026", 2026, 9, 12)]
    [InlineData("2026-09-12", 2026, 9, 12)]
    [InlineData("12.09.26", 2026, 9, 12)]
    [InlineData("12.09.2026 14:30", 2026, 9, 12)]
    [InlineData("12.09.2026 14:30:05", 2026, 9, 12)]
    [InlineData("2026-09-12T14:30:05", 2026, 9, 12)]
    [InlineData("46277", 2026, 9, 12)]   // Excel seri sayısı
    public void Tarih_okunur(string s, int y, int a, int g) => Assert.Equal(new DateOnly(y, a, g), Erp12Csv.TarihOku(s));

    [Theory]
    [InlineData("")]
    [InlineData("Yılmaz")]
    [InlineData("31.02.2026")]
    [InlineData("1234,56")]
    [InlineData("100")]      // seri sayı aralığı dışında
    public void Tarih_okunamaz(string s) => Assert.Null(Erp12Csv.TarihOku(s));

    [Theory]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1234,56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1234.56", "1234.56")]
    [InlineData("1.234.567,8", "1234567.8")]
    [InlineData("1.234", "1234")]
    [InlineData("1,234", "1234")]
    [InlineData("0,234", "0.234")]
    [InlineData("12,5", "12.5")]
    [InlineData("-1.234,56", "-1234.56")]
    [InlineData("(1.234,56)", "-1234.56")]
    [InlineData("1.234,56-", "-1234.56")]
    [InlineData("₺1.234,56", "1234.56")]
    [InlineData("1.234,56 TL", "1234.56")]
    [InlineData("TL 500", "500")]
    [InlineData("1 234,56", "1234.56")]
    [InlineData("+75", "75")]
    public void Tutar_okunur(string s, string beklenen)
        => Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), Erp12Csv.TutarOku(s));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3,4.5")]
    [InlineData("12.34.56")]
    [InlineData("1,23,45")]
    [InlineData("$100")]
    public void Tutar_okunamaz(string s) => Assert.Null(Erp12Csv.TutarOku(s));
}
