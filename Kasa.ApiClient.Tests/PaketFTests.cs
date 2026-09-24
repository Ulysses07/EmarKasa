using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket F istemci metotları: belge alanları, ekler, fatura takibi, muhasebeci CSV'si, POS.</summary>
public class PaketFTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    private const string IslemJson = """{"id":5,"tarih":"2026-09-05","cari":"Market","tutarTl":10.0,"kanal":"MEZAT","tip":"Cari","not":null,"krediKartiId":null,"belgeTuru":"EArsiv","belgeNo":"EA1","faturaBekleniyor":true}""";

    [Fact]
    public async Task Belge_verilmezse_govdeye_belge_alani_yazilmaz_eski_davranis()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, IslemJson);
        await c.IslemGuncelleAsync(5, new IslemYaz(new DateOnly(2026, 9, 5), "Market", 10m, "MEZAT", GiderTipi.Cari, null));
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.False(doc.RootElement.TryGetProperty("belgeTuru", out _));
        Assert.False(doc.RootElement.TryGetProperty("belgeNo", out _));
        Assert.False(doc.RootElement.TryGetProperty("faturaBekleniyor", out _));
        Assert.False(doc.RootElement.TryGetProperty("belge", out _));
        Assert.Equal("Market", doc.RootElement.GetProperty("cari").GetString());
    }

    [Fact]
    public async Task Belge_verilirse_uc_alan_da_bos_olanlar_dahil_gonderilir_yanit_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, IslemJson);
        var dto = await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 9, 5), "Market", 10m, "MEZAT", GiderTipi.Cari, null)
        {
            Belge = new BelgeBilgisi(BelgeTuru.EArsiv, null, true),
        });
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("EArsiv", doc.RootElement.GetProperty("belgeTuru").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("belgeNo").ValueKind);
        Assert.True(doc.RootElement.GetProperty("faturaBekleniyor").GetBoolean());

        Assert.Equal(new BelgeBilgisi(BelgeTuru.EArsiv, "EA1", true), dto.Belge);

        // "Belirtilmedi": tür null olarak gider (sunucu temizler).
        h.Kuyrukla(HttpStatusCode.OK, IslemJson);
        await c.IslemGuncelleAsync(5, new IslemYaz(new DateOnly(2026, 9, 5), "Market", 10m, "MEZAT", GiderTipi.Cari, null) { Belge = BelgeBilgisi.Bos });
        using var doc2 = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(JsonValueKind.Null, doc2.RootElement.GetProperty("belgeTuru").ValueKind);
        Assert.False(doc2.RootElement.GetProperty("faturaBekleniyor").GetBoolean());
    }

    [Fact]
    public async Task Eski_sunucu_yaniti_belge_alansiz_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"tarih":"2026-09-05","cari":"A","tutarTl":1.0,"kanal":"MEZAT","tip":"Cari","not":null}]""");
        var l = await c.IslemlerAsync();
        Assert.Equal(BelgeBilgisi.Bos, l.Single().Belge);
        Assert.Equal((0, false, false), (l.Single().EkSayisi, l.Single().EkVar, l.Single().BelgeVar));
    }

    [Fact]
    public async Task Islem_listesi_ek_sayisini_ve_belgeyi_okur_gorunum_alanlari_json_a_yazilmaz()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"tarih":"2026-09-05","cari":"A","tutarTl":1.0,"kanal":"MEZAT","tip":"Cari","not":null,"belgeTuru":"Fis","belgeNo":"F-7","ekSayisi":3},
             {"id":2,"tarih":"2026-09-05","cari":"B","tutarTl":2.0,"kanal":"MEZAT","tip":"Cari","not":null,"faturaBekleniyor":true},
             {"id":3,"tarih":"2026-09-05","cari":"C","tutarTl":3.0,"kanal":"MEZAT","tip":"Cari","not":null,"belgeNo":"  "}]
            """);
        var l = await c.IslemlerAsync();
        Assert.Equal((3, true, "Ekler (3)", true), (l[0].EkSayisi, l[0].EkVar, l[0].EkEtiketi, l[0].BelgeVar));
        Assert.Equal((0, false, true), (l[1].EkSayisi, l[1].EkVar, l[1].BelgeVar));
        Assert.False(l[2].BelgeVar);   // yalnız boşluktan oluşan no belge sayılmaz

        // Görünüm alanları (ekVar, ekEtiketi, belgeVar, belge) JSON'a karışmaz.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(l[0], new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(3, doc.RootElement.GetProperty("ekSayisi").GetInt32());
        foreach (var ad in new[] { "ekVar", "ekEtiketi", "belgeVar", "belge" })
            Assert.False(doc.RootElement.TryGetProperty(ad, out _), ad);
    }

    [Fact]
    public async Task Ek_yukleme_multipart_dosya_alanini_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":3,"islemId":5,"ad":"fiş.jpg","icerikTipi":"image/jpeg","boyut":4,"yuklemeZamaniUtc":"2026-09-24T09:00:00Z"}""");
        var ek = await c.EkYukleAsync(5, "fiş.jpg", [0xFF, 0xD8, 0xFF, 0xE0]);
        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/islemler/5/ekler", h.SonIstek.RequestUri!.AbsolutePath);
        Assert.Equal("multipart/form-data", h.SonIstek.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("name=dosya", h.SonGovde);
        Assert.Contains("fi%C5%9F.jpg", h.SonGovde);   // filename* (UTF-8)
        Assert.Equal((3, 5, "fiş.jpg", false), (ek.Id, ek.IslemId, ek.Ad, ek.PdfMi));
    }

    [Fact]
    public async Task Ek_yukleme_hatasi_sunucu_mesajiyla_firlatilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"Dosya en fazla 10 MB olabilir."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.EkYukleAsync(5, "a.pdf", [1]));
        Assert.Equal("Dosya en fazla 10 MB olabilir.", ex.Message);
    }

    [Fact]
    public async Task Ek_listele_indir_sil_yollari()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":3,"islemId":5,"ad":"a.pdf","icerikTipi":"application/pdf","boyut":4,"yuklemeZamaniUtc":"2026-09-24T09:00:00Z"}]""");
        var l = await c.EklerAsync(5);
        Assert.True(l.Single().PdfMi);
        Assert.EndsWith("/api/islemler/5/ekler", h.SonIstek!.RequestUri!.AbsolutePath);

        var yanit = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        yanit.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse("attachment; filename=a.pdf; filename*=UTF-8''fi%C5%9F.pdf");
        var h2 = new TekYanit(yanit);
        var c2 = new KasaApiClient(new HttpClient(h2) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore());
        var d = await c2.EkIndirAsync(3);
        Assert.Equal("fiş.pdf", d.DosyaAdi);
        Assert.Equal(new byte[] { 1, 2, 3 }, d.Icerik);
        Assert.EndsWith("/api/ekler/3", h2.Son!.RequestUri!.AbsolutePath);

        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.EkSilAsync(3);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/ekler/3", h.SonIstek.RequestUri!.AbsolutePath);
    }

    private sealed class TekYanit(HttpResponseMessage yanit) : HttpMessageHandler
    {
        public HttpRequestMessage? Son;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Son = request; return Task.FromResult(yanit); }
    }

    [Fact]
    public async Task Belge_guncelle_fatura_takibi_ve_muhasebeci_csv()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, IslemJson);
        await c.BelgeGuncelleAsync(5, new BelgeBilgisi(BelgeTuru.EFatura, "F1", false));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/islemler/5/belge", h.SonIstek.RequestUri!.AbsolutePath);
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.Equal("EFatura", doc.RootElement.GetProperty("belgeTuru").GetString());
            Assert.Equal("F1", doc.RootElement.GetProperty("belgeNo").GetString());
            Assert.False(doc.RootElement.GetProperty("faturaBekleniyor").GetBoolean());
        }

        h.Kuyrukla(HttpStatusCode.OK, """
            {"yil":2026,"ay":9,"bekleyenler":[{"cari":"B","toplam":150.0,"adet":2,"enEskiTarih":"2026-08-20","islemler":[
              {"id":1,"tarih":"2026-08-20","cari":"B","tutarTl":100.0,"kanal":"MEZAT","tip":"Cari","krediKartiId":null,"belgeTuru":null,"belgeNo":null,"faturaBekleniyor":true,"ekSayisi":1,"not":null}]}],
             "bekleyenToplam":150.0,"bekleyenAdet":2,
             "ayOzeti":[{"tur":"Belgesiz","ad":"Belgesiz","toplam":50.0,"adet":2},{"tur":null,"ad":"Belirtilmemiş","toplam":1.0,"adet":1}],
             "ayToplam":51.0,"ayAdet":3,"ayBelgesizToplam":50.0,"ayBelgesizAdet":2}
            """);
        var f = await c.FaturaTakibiAsync(2026, 9);
        Assert.Equal("/api/faturatakibi", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?yil=2026&ay=9", h.SonIstek.RequestUri.Query);
        Assert.Equal(1, f.Bekleyenler[0].Islemler[0].EkSayisi);
        Assert.Equal(BelgeTuru.Belgesiz, f.AyOzeti[0].Tur);
        Assert.Null(f.AyOzeti[1].Tur);
        Assert.Equal(50m, f.AyBelgesizToplam);

        var yanit = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("a;b")) };
        var h2 = new TekYanit(yanit);
        var c2 = new KasaApiClient(new HttpClient(h2) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore());
        var csv = await c2.MuhasebeciCsvAsync(2026, 9);
        Assert.Equal("kasa-muhasebeci-2026-09.csv", csv.DosyaAdi);   // başlık yoksa varsayılan ad
        Assert.Equal("?yil=2026&ay=9", h2.Son!.RequestUri!.Query);
    }

    [Fact]
    public async Task Pos_metotlari_dogru_yol_ve_govde()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":2,"ad":"iyzico","saglayici":"Iyzico","kanalId":1,"kanalAd":"MEZAT","komisyonOrani":2.49,"blokajGunu":7,"aktif":true}""");
        var t = await c.PosTanimOlusturAsync(new PosTanimYaz("iyzico", PosSaglayici.Iyzico, 1, 2.49m, 7));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.Equal("Iyzico", doc.RootElement.GetProperty("saglayici").GetString());
            Assert.Equal(2.49m, doc.RootElement.GetProperty("komisyonOrani").GetDecimal());
        }
        Assert.Equal((PosSaglayici.Iyzico, "MEZAT"), (t.Saglayici, t.KanalAd));

        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.PosSatislariAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 2);
        Assert.Equal("?baslangic=2026-09-01&bitis=2026-09-30&posId=2", h.SonIstek!.RequestUri!.Query);
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.PosSatislariAsync();
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.Created, """{"id":9,"tarih":"2026-09-20","posId":2,"posAd":"iyzico","kanal":"MEZAT","brutTutar":1000.0,"komisyonOrani":2.49,"komisyon":24.9,"net":975.1,"blokajGunu":7,"valor":"2026-09-27","bloke":true,"not":null}""");
        var s = await c.PosSatisOlusturAsync(new PosSatisYaz(new DateOnly(2026, 9, 20), 2, 1000m, null, null, null));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("komisyonOrani").ValueKind);
        Assert.Equal((975.1m, new DateOnly(2026, 9, 27), true), (s.Net, s.Valor, s.Bloke));

        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"bugun":"2026-09-24","blokeNet":975.1,"blokeAdet":1,"valorler":[{"valor":"2026-09-27","net":975.1,"adet":1}],"kanallar":[{"kanal":"MEZAT","brut":1000.0,"komisyon":24.9,"net":975.1,"adet":1}],"toplamBrut":1000.0,"toplamKomisyon":24.9,"toplamNet":975.1}""");
        var o = await c.PosOzetAsync(2026, 9);
        Assert.Equal("?yil=2026&ay=9", h.SonIstek!.RequestUri!.Query);
        Assert.Equal(975.1m, o.BlokeNet);
        Assert.Null(o.BlokeKanallar);   // alanı bilmeyen sunucu: boş döküm

        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.PosTanimSilAsync(2);
        Assert.EndsWith("/api/pos/tanimlar/2", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.PosSatisSilAsync(9);
        Assert.EndsWith("/api/pos/satislar/9", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.OK, """{"id":9,"tarih":"2026-09-20","posId":2,"posAd":"iyzico","kanal":"MEZAT","brutTutar":10.0,"komisyonOrani":0,"komisyon":0,"net":10.0,"blokajGunu":0,"valor":"2026-09-20","bloke":false,"not":null}""");
        await c.PosSatisGuncelleAsync(9, new PosSatisYaz(new DateOnly(2026, 9, 20), 2, 10m, 0m, 0, null));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        h.Kuyrukla(HttpStatusCode.OK, """{"id":2,"ad":"iyzico","saglayici":"Iyzico","kanalId":null,"kanalAd":null,"komisyonOrani":2.49,"blokajGunu":7,"aktif":false}""");
        var g = await c.PosTanimGuncelleAsync(2, new PosTanimYaz("iyzico", PosSaglayici.Iyzico, null, 2.49m, 7, false));
        Assert.False(g.Aktif);
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        Assert.Empty(await c.PosTanimlariAsync());
    }

    [Fact]
    public async Task Pos_ozeti_kanal_kanal_blokeyi_okur_tanim_guncellemesi_eski_satislara_uygula_isaretini_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"bugun":"2026-09-24","blokeNet":1020.0,"blokeAdet":2,"valorler":[],"kanallar":[],"toplamBrut":0,"toplamKomisyon":0,"toplamNet":0,"blokeKanallar":[{"kanal":"MEZAT","net":980.0,"adet":1},{"kanal":"Kanalsız","net":40.0,"adet":1}]}""");
        var o = await c.PosOzetAsync(2026, 9);
        Assert.Equal([new PosKanalBlokeDto("MEZAT", 980m, 1), new PosKanalBlokeDto("Kanalsız", 40m, 1)], o.BlokeKanallar);

        h.Kuyrukla(HttpStatusCode.OK, """{"id":2,"ad":"iyzico","saglayici":"Iyzico","kanalId":3,"kanalAd":"TOPTAN","komisyonOrani":2.49,"blokajGunu":7,"aktif":true}""");
        await c.PosTanimGuncelleAsync(2, new PosTanimYaz("iyzico", PosSaglayici.Iyzico, 3, 2.49m, 7, EskiSatislaraUygula: true));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.True(doc.RootElement.GetProperty("eskiSatislaraUygula").GetBoolean());
        h.Kuyrukla(HttpStatusCode.OK, """{"id":2,"ad":"iyzico","saglayici":"Iyzico","kanalId":3,"kanalAd":"TOPTAN","komisyonOrani":2.49,"blokajGunu":7,"aktif":true}""");
        await c.PosTanimGuncelleAsync(2, new PosTanimYaz("iyzico", PosSaglayici.Iyzico, 3, 2.49m, 7));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.False(doc.RootElement.GetProperty("eskiSatislaraUygula").GetBoolean());
    }
}
