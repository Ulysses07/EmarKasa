using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket B istemci uçları: doğru yol/sorgu/gövde, JSON çözümü, dosya indirme ve hata metni.</summary>
public class PaketBIstemciTests
{
    private static async Task<(KasaApiClient C, SahteHandler H)> KurAsync()
    {
        var h = new SahteHandler();
        var store = new BellekTokenStore();
        await store.YazAsync("tok");
        return (new KasaApiClient(new HttpClient(h) { BaseAddress = new Uri("https://kasa.emarglobal.com/") }, store), h);
    }

    private sealed class DosyaHandler(byte[] icerik, string tip, string? disposition) : HttpMessageHandler
    {
        public HttpRequestMessage? SonIstek;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SonIstek = request;
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(icerik) };
            r.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(tip);
            if (disposition is not null) r.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(disposition);
            return Task.FromResult(r);
        }
    }

    private static async Task<(KasaApiClient C, DosyaHandler H)> DosyaKurAsync(byte[] icerik, string tip, string? disposition)
    {
        var h = new DosyaHandler(icerik, tip, disposition);
        var store = new BellekTokenStore();
        await store.YazAsync("tok");
        return (new KasaApiClient(new HttpClient(h) { BaseAddress = new Uri("https://kasa.emarglobal.com/") }, store), h);
    }

    [Fact]
    public async Task Kasa_dokumu_sorgusu_ve_cozumu()
    {
        var (c, h) = await KurAsync();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"baslangic":"2026-09-01","bitis":"2026-09-30","acilis":100,"kapanis":150.5,"toplamGiren":60.5,"toplamCikan":10,
             "adimlar":[{"tur":"Gelen","turAdi":"Gelen","kanal":"MEZAT","tutar":60.5,"bakiye":160.5},
                        {"tur":"KartOdemesi","turAdi":"Kart ödemesi","kanal":null,"tutar":-10,"bakiye":150.5}]}
            """);
        var d = await c.KasaDokumuAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal("/api/rapor/kasa-dokumu?baslangic=2026-09-01&bitis=2026-09-30", h.SonIstek!.RequestUri!.PathAndQuery);
        Assert.Equal(150.5m, d.Kapanis);
        Assert.Null(d.Adimlar[1].Kanal);
        Assert.Equal("Kart ödemesi", d.Adimlar[1].TurAdi);
        Assert.Equal("Bearer", h.SonIstek.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Ay_kapanisi_okuma_ve_editor_eylemleri()
    {
        var (c, h) = await KurAsync();
        const string yanit = """
            {"yil":2026,"ay":8,"etiket":"Ağustos 2026","kilitli":true,"kilitZamaniUtc":"2026-09-01T10:00:00Z","kilitlenebilir":true,
             "yayinlandi":true,"yayinZamaniUtc":"2026-09-02T10:00:00Z",
             "farklar":[{"kalem":"MEZAT · Gelen","eski":10,"yeni":null}],
             "degisiklikler":[{"id":5,"zamanUtc":"2026-09-03T10:00:00Z","rol":"editor","tur":"İşlem","kayitId":3,"eylem":"Eklendi","ozet":"x",
                               "eskiJson":null,"yeniJson":"{}","geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":false}]}
            """;
        h.Kuyrukla(HttpStatusCode.OK, yanit);
        var d = await c.AyKapanisiAsync(2026, 8);
        Assert.Equal("/api/ay-kapanisi?yil=2026&ay=8", h.SonIstek!.RequestUri!.PathAndQuery);
        Assert.True(d.YayindanSonraDegisti);
        Assert.Null(d.Farklar[0].Yeni);
        Assert.Equal("İşlem", d.Degisiklikler[0].Tur);

        foreach (var (islem, yol) in new (Func<Task<AyKapanisDto>>, string)[]
                 {
                     (() => c.AyiKilitleAsync(2026, 8), "/api/ay-kapanisi/kilitle"),
                     (() => c.AyKilidiniAcAsync(2026, 8), "/api/ay-kapanisi/kilit-ac"),
                     (() => c.AyiYayinlaAsync(2026, 8), "/api/ay-kapanisi/yayinla"),
                 })
        {
            h.Kuyrukla(HttpStatusCode.OK, yanit);
            await islem();
            Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
            Assert.Equal(yol, h.SonIstek.RequestUri!.AbsolutePath);
            var govde = JsonDocument.Parse(h.SonGovde!).RootElement;
            Assert.Equal(2026, govde.GetProperty("yil").GetInt32());
            Assert.Equal(8, govde.GetProperty("ay").GetInt32());
        }

        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Ağustos 2026 zaten kilitli."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.AyiKilitleAsync(2026, 8));
        Assert.Equal(HttpStatusCode.Conflict, ex.DurumKodu);
        Assert.Equal("Ağustos 2026 zaten kilitli.", ex.SunucuMesaji);

        h.Kuyrukla(HttpStatusCode.OK, """[{"yil":2026,"ay":8,"etiket":"Ağustos 2026","kilitZamaniUtc":"2026-09-01T10:00:00Z"}]""");
        Assert.Equal("Ağustos 2026", (await c.AyKilitleriAsync()).Single().Etiket);
    }

    [Fact]
    public void Kirmizi_serit_yalniz_rakam_degisince_notr_not_yalniz_dokunulunca()
    {
        var satir = new DegisiklikDto(5, new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc), "editor", "İşlem", 3, "Güncellendi", "x",
            null, "{}", false, null, false);
        AyKapanisDto Durum(bool yayin, int fark, int satirSayisi) => new(2026, 8, "Ağustos 2026", true, null, true, yayin, null,
            Enumerable.Repeat(new AyFarkiDto("MEZAT · Gelen", 10, 20), fark).ToList(), Enumerable.Repeat(satir, satirSayisi).ToList());

        Assert.True(Durum(true, 1, 1).YayindanSonraDegisti);
        Assert.False(Durum(true, 1, 1).YayindanSonraDokunuldu);
        Assert.False(Durum(true, 0, 1).YayindanSonraDegisti);                  // yalnız not/sayım: kırmızı değil
        Assert.True(Durum(true, 0, 1).YayindanSonraDokunuldu);
        Assert.False(Durum(true, 0, 0).YayindanSonraDegisti || Durum(true, 0, 0).YayindanSonraDokunuldu);
        Assert.False(Durum(false, 1, 1).YayindanSonraDegisti || Durum(false, 0, 1).YayindanSonraDokunuldu);
    }

    [Fact]
    public async Task Tipli_islem_sayfasi_ve_csv_tip_sorgusunu_ekler()
    {
        var (c, h) = await KurAsync();
        h.Kuyrukla(HttpStatusCode.OK, "[]", ("X-Toplam-Kayit", "7"));
        var s = await c.IslemSayfasiTipeGoreAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "Ortak", IslemTipSuzgeci.Nakit, 500, 500);
        Assert.Equal("/api/islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=Ortak&limit=500&offset=500&tip=Nakit",
            h.SonIstek!.RequestUri!.PathAndQuery);
        Assert.Equal(7, s.Toplam);

        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.IslemSayfasiTipeGoreAsync(null, null, null, IslemTipSuzgeci.KrediKarti, 500, 0);
        Assert.Equal("/api/islemler?limit=500&offset=0&tip=KrediKarti", h.SonIstek!.RequestUri!.PathAndQuery);

        var (c2, h2) = await DosyaKurAsync([0x41], "text/csv", null);
        var d = await c2.IslemlerCsvTipeGoreAsync(new DateOnly(2026, 8, 1), null, "MEZAT", IslemTipSuzgeci.SabitGider);
        Assert.Equal("/api/disaaktar/islemler.csv?baslangic=2026-08-01&kanal=MEZAT&tip=SabitGider", h2.SonIstek!.RequestUri!.PathAndQuery);
        Assert.Equal("kasa-islemler.csv", d.DosyaAdi);
        await c2.IslemlerCsvTipeGoreAsync(null, null, null, IslemTipSuzgeci.Cari);
        Assert.Equal("/api/disaaktar/islemler.csv?tip=Cari", h2.SonIstek!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Kur_hedef_butce_ve_cari_ozeti_govde_ve_sorgu()
    {
        var (c, h) = await KurAsync();
        h.Kuyrukla(HttpStatusCode.OK, """{"ay":"2026-08-01","tufeEndeksi":null,"usdTry":41.1234,"eurTry":null,"altinGramTry":null}""");
        var k = await c.KurKaydetAsync(new KurDto(new DateOnly(2026, 8, 1), null, 41.1234m, null, null));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.Contains("\"usdTry\":41.1234", h.SonGovde);
        Assert.Contains("\"tufeEndeksi\":null", h.SonGovde);
        Assert.Equal(41.1234m, k.UsdTry);

        h.Kuyrukla(HttpStatusCode.OK, """{"kur":{"ay":"2026-08-01","tufeEndeksi":null,"usdTry":41,"eurTry":45,"altinGramTry":null},"gunSayisi":20,"ilkGun":"2026-08-03","sonGun":"2026-08-31"}""");
        var t = await c.KurTcmbDoldurAsync(new DateOnly(2026, 8, 1));
        Assert.Equal("/api/kurlar/tcmb", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Contains("\"ay\":\"2026-08-01\"", h.SonGovde);
        Assert.Equal(20, t.GunSayisi);

        h.Kuyrukla(HttpStatusCode.BadGateway, """{"hata":"TCMB'ye ulaşılamadı (bağlantı hatası)."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.KurTcmbDoldurAsync(new DateOnly(2026, 8, 1)));
        Assert.Equal("TCMB'ye ulaşılamadı (bağlantı hatası).", ex.SunucuMesaji);

        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":8,"kanallar":[],"kalemler":[]}""");
        await c.HedefButceKaydetAsync(new HedefButceYaz(new DateOnly(2026, 8, 1), [new HedefYaz(3, 1500.5m), new HedefYaz(4, null)], null));
        var g = JsonDocument.Parse(h.SonGovde!).RootElement;
        Assert.Equal(1500.5m, g.GetProperty("kanallar")[0].GetProperty("tutar").GetDecimal());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("kanallar")[1].GetProperty("tutar").ValueKind);

        h.Kuyrukla(HttpStatusCode.OK, """{"kopyalanan":2,"atlanan":1}""");
        Assert.Equal(2, (await c.HedefButceKopyalaAsync(new DateOnly(2026, 8, 1))).Kopyalanan);
        Assert.Equal("/api/hedef-butce/kopyala", h.SonIstek!.RequestUri!.AbsolutePath);

        h.Kuyrukla(HttpStatusCode.OK, """{"ad":"PORT KARGO","yil":2026,"tur":"kalem","aylar":[],"toplam":0,"sablonToplam":null}""");
        await c.CariOzetiAsync("Kira & Aidat", 2026, CariOzetiTuru.Kalem);
        Assert.Equal("/api/rapor/cari-ozeti?ad=Kira%20%26%20Aidat&yil=2026&tur=kalem", h.SonIstek!.RequestUri!.PathAndQuery);

        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"kanallar":["MEZAT"],"aylar":[{"yil":2026,"ay":9,"takipOncesi":false,"kanallar":[{"kanal":"MEZAT","gelir":10,"aySonucu":5}],"tufeEndeksi":null,"usdTry":null,"eurTry":null,"altinGramTry":null}]}""");
        var gr = await c.GrafikAsync(2026, 9);
        Assert.Equal("/api/rapor/grafik?yil=2026&ay=9", h.SonIstek!.RequestUri!.PathAndQuery);
        Assert.Equal(5m, gr.Aylar[0].Kanallar[0].AySonucu);
    }

    [Fact]
    public async Task Dosya_uclari_sunucunun_adini_ve_baytlari_doner()
    {
        var html = Encoding.UTF8.GetBytes("<!DOCTYPE html>");
        var (c, h) = await DosyaKurAsync(html, "text/html; charset=utf-8", "attachment; filename=kasa-aylik-rapor-2026-08.html");
        var d = await c.AylikYazdirAsync(2026, 8);
        Assert.Equal("kasa-aylik-rapor-2026-08.html", d.DosyaAdi);
        Assert.Equal(html, d.Icerik);
        Assert.Equal("/api/rapor/aylik-yazdir?yil=2026&ay=8", h.SonIstek!.RequestUri!.PathAndQuery);

        var (c2, h2) = await DosyaKurAsync([0x50, 0x4B], "application/zip", null);
        var z = await c2.AyPaketiAsync(2026, 8);
        Assert.Equal("kasa-ay-paketi-2026-08.zip", z.DosyaAdi);   // başlık yoksa varsayılan ad
        Assert.Equal("/api/disaaktar/ay-paketi.zip?yil=2026&ay=8", h2.SonIstek!.RequestUri!.PathAndQuery);

        await c2.CeklerCsvAsync(CekYonu.Verilen, CekDurumu.Portfoyde, new DateOnly(2026, 8, 1), null);
        Assert.Equal("/api/disaaktar/cekler.csv?yon=Verilen&durum=Portfoyde&baslangic=2026-08-01", h2.SonIstek!.RequestUri!.PathAndQuery);
        await c2.CeklerCsvAsync();
        Assert.Equal("/api/disaaktar/cekler.csv", h2.SonIstek!.RequestUri!.PathAndQuery);
        // Paket D süzgeçleri (Çekler sayfasının tür/konum çipleri) dosyaya da gider.
        await c2.CeklerCsvAsync(CekYonu.Alinan, tur: CekTuru.Senet, konum: CekKonumu.BankadaTahsilde);
        Assert.Equal("/api/disaaktar/cekler.csv?yon=Alinan&tur=Senet&konum=BankadaTahsilde", h2.SonIstek!.RequestUri!.PathAndQuery);
        Assert.Equal("kasa-sayimlari.csv", (await c2.KasaSayimlariCsvAsync()).DosyaAdi);
        Assert.Equal("/api/disaaktar/kasasayimlari.csv", h2.SonIstek!.RequestUri!.PathAndQuery);
        await c2.GecmisCsvAsync("Kart ödemesi");
        Assert.Equal("/api/disaaktar/gecmis.csv?tur=Kart%20%C3%B6demesi", h2.SonIstek!.RequestUri!.PathAndQuery);
        await c2.GecmisCsvAsync();
        Assert.Equal("/api/disaaktar/gecmis.csv", h2.SonIstek!.RequestUri!.PathAndQuery);
        await c2.KasaDokumuCsvAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        Assert.Equal("/api/disaaktar/kasa-dokumu.csv?baslangic=2026-08-01&bitis=2026-08-31", h2.SonIstek!.RequestUri!.PathAndQuery);
    }
}
