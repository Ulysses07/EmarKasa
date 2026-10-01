using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çek uçları (/api/takip/cekler; docs/specs/2026-10-01-cekler.md "Uçlar"): ekleme, düzeltme, silme, hareket ve geri alma; istekId
/// tekrar koruması, sürüm çakışması, aynı çek uyarısı, geçiş kuralları, ay kilidi ve yetki. Bugün 25 Eylül 2026 (sabit saat),
/// takip başlangıcı 1 Haziran 2026, açılış kasası 1.000; kanallar MEZAT, PERAKENDE, TOPTAN.
/// </summary>
public class CekUcTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekYaz Alinan(string no = "12345", decimal tutar = 50_000m, bool teminat = false, DateOnly? vade = null) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, no, "Ziraat", "Ahmet Yılmaz", tutar, vade ?? Today.AddDays(20), null, teminat, null, null);

    private static CekYaz Verilen(string kanal = KanalEtiketleri.Ortak, decimal tutar = 30_000m, DateOnly? vade = null) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, "777", "Halk", "Mehmet Ticaret", tutar, vade ?? Today.AddDays(10), kanal, false, null, null);

    private static CekHareketYaz Hareket(CekDto cek, string tur, decimal tutar = 0m, DateOnly? tarih = null, string? kanal = null, decimal? net = null, string? karsi = null) =>
        new(Guid.NewGuid(), cek.Surum, tur, tarih ?? Today, tutar, net, kanal, karsi);

    private static async Task<(HttpStatusCode Durum, string Govde)> Gonder(HttpClient c, HttpMethod yontem, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(yontem, yol) { Content = JsonContent.Create(govde) };
        using var yanit = await c.SendAsync(istek, TestContext.Current.CancellationToken);
        return (yanit.StatusCode, await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static string Hata(string govde) => JsonDocument.Parse(govde).RootElement.GetProperty("hata").GetString()!;

    private static async Task<T> Delete<T>(HttpClient c, string yol, object govde)
    {
        var (durum, metin) = await Gonder(c, HttpMethod.Delete, yol, govde);
        Assert.True(durum == HttpStatusCode.OK, $"{durum}: {metin}");
        return JsonSerializer.Deserialize<T>(metin, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public async Task Alinan_cek_eklenir_kismi_tahsil_edilir_son_hareket_geri_alinir_cek_silinir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var istek = Alinan();
        var cek = await Post<CekDto>(c, Yol, istek);
        Assert.Equal((1, CekDurumlari.Portfoyde, 50_000m, CekKonumlari.Elde, (string?)null, (string?)null), (cek.Surum, cek.Durum, cek.Kalan, cek.Konum, cek.Kanal, cek.Uyari));
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade], cek.IzinliHareketler);
        // Aynı istek kimliği ikinci çek oluşturmaz.
        Assert.Equal(cek.Id, (await Post<CekDto>(c, Yol, istek)).Id);
        Assert.Single((await c.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!);

        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 20_000m, kanal: "MEZAT"));
        Assert.Equal((CekDurumlari.KismenTahsilEdildi, 30_000m, 2), (cek.Durum, cek.Kalan, cek.Surum));
        var hareket = Assert.Single(cek.Hareketler);
        Assert.Equal((1, CekHareketTurleri.Tahsilat, 20_000m, "MEZAT"), (hareket.Sira, hareket.Tur, hareket.Tutar, hareket.Kanal));
        Assert.Equal(21_000m, (await Panel(c)).GuncelKasa);

        cek = await Delete<CekDto>(c, $"{Yol}/{cek.Id}/hareketler/son", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m), (cek.Durum, cek.Kalan));
        Assert.Empty(cek.Hareketler);
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);

        var (durum, _) = await Gonder(c, HttpMethod.Delete, $"{Yol}/{cek.Id}", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(HttpStatusCode.NoContent, durum);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Yol}/{cek.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Duzeltme_surumle_yapilir_eski_surum_409_konum_ve_vade_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        var duzelt = Alinan() with { Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde, VadeTarihi = Today.AddDays(45) };
        var yeni = (await c.PutAsJsonAsync($"{Yol}/{cek.Id}", duzelt, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, yeni.StatusCode);
        var sonra = (await yeni.Content.ReadFromJsonAsync<CekDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal((CekKonumlari.BankadaTahsilde, Today.AddDays(45), 2), (sonra.Konum, sonra.VadeTarihi, sonra.Surum));
        var (durum, govde) = await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", duzelt with { IstekId = Guid.NewGuid(), Konum = CekKonumlari.Icrada });
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.Equal("Çek başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.", Hata(govde));
    }

    [Fact]
    public async Task Ayni_yon_banka_ve_no_uyari_dondurur_kayit_yine_yapilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var ilk = await Post<CekDto>(c, Yol, Alinan());
        var ikinci = await Post<CekDto>(c, Yol, Alinan() with { Banka = " ziraat " });
        Assert.Equal($"Aynı yön, banka ve numarayla kayıtlı başka çek var: #{ilk.Id}.", ikinci.Uyari);
        Assert.Equal(2, (await c.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!.Count);
        Assert.Null((await Post<CekDto>(c, Yol, Verilen())).Uyari);
    }

    [Fact]
    public async Task Gecis_ve_tutar_kurallari_turkce_iletiyle_reddedilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var verilen = await Post<CekDto>(c, Yol, Verilen());
        var (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Ciro, 30_000m, karsi: "X"));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.Equal("Bu kayıt portföyde; şu an yalnız şu hareketler girilebilir: Ödeme, Karşılıksız, İade.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 30_000.01m));
        Assert.Equal(HttpStatusCode.BadRequest, durum);
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (30.000,00 TL) aşamaz.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 1m, kanal: "MEZAT"));
        Assert.Equal("Verilen çek çekin kasasından ödenir; harekette kasa seçilmez.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 1m, tarih: Today.AddDays(1)));
        Assert.Equal("Hareket tarihi takip başlangıcı (01.06.2026) ile bugün arasında olmalı.", Hata(govde));
        // Takip başlangıcından önceki tarih de reddedilir (alt sınır).
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 1m, tarih: Month.AddMonths(-3).AddDays(-1)));
        Assert.Equal("Hareket tarihi takip başlangıcı (01.06.2026) ile bugün arasında olmalı.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, Yol, Alinan() with { Kanal = "MEZAT" });
        Assert.Equal("Alınan çekte kasa tahsilat, ciro ya da kırdırma hareketinde seçilir.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, Yol, Verilen(kanal: ""));
        Assert.Equal("Verilen çekin ödeneceği kasayı (kanal ya da Ortak) seçin.", Hata(govde));

        var alinan = await Post<CekDto>(c, Yol, Alinan());
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{alinan.Id}/hareketler", Hareket(alinan, CekHareketTurleri.Tahsilat, 10m));
        Assert.Equal("Tahsilatın, cironun ya da kırdırmanın kasasını (kanal) seçin.", Hata(govde));
    }

    [Fact]
    public async Task Ciro_ve_donus_ayni_kasada_ters_satir_uretir_donus_kasasi_cirodan_gelir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Ciro, 50_000m, Today.AddDays(-5), "PERAKENDE", karsi: "Veli Toptan"));
        Assert.Equal(CekDurumlari.CiroEdildi, cek.Durum);
        Assert.Equal([CekHareketTurleri.Donus], cek.IzinliHareketler);
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Donus, 50_000m));
        Assert.Equal(CekDurumlari.Karsiliksiz, cek.Durum);
        Assert.Equal(("PERAKENDE", "Veli Toptan"), (cek.Hareketler[1].Kanal, cek.Hareketler[1].Karsi));
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 50_000m, kanal: "MEZAT"));
        Assert.Equal(CekDurumlari.TahsilEdildi, cek.Durum);
        Assert.Equal(51_000m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kapatilmis_aydaki_hareket_geri_alinamaz_aya_hareket_girilemez_tutar_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var agustos = Month.AddMonths(-1);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 1_000m, agustos.AddDays(5), "MEZAT"));
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, agustos.Year, agustos.Month, "Ay tamamlandı"));

        var kilitIletisi = $"{agustos.AddMonths(1).AddDays(-1):yyyy-MM-dd} tarihine kadar dönem kilitli.";
        var (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 1_000m, agustos.AddDays(20), "MEZAT"));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        (durum, govde) = await Gonder(c, HttpMethod.Delete, $"{Yol}/{cek.Id}/hareketler/son", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        (durum, govde) = await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", Alinan(tutar: 60_000m) with { Surum = cek.Surum });
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", Alinan() with { Surum = cek.Surum, Not = "Not eklendi" })).Durum);
    }

    [Fact]
    public async Task Liste_yon_durum_arama_ve_vade_suzgecleriyle_vadeye_gore_siralanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var yakin = await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, vade: Today.AddDays(5)));
        await Post<CekDto>(c, Yol, Alinan("A-2", 20_000m, vade: Today.AddDays(60)));
        await Post<CekDto>(c, Yol, Alinan("A-3", 40_000m, teminat: true, vade: Today.AddDays(3)));
        var gecmis = await Post<CekDto>(c, Yol, Alinan("A-4", 5_000m, vade: Today.AddDays(-2)));
        await Post<CekDto>(c, Yol, Verilen(tutar: 7_000m, vade: Today.AddDays(30)));
        var kapali = await Post<CekDto>(c, Yol, Alinan("A-5", 1_000m, vade: Today.AddDays(1)));
        await Post<CekDto>(c, $"{Yol}/{kapali.Id}/hareketler", Hareket(kapali, CekHareketTurleri.Iade));
        await Post<CekDto>(c, $"{Yol}/{gecmis.Id}/hareketler", Hareket(gecmis, CekHareketTurleri.Tahsilat, 2_000m, kanal: "MEZAT"));

        async Task<List<string>> Nolar(string sorgu) =>
            (await c.GetFromJsonAsync<List<CekDto>>(Yol + sorgu, TestContext.Current.CancellationToken))!.Select(x => x.No).ToList();
        Assert.Equal(["A-4", "A-3", "A-1", "A-2"], await Nolar($"?yon={CekYonleri.Alinan}&durum={CekSuzgecleri.Portfoyde}"));
        Assert.Equal(["A-5"], await Nolar($"?durum={CekSuzgecleri.Kapanan}"));
        Assert.Equal(["777"], await Nolar($"?yon={CekYonleri.Verilen}"));
        Assert.Equal(["A-1"], await Nolar($"?ara=a-1"));
        Assert.Equal(["777"], await Nolar("?ara=mehmet"));
        Assert.Equal(["A-3", "A-1"], await Nolar($"?yon={CekYonleri.Alinan}&durum={CekSuzgecleri.Portfoyde}&vadeBas={Today:yyyy-MM-dd}&vadeSon={Today.AddDays(30):yyyy-MM-dd}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Yol + "?durum=Yanlis", TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(yakin.Id, (await c.GetFromJsonAsync<CekDto>($"{Yol}/{yakin.Id}", TestContext.Current.CancellationToken))!.Id);
    }

    [Fact]
    public async Task Izleyici_cekleri_okur_yazamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan());
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-cek-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-cek-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Single((await izleyici.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync(Yol, Alinan(), TestContext.Current.CancellationToken)).StatusCode);
    }
}
