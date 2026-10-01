using System.Net;
using System.Net.Http.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek panel özeti (GET /api/takip/cekler/ozet; docs/specs/2026-10-01-cekler.md "Panel"): portföydeki alınan, 30 gün içinde
/// tahsil edilecek alınan, 30 gün içinde ödenecek verilen ve vadesi geçmiş tahsil edilmemiş alınan; tutarlar kalandır, teminat ve
/// kapanmış çekler dışarıdadır. Bugün 25 Eylül 2026.</summary>
public class CekOzetTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekYaz Alinan(string no, decimal tutar, DateOnly vade, bool teminat = false) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, no, "Ziraat", "Ahmet Yılmaz", tutar, vade, null, teminat, null, null);

    [Fact]
    public async Task Ozet_kalan_tutarlari_teminatsiz_ve_acik_ceklerden_toplar()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today.AddDays(5)));
        await Post<CekDto>(c, Yol, Alinan("A-2", 20_000m, Today.AddDays(60)));
        await Post<CekDto>(c, Yol, Alinan("A-3", 40_000m, Today.AddDays(3), teminat: true));
        var gecmis = await Post<CekDto>(c, Yol, Alinan("A-4", 5_000m, Today.AddDays(-2)));
        await Post<CekDto>(c, $"{Yol}/{gecmis.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), gecmis.Surum, CekHareketTurleri.Tahsilat, Today, 2_000m, null, "MEZAT", null));
        var kapali = await Post<CekDto>(c, Yol, Alinan("A-5", 1_000m, Today.AddDays(1)));
        await Post<CekDto>(c, $"{Yol}/{kapali.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), kapali.Surum, CekHareketTurleri.Iade, Today, 0m, null, null, null));
        await Post<CekDto>(c, Yol, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, "777", "Halk", "Mehmet Ticaret", 7_000m, Today.AddDays(30),
            KanalEtiketleri.Ortak, false, null, null));
        await Post<CekDto>(c, Yol, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Senet, CekYonleri.Verilen, "S-9", null, "Veli", 9_000m, Today.AddDays(31),
            "MEZAT", false, null, null));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(Today, ozet.Tarih);
        Assert.Equal(new CekOzetKalemi(3, 33_000m), ozet.PortfoydekiAlinan);
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.Alinan30);
        Assert.Equal(new CekOzetKalemi(1, 7_000m), ozet.Verilen30);
        Assert.Equal(new CekOzetKalemi(1, 3_000m), ozet.VadesiGecmis);
    }

    [Fact]
    public async Task Bos_ozet_sifirdir_izleyici_okur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        Assert.Equal(new CekOzetDto(Today, new(0, 0m), new(0, 0m), new(0, 0m), new(0, 0m)),
            await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken));
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync(Yol + "/ozet", TestContext.Current.CancellationToken)).StatusCode);
    }
}
