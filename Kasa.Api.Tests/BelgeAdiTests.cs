using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Belge adı ve türü (purchase-1, apiclient-3): yüklemede adın uzantısı, bildirilen içerik türü ve dosyanın sihirli baytları
/// birbiriyle tutarlı olmalı (.hta/.cmd gibi uzantı ya da uyuşmayan tür 400). Saklanan ve indirilen ad yol parçalarından,
/// kontrol ve Unicode biçim (yön) karakterlerinden arınır; uzantısı yalnız tespit edilen türden (.pdf/.png/.jpg) gelir.
/// Eski kayıtlar okunurken aynı kuralla adlandırılır (veri dönüşümü gerekmez). İndirme her zaman ek (attachment) olarak,
/// RFC 6266 filename* ve nosniff ile gider.
/// </summary>
public class BelgeAdiTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [255, 216, 255, 224, 0, 16, 74, 70, 73, 70];

    private static async Task<(KasaWebFactory F, HttpClient Editor, AlisDto Alis)> Kur()
    {
        var f = KasaWebFactory.Sabit(Bugun);
        var editor = await f.EditorClientAsync();
        return (f, editor, await AlisTestYardimcisi.Taslak(editor, "Belge"));
    }

    private static async Task<BelgeDto> Yuklendi(HttpClient c, int alisId, byte[] icerik, string ad, string? tur = null)
    {
        using var r = await AlisTestYardimcisi.YukleYanit(c, alisId, icerik, ad, tur);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{ad}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<BelgeDto>())!;
    }

    [Fact]
    public async Task Yuklenen_belgenin_adi_turden_uzantiyla_ve_bicim_karakterleri_olmadan_saklanir()
    {
        var (f, c, alis) = await Kur();
        await using var _ = f; using var __ = c;
        Assert.Equal("faturagpj.pdf", (await Yuklendi(c, alis.Id, AlisTestYardimcisi.Pdf(64), "fatura‮gpj.pdf", "application/pdf")).DosyaAdi);
        Assert.Equal("x.pdf", (await Yuklendi(c, alis.Id, AlisTestYardimcisi.Pdf(64), "..\\..\\gizli\\x.pdf")).DosyaAdi);
        Assert.Equal("belge-CON.png", (await Yuklendi(c, alis.Id, Png, "CON.png", "image/png")).DosyaAdi);
        Assert.Equal("foto.jpg", (await Yuklendi(c, alis.Id, Jpeg, "foto.JPEG", "image/jpeg")).DosyaAdi);
        Assert.Equal("kamera.png", (await Yuklendi(c, alis.Id, Png, "kamera", "application/octet-stream")).DosyaAdi);
        Assert.Equal("Fatura 12.05.2024.pdf", (await Yuklendi(c, alis.Id, AlisTestYardimcisi.Pdf(64), "Fatura 12.05.2024")).DosyaAdi);
        Assert.Equal("belge.pdf", (await Yuklendi(c, alis.Id, AlisTestYardimcisi.Pdf(64), "‏.pdf")).DosyaAdi);
    }

    [Theory]
    [InlineData("fatura‮fdp.hta", null)]
    [InlineData("fatura.pdf.hta", null)]
    [InlineData("kurulum.cmd", null)]
    [InlineData("sayfa.html", "text/html")]
    [InlineData("fatura.pdf", "text/html")]
    [InlineData("fatura.pdf", "image/png")]
    [InlineData("foto.png", null)]
    public async Task Uzanti_bildirilen_tur_ve_icerik_uyusmazsa_yukleme_reddedilir(string ad, string? tur)
    {
        var (f, c, alis) = await Kur();
        await using var _ = f; using var __ = c;
        // foto.png satırı JPEG içeriktir; diğerleri '%PDF-' ile başlar.
        var icerik = ad == "foto.png" ? Jpeg : AlisTestYardimcisi.Pdf(64);
        using var r = await AlisTestYardimcisi.YukleYanit(c, alis.Id, icerik, ad, tur);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("uyuşmuyor", await AlisTestYardimcisi.Hata(r));
        Assert.Empty((await c.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{alis.Id}/belgeler"))!);
    }

    [Fact]
    public async Task Indirme_ek_olarak_rfc6266_adi_ve_nosniff_ile_gider()
    {
        var (f, c, alis) = await Kur();
        await using var _ = f; using var __ = c;
        var belge = await Yuklendi(c, alis.Id, AlisTestYardimcisi.Pdf(64), "Fatura Ş.pdf");
        Assert.Equal("Fatura Ş.pdf", belge.DosyaAdi);
        using var r = await c.GetAsync($"/api/belgeler/{belge.Id}");
        r.EnsureSuccessStatusCode();
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("Fatura Ş.pdf", r.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", Assert.Single(r.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Eski_kayitlarin_adi_okunurken_ture_gore_normalize_edilir()
    {
        var (f, c, alis) = await Kur();
        await using var _ = f; using var __ = c;
        int pdf, bilinmeyen;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var eski = new BelgeEntity { AlisId = alis.Id, DosyaAdi = "eski‮lmth.hta", IcerikTuru = "application/pdf", Boyut = 9, Yuklendi = DateTimeOffset.UnixEpoch, Icerik = "%PDF-1.4\n"u8.ToArray() };
            var tur = new BelgeEntity { AlisId = alis.Id, DosyaAdi = "rapor.html", IcerikTuru = "text/html", Boyut = 4, Yuklendi = DateTimeOffset.UnixEpoch, Icerik = "<b/>"u8.ToArray() };
            db.Belgeler.AddRange(eski, tur); db.SaveChanges();
            (pdf, bilinmeyen) = (eski.Id, tur.Id);
        }
        var liste = (await c.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{alis.Id}/belgeler"))!;
        Assert.Equal(["eskilmth.pdf", "rapor.bin"], liste.OrderBy(b => b.Id).Select(b => b.DosyaAdi));

        using (var r = await c.GetAsync($"/api/belgeler/{pdf}"))
        {
            Assert.Equal("eskilmth.pdf", r.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        }
        // İzinli türlerin dışındaki (eski ya da elle yazılmış) içerik tarayıcıda yorumlanamaz: ikili dosya olarak iner.
        using (var r = await c.GetAsync($"/api/belgeler/{bilinmeyen}"))
        {
            Assert.Equal("rapor.bin", r.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal("application/octet-stream", r.Content.Headers.ContentType!.MediaType);
        }
    }

    [Theory]
    [InlineData("fatura.pdf", "application/pdf", "fatura.pdf")]
    [InlineData("fatura.pdf.hta", "application/pdf", "fatura.pdf")]
    [InlineData("fatura‮fdp.hta", "application/pdf", "faturafdp.pdf")]
    [InlineData("a​b﻿c⁦d⁩.png", "image/png", "abcd.png")]
    [InlineData("../../x\\y\\z.jpeg", "image/jpeg", "z.jpg")]
    [InlineData("nul.pdf", "application/pdf", "belge-nul.pdf")]
    [InlineData("COM1", "image/png", "belge-COM1.png")]
    [InlineData("lpt9.txt.pdf", "application/pdf", "belge-lpt9.txt.pdf")]
    [InlineData("  . . ", "application/pdf", "belge.pdf")]
    [InlineData(null, "image/jpeg", "belge.jpg")]
    [InlineData("a<b>c:d\"e|f?g*h.pdf", "application/pdf", "abcdefgh.pdf")]
    [InlineData("rapor.xlsx", "text/html", "rapor.bin")]
    [InlineData("ekstre 2024.09", "application/pdf", "ekstre 2024.09.pdf")]
    public void Guvenli_belge_adi_kurallari(string? ad, string tur, string beklenen)
        => Assert.Equal(beklenen, BelgeEndpoints.GuvenliBelgeAdi(ad, tur));

    [Fact]
    public void Guvenli_belge_adi_govdeyi_120_karakterde_keser_vekil_cifti_bolmez()
    {
        Assert.Equal(new string('a', 120) + ".pdf", BelgeEndpoints.GuvenliBelgeAdi(new string('a', 300) + ".pdf", "application/pdf"));
        var ad = BelgeEndpoints.GuvenliBelgeAdi(new string('a', 119) + "😀😀.png", "image/png");
        Assert.Equal(new string('a', 119) + ".png", ad);
    }
}
