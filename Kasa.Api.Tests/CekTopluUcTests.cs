using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Toplu çek girişi (POST /api/takip/cekler/toplu) ve özetin ödenecek toplamı (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md).
/// Bugün 25 Eylül 2026 (sabit saat); kanallar MEZAT, PERAKENDE, TOPTAN.</summary>
public class CekTopluUcTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekTopluSatirYaz Satir(string no, string kisi = "Mehmet Ticaret", decimal tutar = 10_000m, string yon = CekYonleri.Verilen, string? kanal = "MEZAT", string? banka = "Ziraat") =>
        new(CekTurleri.Cek, yon, no, banka, kisi, tutar, Today.AddDays(30), kanal, false, null, null);

    private static async Task<(HttpStatusCode Durum, JsonElement Govde)> Gonder(HttpClient c, object govde)
    {
        using var yanit = await c.PostAsJsonAsync(Yol + "/toplu", govde, TestContext.Current.CancellationToken);
        return (yanit.StatusCode, JsonDocument.Parse(await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement);
    }

    private static async Task<List<CekDto>> Liste(HttpClient c) => (await c.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Toplu_cekler_giris_sirasiyla_eklenir_ayni_istek_yeniden_eklemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var istek = new CekTopluYaz(Guid.NewGuid(), [Satir("3"), Satir("1", kanal: KanalEtiketleri.Ortak), Satir("2", yon: CekYonleri.Alinan, kanal: null)]);
        var sonuc = await Post<CekTopluSonuc>(c, Yol + "/toplu", istek);
        Assert.Equal(["3", "1", "2"], sonuc.Cekler.Select(x => x.No));
        Assert.Equal(["MEZAT", KanalEtiketleri.Ortak, null], sonuc.Cekler.Select(x => x.Kanal));
        Assert.All(sonuc.Cekler, x => Assert.Equal((1, CekDurumlari.Portfoyde, 10_000m, (string?)null), (x.Surum, x.Durum, x.Kalan, x.Uyari)));
        Assert.Equal(CekKonumlari.Elde, sonuc.Cekler[2].Konum);

        var tekrar = await Post<CekTopluSonuc>(c, Yol + "/toplu", istek);
        Assert.Equal(sonuc.Cekler.Select(x => x.Id), tekrar.Cekler.Select(x => x.Id));
        Assert.Equal(3, (await Liste(c)).Count);
        var (durum, govde) = await Gonder(c, istek with { Satirlar = [Satir("4")] });
        Assert.Equal((HttpStatusCode.Conflict, "ISTEK_KIMLIGI_CAKISMASI"), (durum, govde.GetProperty("kod").GetString()));
        // Arada silinen çek tekrar yanıtında yoktur, geri de gelmez.
        using var sil = new HttpRequestMessage(HttpMethod.Delete, $"{Yol}/{sonuc.Cekler[0].Id}") { Content = JsonContent.Create(new CekSilYaz(Guid.NewGuid(), 1)) };
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(sil, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(["1", "2"], (await Post<CekTopluSonuc>(c, Yol + "/toplu", istek)).Cekler.Select(x => x.No));
        // Kayıt kasayı etkilemez.
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Bir_satir_hataliysa_hicbiri_kaydedilmez_hatalarin_hepsi_satir_numarasiyla_doner()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var id = Guid.NewGuid();
        var (durum, govde) = await Gonder(c, new CekTopluYaz(id, [Satir("1"), Satir(" "), Satir("3", tutar: 0), Satir("4", kanal: "YOK"), Satir("5", banka: null)]));
        Assert.Equal(HttpStatusCode.BadRequest, durum);
        Assert.Equal("Satır 2: Çek / senet numarası boş olamaz.\nSatır 3: Tutar sıfırdan büyük olmalı.\nSatır 4: Kayıtlı bir kasa (kanal) seçin.\nSatır 5: Banka boş olamaz.",
            govde.GetProperty("hata").GetString());
        Assert.Empty(await Liste(c));
        // Reddedilen istek kimliği tüketilmez: düzeltilmiş satırlarla aynı kimlik kullanılabilir.
        Assert.Single((await Post<CekTopluSonuc>(c, Yol + "/toplu", new CekTopluYaz(id, [Satir("1")]))).Cekler);
    }

    [Fact]
    public async Task Satir_sayisi_sinirli_bos_ve_null_satir_reddedilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        foreach (var govde in new object[]
        {
            new CekTopluYaz(Guid.NewGuid(), []), new { istekId = Guid.NewGuid() },
            new CekTopluYaz(Guid.NewGuid(), Enumerable.Range(1, 201).Select(i => Satir(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList()),
        })
        {
            var (durum, yanit) = await Gonder(c, govde);
            Assert.Equal((HttpStatusCode.BadRequest, "En az 1, en çok 200 çek girin."), (durum, yanit.GetProperty("hata").GetString()));
        }
        var (d, y) = await Gonder(c, new { istekId = Guid.NewGuid(), satirlar = new object?[] { null } });
        Assert.Equal((HttpStatusCode.BadRequest, "Satır 1: Satır boş."), (d, y.GetProperty("hata").GetString()));
        Assert.Equal(200, (await Post<CekTopluSonuc>(c, Yol + "/toplu",
            new CekTopluYaz(Guid.NewGuid(), Enumerable.Range(1, 200).Select(i => Satir(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList()))).Cekler.Count);
    }

    [Fact]
    public async Task Ozet_odenecek_toplami_teminatsiz_acik_verilen_ceklerin_kalanidir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var sonuc = await Post<CekTopluSonuc>(c, Yol + "/toplu", new CekTopluYaz(Guid.NewGuid(),
            [Satir("1"), Satir("2"), Satir("3") with { Teminat = true }, Satir("4", yon: CekYonleri.Alinan, kanal: null), Satir("5")]));
        await Post<CekDto>(c, $"{Yol}/{sonuc.Cekler[0].Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), 1, CekHareketTurleri.Odeme, Today, 4_000m, null, null, null));
        await Post<CekDto>(c, $"{Yol}/{sonuc.Cekler[4].Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), 1, CekHareketTurleri.Odeme, Today, 10_000m, null, null, null));
        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(2, 16_000m), ozet.PortfoydekiVerilen);
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.PortfoydekiAlinan);
    }
}
