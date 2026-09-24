using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket C istemci metotları: yol/sorgu kodlaması, 204/404/409/400 dallanması.</summary>
public class HizliGirisTests
{
    private static (KasaApiClient c, SahteHandler h, BellekTokenStore t) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        var t = new BellekTokenStore();
        return (new KasaApiClient(http, t), h, t);
    }

    private static readonly IslemYaz Taslak = new(new DateOnly(2026, 9, 24), "Market", 1_250.5m, "MEZAT", GiderTipi.Cari, "not");

    [Fact]
    public async Task Gelismis_arama_eski_ve_yeni_suzgecleri_invariant_kodlar()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]", ("X-Toplam-Kayit", "7"));
        var s = await c.IslemAraAsync(new IslemAramasi(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "MEZAT", "Şahin",
            NotAra: " iade ", Tip: GiderTipi.KrediKarti, KartId: 3, MinTutar: 1_000.5m, MaxTutar: 2_000m), 50, 100);
        Assert.Equal(7, s.Toplam);
        var q = Uri.UnescapeDataString(h.SonIstek!.RequestUri!.Query);
        Assert.Equal("?baslangic=2026-09-01&bitis=2026-09-30&kanal=MEZAT&cari=Şahin&limit=50&offset=100&notAra=iade&tip=KrediKarti&kartId=3&minTutar=1000.5&maxTutar=2000", q);
        Assert.EndsWith("/api/islemler", h.SonIstek.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task Gelismis_suzgec_yoksa_eski_istekle_ayni_yol_gider_csv_de_suzulur()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]", ("X-Toplam-Kayit", "0"));
        await c.IslemAraAsync(new IslemAramasi(Kanal: "MEZAT"), 10, 0);
        Assert.Equal("?kanal=MEZAT&limit=10&offset=0", h.SonIstek!.RequestUri!.Query);
        Assert.False(new IslemAramasi(Kanal: "MEZAT", Cari: "x").GelismisVar);
        Assert.True(new IslemAramasi(MaxTutar: 1m).GelismisVar);

        h.Kuyrukla(HttpStatusCode.OK, "a;b");
        await c.IslemAramaCsvAsync(new IslemAramasi(MinTutar: 5m));
        Assert.Equal("/api/disaaktar/islemler.csv", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?minTutar=5", h.SonIstek.RequestUri.Query);
    }

    [Fact]
    public async Task Uyarilar_taslagi_gonderir_eski_sunucuda_bos_doner()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"kod":"AyniTutar","mesaj":"Aynı cariye…"}]""");
        var u = await c.IslemUyarilariAsync(Taslak with { KrediKartiId = 4 }, haricId: 9);
        Assert.Equal(IslemUyariKodlari.AyniTutar, Assert.Single(u).Kod);
        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/islemler/uyarilar", h.SonIstek.RequestUri!.AbsolutePath);
        using var govde = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(9, govde.RootElement.GetProperty("haricId").GetInt32());
        Assert.Equal(4, govde.RootElement.GetProperty("krediKartiId").GetInt32());
        Assert.Equal("Cari", govde.RootElement.GetProperty("tip").GetString());
        Assert.Equal(1_250.5m, govde.RootElement.GetProperty("tutarTl").GetDecimal());

        h.Kuyrukla(HttpStatusCode.NotFound);
        Assert.Empty(await c.IslemUyarilariAsync(Taslak));
        h.Kuyrukla(HttpStatusCode.MethodNotAllowed);
        Assert.Empty(await c.IslemUyarilariAsync(Taslak));
        h.Kuyrukla(HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<KasaApiException>(() => c.IslemUyarilariAsync(Taslak))).DurumKodu);
    }

    [Fact]
    public async Task Toplu_basarida_sonucu_satir_hatasinda_hatalari_doner()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"eklenen":2,"toplam":30.5,"yeniCariler":["Yeni"],"islemler":[
              {"id":1,"tarih":"2026-09-01","cari":"Yeni","tutarTl":10,"kanal":"MEZAT","tip":"Cari","not":null,"krediKartiId":null},
              {"id":2,"tarih":"2026-09-02","cari":"Yeni","tutarTl":20.5,"kanal":"MEZAT","tip":"Cari","not":null,"krediKartiId":null}]}
            """);
        var ok = await c.TopluIslemKaydetAsync([Taslak, Taslak], yeniCarileriEkle: true);
        Assert.True(ok.Kaydedildi);
        Assert.Equal((2, 30.5m), (ok.Eklenen, ok.Toplam));
        Assert.Equal(["Yeni"], ok.YeniCariler);
        Assert.Equal([1, 2], ok.Islemler.Select(i => i.Id));
        using (var govde = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.True(govde.RootElement.GetProperty("yeniCarileriEkle").GetBoolean());
            Assert.Equal(2, govde.RootElement.GetProperty("satirlar").GetArrayLength());
            Assert.Equal("Market", govde.RootElement.GetProperty("satirlar")[0].GetProperty("cari").GetString());
        }

        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"1 satırda hata var; hiçbir satır kaydedilmedi. 2. satır: X","satirlar":[{"sira":2,"hata":"X"}]}""");
        var red = await c.TopluIslemKaydetAsync([Taslak, Taslak], false);
        Assert.False(red.Kaydedildi);
        Assert.Equal(new TopluSatirHatasi(2, "X"), Assert.Single(red.SatirHatalari));
        Assert.StartsWith("1 satırda hata var", red.Hata);

        // Satır listesi olmayan 400 (ör. "Yüklenecek satır yok.") istisnadır.
        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"Yüklenecek satır yok."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.TopluIslemKaydetAsync([], false));
        Assert.Equal("Yüklenecek satır yok.", ex.SunucuMesaji);
    }

    [Fact]
    public async Task Oneri_204te_ve_eski_sunucuda_null_doner()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"cari":"Öz Ticaret","tarih":"2026-09-10","tutarTl":20,"kanal":"TOPTAN","tip":"SabitGider","krediKartiId":null}""");
        var o = await c.IslemOnerisiAsync(" öz ticaret ");
        Assert.Equal(("TOPTAN", GiderTipi.SabitGider), (o!.Kanal, o.Tip));
        Assert.Equal("?cari=%C3%B6z%20ticaret", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.NoContent);
        Assert.Null(await c.IslemOnerisiAsync("x"));
        h.Kuyrukla(HttpStatusCode.NotFound);
        Assert.Null(await c.IslemOnerisiAsync("x"));
        Assert.Null(await c.IslemOnerisiAsync("  ")); // istek atılmaz
    }

    [Fact]
    public async Task Korumali_gelen_409da_mevcut_tutari_doner()
    {
        var (c, h, _) = Kur();
        var g = new GelenYaz(new DateOnly(2026, 9, 21), "MEZAT", 150m);
        h.Kuyrukla(HttpStatusCode.OK, """{"id":5,"donemStart":"2026-09-21","kanal":"MEZAT","tutarTl":150}""");
        var ok = await c.GelenKorumaliKaydetAsync(g, 100m);
        Assert.True(ok.Kaydedildi);
        Assert.Equal(5, ok.Gelen!.Id);
        using (var govde = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal(100m, govde.RootElement.GetProperty("beklenenTutar").GetDecimal());
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);

        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"değişmiş","mevcutTutar":120.25}""");
        var red = await c.GelenKorumaliKaydetAsync(g, 100m);
        Assert.False(red.Kaydedildi);
        Assert.Equal(120.25m, red.MevcutTutar);
        Assert.Equal("değişmiş", red.Mesaj);

        // Kısıt çakışması 409'u (mevcutTutar yok): güncel değer yeniden okunur.
        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Gelen aynı anda başka bir yerden kaydedildi; tekrar deneyin."}""")
         .Kuyrukla(HttpStatusCode.OK, """[{"id":6,"donemStart":"2026-09-21","kanal":"MEZAT","tutarTl":80}]""");
        var yeniden = await c.GelenKorumaliKaydetAsync(g, 0m);
        Assert.False(yeniden.Kaydedildi);
        Assert.Equal(80m, yeniden.MevcutTutar);
        Assert.Equal("?donemStart=2026-09-21", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Gelen_tablosu_ve_eksikler_okunur()
    {
        var (c, h, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"donemStart":"2026-09-21","donemEnd":"2026-09-27","onceki":"2026-09-14","sonraki":null,
             "satirlar":[{"kanal":"MEZAT","aktif":true,"tutarTl":null,"gelenId":null},{"kanal":"ESKİ","aktif":false,"tutarTl":5,"gelenId":3}]}
            """);
        var t = await c.GelenTablosuAsync(new DateOnly(2026, 9, 21));
        Assert.Equal("?donemStart=2026-09-21", h.SonIstek!.RequestUri!.Query);
        Assert.Null(t.Sonraki);
        Assert.Null(t.Satirlar[0].TutarTl);
        Assert.Equal(5m, t.Satirlar[1].TutarTl);

        h.Kuyrukla(HttpStatusCode.OK, "{\"donemStart\":\"2026-09-21\",\"donemEnd\":\"2026-09-27\",\"satirlar\":[]}");
        await c.GelenTablosuAsync();
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.OK, """[{"donemStart":"2026-09-14","donemEnd":"2026-09-20","kanal":"MEZAT"}]""", ("X-Toplam-Kayit", "250"));
        var e = await c.EksikGelenListesiAsync();
        Assert.Equal(250, e.Toplam);
        Assert.Equal("MEZAT", Assert.Single(e.Kayitlar).Kanal);
    }

    [Fact]
    public async Task Son_silme_bulunamazsa_null_401de_oturum_duser()
    {
        var (c, h, t) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"id":9,"zamanUtc":"2026-09-24T09:00:00Z","rol":"editor","tur":"İşlem","kayitId":42,"eylem":"Silindi",
             "ozet":"x","eskiJson":"{}","yeniJson":null,"geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":true}
            """);
        var d = await c.SonSilmeAsync(GecmisTurAdlari.Islem, 42);
        Assert.Equal(9, d!.Id);
        Assert.Equal("?tur=%C4%B0%C5%9Flem&kayitId=42", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.NotFound);
        Assert.Null(await c.SonSilmeAsync(GecmisTurAdlari.Cari, 1));

        await t.YazAsync("tok");
        var olay = 0;
        c.OturumSonaErdi += (_, _) => olay++;
        h.Kuyrukla(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<KasaApiException>(() => c.SonSilmeAsync(GecmisTurAdlari.Cari, 1));
        Assert.Equal("Bearer", h.SonIstek!.Headers.Authorization!.Scheme);
        Assert.Null(await t.OkuAsync());
        Assert.Equal(1, olay);
    }
}
