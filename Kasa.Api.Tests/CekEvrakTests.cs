using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket D — tek dokunuşla çek durumu (özellik 30) ve çek/senet türü, konumu, ciro edilen cari,
/// risk dağılımı (özellik 43). Kasa kuralı değişmez: aynı kayıt, aynı kasa.
/// </summary>
public class CekEvrakTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public CekEvrakTests(PaketDFactory factory) => _factory = factory;

    private static object Govde(string yon = "Alinan", string durum = "Portfoyde", decimal tutar = 1_000m,
        string? islemTarihi = null, string kisi = "Ahmet Yılmaz", string kanal = "MEZAT", string? banka = "Ziraat",
        string? tur = null, string? konum = null, string? ciroEdilenCari = null, string duzenleme = "2026-09-01")
    {
        // Tür/konum verilmezse gövdede hiç yer almaz (eski istemci gibi): varsayılanlar sunucuda.
        var d = new Dictionary<string, object?>
        {
            ["yon"] = yon, ["cekNo"] = "001", ["banka"] = banka, ["kisi"] = kisi, ["tutar"] = tutar,
            ["duzenlemeTarihi"] = duzenleme, ["vadeTarihi"] = "2026-10-15", ["kanal"] = kanal, ["durum"] = durum,
            ["islemTarihi"] = islemTarihi, ["not"] = "not", ["ciroEdilenCari"] = ciroEdilenCari,
        };
        if (tur is not null) d["tur"] = tur;
        if (konum is not null) d["konum"] = konum;
        return d;
    }

    private static async Task<JsonElement> Ekle(HttpClient c, object govde)
        => await Basarili(await c.PostAsJsonAsync("/api/cekler", govde));

    /// <summary>Kaydın Id dışındaki tüm alanları (tek dokunuş ile tam formun karşılaştırması).</summary>
    private static string IdsizJson(JsonElement e)
        => JsonSerializer.Serialize(e.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value));

    [Theory]
    [InlineData("Alinan", "TahsilEdildi", null)]
    [InlineData("Verilen", "Odendi", null)]
    [InlineData("Alinan", "CiroEdildi", "market")]
    [InlineData("Alinan", "Karsiliksiz", null)]
    public async Task Tek_dokunus_tam_formla_ayni_kaydi_ve_ayni_kasayi_uretir(string yon, string durum, string? ciro)
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var a = await Ekle(c, Govde(yon));
        var b = await Ekle(c, Govde(yon));

        var kasa0 = await GuncelKasa(c);
        var tek = await Basarili(await c.PostAsJsonAsync($"/api/cekler/{a.Id()}/durum", new { durum, ciroEdilenCari = ciro }));
        var kasa1 = await GuncelKasa(c);

        // Tam form: aynı alanlar + aynı durum; tarih bugün (karşılıksızda tarih yok).
        var islemTarihi = durum == "Karsiliksiz" ? null : "2026-09-24";
        var tam = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{b.Id()}",
            Govde(yon, durum, islemTarihi: islemTarihi, ciroEdilenCari: ciro)));
        var kasa2 = await GuncelKasa(c);

        // DB'deki iki kayıt Id dışında birebir aynı.
        var liste = (await GetJson(c, "/api/cekler")).EnumerateArray().ToList();
        Assert.Equal(IdsizJson(liste.Single(x => x.Id() == b.Id())), IdsizJson(liste.Single(x => x.Id() == a.Id())));
        Assert.Equal(tam.Str("durum"), tek.Str("durum"));
        Assert.Equal(kasa2 - kasa1, kasa1 - kasa0);
        var beklenenEtki = durum switch { "TahsilEdildi" => 1_000m, "Odendi" => -1_000m, _ => 0m };
        Assert.Equal(beklenenEtki, kasa1 - kasa0);
        if (ciro is not null) Assert.Equal("Market", tek.Str("ciroEdilenCari"));   // kayıtlı yazım
        if (durum == "Karsiliksiz") Assert.True(tek.Null("islemTarihi"));
        else Assert.Equal("2026-09-24", tek.Str("islemTarihi"));
    }

    [Fact]
    public async Task Tek_dokunus_verilen_tarihi_kullanir_ve_ayni_dogrulamadan_gecer()
    {
        var c = await _factory.EditorClientAsync();
        var a = await Ekle(c, Govde());
        var r = await c.PostAsJsonAsync($"/api/cekler/{a.Id()}/durum", new { durum = "TahsilEdildi", tarih = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("İşlem tarihi düzenleme tarihinden önce olamaz.", await Hata(r));

        var ok = await Basarili(await c.PostAsJsonAsync($"/api/cekler/{a.Id()}/durum", new { durum = "TahsilEdildi", tarih = "2026-09-20" }));
        Assert.Equal("2026-09-20", ok.Str("islemTarihi"));
    }

    [Fact]
    public async Task Tek_dokunus_yalniz_portfoydeki_ve_yone_uygun_evrakta_calisir()
    {
        var c = await _factory.EditorClientAsync();
        var alinan = await Ekle(c, Govde());
        var verilen = await Ekle(c, Govde("Verilen"));

        var r1 = await c.PostAsJsonAsync($"/api/cekler/{verilen.Id()}/durum", new { durum = "TahsilEdildi" });
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Equal("Verilen evrak için yalnız ödendi seçilebilir.", await Hata(r1));
        var r2 = await c.PostAsJsonAsync($"/api/cekler/{alinan.Id()}/durum", new { durum = "Odendi" });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        var r3 = await c.PostAsJsonAsync($"/api/cekler/{alinan.Id()}/durum", new { durum = "IadeEdildi" });
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
        var r4 = await c.PostAsJsonAsync($"/api/cekler/{alinan.Id()}/durum", new { durum = "CiroEdildi", ciroEdilenCari = "  " });
        Assert.Equal("Ciro edilen cariyi seçin.", await Hata(r4));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/cekler/987654/durum", new { durum = "TahsilEdildi" })).StatusCode);

        await Basarili(await c.PostAsJsonAsync($"/api/cekler/{alinan.Id()}/durum", new { durum = "TahsilEdildi" }));
        var r5 = await c.PostAsJsonAsync($"/api/cekler/{alinan.Id()}/durum", new { durum = "Karsiliksiz" });
        Assert.Equal(HttpStatusCode.Conflict, r5.StatusCode);
        Assert.Contains("artık portföyde değil", await Hata(r5));

        var izleyici = await _factory.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync($"/api/cekler/{verilen.Id()}/durum", new { durum = "Odendi" })).StatusCode);
    }

    [Fact]
    public async Task Ciro_edilen_cari_yalniz_ciroda_tutulur_serbest_metin_kirpilir()
    {
        var c = await _factory.EditorClientAsync();
        var a = await Ekle(c, Govde(durum: "CiroEdildi", islemTarihi: "2026-09-10", ciroEdilenCari: "  Yeni Tedarikçi  "));
        Assert.Equal("Yeni Tedarikçi", a.Str("ciroEdilenCari"));
        // Portföye dönünce ciro bilgisi silinir.
        var p = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{a.Id()}", Govde(ciroEdilenCari: "Yeni Tedarikçi")));
        Assert.True(p.Null("ciroEdilenCari"));
        var uzun = await c.PostAsJsonAsync("/api/cekler", Govde(durum: "CiroEdildi", islemTarihi: "2026-09-10", ciroEdilenCari: new string('x', 201)));
        Assert.Equal("Ciro edilen cari en fazla 200 karakter olabilir.", await Hata(uzun));
    }

    [Fact]
    public async Task Eski_istemci_govdesi_cek_ve_elde_varsayilir_verilen_evrak_hep_eldedir()
    {
        var c = await _factory.EditorClientAsync();
        var eski = await Basarili(await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Eski İstemci", tutar = 10m, duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-09-30",
            kanal = "MEZAT", durum = "Portfoyde",
        }));
        Assert.Equal("Cek", eski.Str("tur"));
        Assert.Equal("Elde", eski.Str("konum"));

        var v = await Ekle(c, Govde("Verilen", tur: "Senet", konum: "Icrada"));
        Assert.Equal("Senet", v.Str("tur"));
        Assert.Equal("Elde", v.Str("konum"));

        var bozuk = await c.PostAsJsonAsync("/api/cekler", Govde(tur: "Bono"));
        Assert.Equal(HttpStatusCode.BadRequest, bozuk.StatusCode);
    }

    [Fact]
    public async Task Tur_ve_konum_suzgeci()
    {
        var c = await _factory.EditorClientAsync();
        var senet = await Ekle(c, Govde(tur: "Senet", konum: "Teminatta", kisi: "Süzgeç Senet"));
        await Ekle(c, Govde(tur: "Cek", konum: "Teminatta", kisi: "Süzgeç Çek"));
        await Ekle(c, Govde(tur: "Senet", konum: "Elde", kisi: "Süzgeç Senet 2"));

        var l = (await GetJson(c, "/api/cekler?tur=senet&konum=Teminatta")).EnumerateArray().ToList();
        Assert.Contains(l, x => x.Id() == senet.Id());
        Assert.All(l, x => { Assert.Equal("Senet", x.Str("tur")); Assert.Equal("Teminatta", x.Str("konum")); });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cekler?tur=Bono")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cekler?konum=Kasada")).StatusCode);
    }

    [Fact]
    public async Task Senet_cekle_ayni_kasa_hareketini_uretir()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var k0 = await GuncelKasa(c);
        await Ekle(c, Govde(durum: "TahsilEdildi", islemTarihi: "2026-09-15", tutar: 777.77m, tur: "Cek"));
        var k1 = await GuncelKasa(c);
        await Ekle(c, Govde(durum: "TahsilEdildi", islemTarihi: "2026-09-15", tutar: 777.77m, tur: "Senet", konum: "BankadaTahsilde"));
        var k2 = await GuncelKasa(c);
        await Ekle(c, Govde("Verilen", durum: "Odendi", islemTarihi: "2026-09-16", tutar: 100m, tur: "Senet"));
        var k3 = await GuncelKasa(c);
        Assert.Equal(777.77m, k1 - k0);
        Assert.Equal(k1 - k0, k2 - k1);
        Assert.Equal(-100m, k3 - k2);
    }

    [Fact]
    public async Task Risk_dagilimi_portfoydeki_alinan_evraki_kesideci_ve_bankaya_gore_boler()
    {
        using var f = new PaketDFactory();   // temiz DB: toplamlar kesin
        var c = await f.EditorClientAsync();
        await Ekle(c, Govde(tutar: 600m, kisi: "Ahmet Yılmaz", banka: "Ziraat"));
        await Ekle(c, Govde(tutar: 200m, kisi: "AHMET YILMAZ", banka: "İş Bankası", tur: "Senet", konum: "Icrada"));
        await Ekle(c, Govde(tutar: 200m, kisi: "Mehmet", banka: null));
        await Ekle(c, Govde(tutar: 999m, kisi: "Tahsil", durum: "TahsilEdildi", islemTarihi: "2026-09-10"));   // portföyde değil
        await Ekle(c, Govde("Verilen", tutar: 999m, kisi: "Verilen"));                                         // alınan değil

        var r = await GetJson(c, "/api/cekler/risk");
        Assert.Equal(1_000m, r.Dec("toplam"));
        Assert.Equal(3, r.GetProperty("adet").GetInt32());
        var kesideci = r.GetProperty("kesideciler").EnumerateArray().ToList();
        Assert.Equal(["Ahmet Yılmaz", "Mehmet"], kesideci.Select(k => k.Str("ad")));
        Assert.Equal([800m, 200m], kesideci.Select(k => k.Dec("tutar")));
        Assert.Equal([0.8m, 0.2m], kesideci.Select(k => k.Dec("oran")));
        Assert.Equal(2, kesideci[0].GetProperty("adet").GetInt32());
        var banka = r.GetProperty("bankalar").EnumerateArray().Select(k => (k.Str("ad"), k.Dec("tutar"))).ToList();
        Assert.Equal([("Ziraat", 600m), ("Banka belirtilmemiş", 200m), ("İş Bankası", 200m)], banka);

        var senet = await GetJson(c, "/api/cekler/risk?tur=Senet");
        Assert.Equal(200m, senet.Dec("toplam"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cekler/risk?tur=x")).StatusCode);
        var izleyici = await f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/cekler/risk")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/cekler/risk")).StatusCode);
    }
}
