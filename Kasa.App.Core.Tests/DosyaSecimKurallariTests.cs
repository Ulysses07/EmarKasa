namespace Kasa.App.Core.Tests;

/// <summary>maui-8: dosya seçimi kuralları (uzantıdan içerik türü, 10 MB sınırı, okuma sırasında vazgeçme) Kasa.App sayfa
/// kod-arkasından App.Core'a taşındı ve CI'da (Linux) sınanır; sayfa yalnız FilePicker ve DisplayAlertAsync çağırır.</summary>
public class DosyaSecimKurallariTests
{
    [Theory]
    [InlineData("fatura.PDF", "application/pdf")]
    [InlineData("dekont.pdf", "application/pdf")]
    [InlineData("fis.png", "image/png")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("B.JPG", "image/jpeg")]
    [InlineData("resim.gif", null)]
    [InlineData("uzantisiz", null)]
    [InlineData("belge.pdf.exe", null)]
    public void Belge_icerik_turu_uzantidan_buyuk_kucuk_harf_duyarsiz_belirlenir(string ad, string? beklenen)
        => Assert.Equal(beklenen, DosyaSecimKurallari.BelgeIcerikTuru(ad));

    [Theory]
    [InlineData("ekstre.pdf", true)]
    [InlineData("EKSTRE.PDF", true)]
    [InlineData("ekstre.pdf.exe", false)]
    [InlineData("ekstre.png", false)]
    public void Pdf_uzantisi_denetlenir(string ad, bool beklenen) => Assert.Equal(beklenen, DosyaSecimKurallari.PdfMi(ad));

    [Fact]
    public async Task Tam_sinirdaki_dosya_okunur_bir_bayt_fazlasi_reddedilir()
    {
        var sinir = DosyaSecimKurallari.EnFazlaBayt;
        Assert.Equal(10 * 1024 * 1024, sinir);

        var tam = await DosyaSecimKurallari.SinirliOkuAsync(new MemoryStream(new byte[sinir]), sinir);
        Assert.Equal(DosyaOkumaDurumu.Tamam, tam.Durum);
        Assert.Equal(sinir, tam.Icerik!.LongLength);

        var fazla = await DosyaSecimKurallari.SinirliOkuAsync(new MemoryStream(new byte[sinir + 1]), sinir);
        Assert.Equal(DosyaOkumaDurumu.SinirAsildi, fazla.Durum);
        Assert.Null(fazla.Icerik);

        var bos = await DosyaSecimKurallari.SinirliOkuAsync(new MemoryStream(), sinir);
        Assert.Equal(DosyaOkumaDurumu.Tamam, bos.Durum);
        Assert.Empty(bos.Icerik!);
    }

    [Fact]
    public async Task Okuma_surerken_devam_kosulu_bozulursa_vazgecilir()
    {
        var parca = 0;
        var okuma = await DosyaSecimKurallari.SinirliOkuAsync(new MemoryStream(new byte[300_000]), DosyaSecimKurallari.EnFazlaBayt, () => ++parca < 2);
        Assert.Equal(DosyaOkumaDurumu.Vazgecildi, okuma.Durum);
        Assert.Null(okuma.Icerik);

        var kosulsuz = await DosyaSecimKurallari.SinirliOkuAsync(new MemoryStream([1, 2, 3]), 3, () => true);
        Assert.Equal(new byte[] { 1, 2, 3 }, kosulsuz.Icerik);
    }
}
