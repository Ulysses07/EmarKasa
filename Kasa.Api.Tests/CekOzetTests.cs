using System.Net;
using System.Net.Http.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek panel özeti (GET /api/takip/cekler/ozet; docs/specs/2026-10-01-cekler.md "Panel"): portföydeki alınan, 30 gün içinde
/// tahsil edilecek alınan, 30 gün içinde ödenecek verilen, vadesi geçmiş tahsil edilmemiş alınan ve vadesi geçmiş ödenmemiş verilen;
/// tutarlar kalandır, teminat ve kapanmış çekler dışarıdadır. Bugün 25 Eylül 2026.</summary>
public class CekOzetTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekYaz Alinan(string no, decimal tutar, DateOnly vade, bool teminat = false) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, no, "Ziraat", "Ahmet Yılmaz", tutar, vade, null, teminat, null, null);

    private static CekYaz Verilen(string no, decimal tutar, DateOnly vade, string kanal = KanalEtiketleri.Ortak, bool teminat = false) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, no, "Halk", "Mehmet Ticaret", tutar, vade, kanal, teminat, null, null);

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
    public async Task Vade_bugun_olan_alinan_cek_alinan30a_girer_vadesi_gecmise_girmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.Alinan30);
        Assert.Equal(new CekOzetKalemi(0, 0m), ozet.VadesiGecmis);
    }

    [Fact]
    public async Task Vade_dun_olan_alinan_cek_vadesi_gecmise_girer()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today.AddDays(-1)));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.VadesiGecmis);
        Assert.Equal(new CekOzetKalemi(0, 0m), ozet.Alinan30);
    }

    [Fact]
    public async Task Vade_otuz_gun_sonra_olan_alinan_cek_alinan30a_girer()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today.AddDays(30)));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.Alinan30);
    }

    [Fact]
    public async Task Karsiliksiz_alinan_cek_vadesi_gecmisten_ve_portfoyden_duser()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var gecmis = await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today.AddDays(-1)));

        var onceki = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 10_000m), onceki.PortfoydekiAlinan);
        Assert.Equal(new CekOzetKalemi(1, 10_000m), onceki.VadesiGecmis);

        await Post<CekDto>(c, $"{Yol}/{gecmis.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), gecmis.Surum, CekHareketTurleri.Karsiliksiz, Today, 0m, null, null, null));

        var sonraki = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(0, 0m), sonraki.PortfoydekiAlinan);
        Assert.Equal(new CekOzetKalemi(0, 0m), sonraki.VadesiGecmis);
    }

    [Fact]
    public async Task Kismen_odenmis_verilen_cek_verilen30da_kalan_tutarla_sayilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var verilen = await Post<CekDto>(c, Yol, Verilen("777", 10_000m, Today.AddDays(10)));
        await Post<CekDto>(c, $"{Yol}/{verilen.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), verilen.Surum, CekHareketTurleri.Odeme, Today, 4_000m, null, null, null));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 6_000m), ozet.Verilen30);
    }

    /// <summary>Vadesi geçmiş, ödenmemiş verilen çek (2026-10-02 ürün sahibi kararı): vadesi dün olan girer, bugün olan girmez
    /// (bugün Verilen30'dadır); alınan çek bu kaleme girmez, mevcut VadesiGecmis (alınan) kalemi değişmez.</summary>
    [Fact]
    public async Task Vadesi_dun_olan_odenmemis_verilen_cek_verilen_vadesi_gecmise_girer_bugun_olan_girmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Verilen("V-1", 10_000m, Today.AddDays(-1)));
        await Post<CekDto>(c, Yol, Verilen("V-2", 20_000m, Today));
        await Post<CekDto>(c, Yol, Alinan("A-1", 5_000m, Today.AddDays(-1)));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.VerilenVadesiGecmis);
        Assert.Equal(new CekOzetKalemi(1, 20_000m), ozet.Verilen30);
        Assert.Equal(new CekOzetKalemi(1, 5_000m), ozet.VadesiGecmis);
    }

    [Fact]
    public async Task Teminat_odenmis_ve_karsiliksiz_verilen_cek_verilen_vadesi_gecmise_girmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Verilen("T-1", 10_000m, Today.AddDays(-3), teminat: true));
        var odenen = await Post<CekDto>(c, Yol, Verilen("O-1", 10_000m, Today.AddDays(-3)));
        await Post<CekDto>(c, $"{Yol}/{odenen.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), odenen.Surum, CekHareketTurleri.Odeme, Today, 10_000m, null, null, null));
        var karsiliksiz = await Post<CekDto>(c, Yol, Verilen("K-1", 10_000m, Today.AddDays(-3)));
        await Post<CekDto>(c, $"{Yol}/{karsiliksiz.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), karsiliksiz.Surum, CekHareketTurleri.Karsiliksiz, Today, 0m, null, null, null));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(0, 0m), ozet.VerilenVadesiGecmis);
    }

    [Fact]
    public async Task Kismen_odenmis_vadesi_gecmis_verilen_cek_kalan_tutarla_sayilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var verilen = await Post<CekDto>(c, Yol, Verilen("777", 10_000m, Today.AddDays(-5)));
        await Post<CekDto>(c, $"{Yol}/{verilen.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), verilen.Surum, CekHareketTurleri.Odeme, Today, 4_000.5m, null, null, null));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(new CekOzetKalemi(1, 5_999.5m), ozet.VerilenVadesiGecmis);
    }

    [Fact]
    public async Task Bos_ozet_sifirdir_izleyici_okur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        Assert.Equal(new CekOzetDto(Today, new(0, 0m), new(0, 0m), new(0, 0m), new(0, 0m), new(0, 0m)),
            await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken));
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync(Yol + "/ozet", TestContext.Current.CancellationToken)).StatusCode);
    }
}
