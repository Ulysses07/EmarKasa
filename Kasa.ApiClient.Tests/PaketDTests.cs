using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket D istemci uçları: yol, metot, gövde ve yanıt eşlemesi.</summary>
public class PaketDTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://kasa.emarglobal.com/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    private const string CekJson = """
        {"id":5,"yon":"Alinan","cekNo":null,"banka":null,"kisi":"Ahmet","tutar":10,"duzenlemeTarihi":"2026-09-01",
         "vadeTarihi":"2026-10-15","kanal":"MEZAT","durum":"CiroEdildi","islemTarihi":"2026-09-24","not":null,
         "tur":"Senet","konum":"Icrada","ciroEdilenCari":"Market"}
        """;

    [Fact]
    public async Task Cek_durum_tek_dokunus_gonderir_yeni_alanlari_esler()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, CekJson);
        var cek = await c.CekDurumAsync(5, new CekDurumYaz(CekDurumu.CiroEdildi, null, "Market"));
        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.Equal("/api/cekler/5/durum", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("CiroEdildi", doc.RootElement.GetProperty("durum").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("tarih").ValueKind);
        Assert.Equal("Market", doc.RootElement.GetProperty("ciroEdilenCari").GetString());
        Assert.Equal((CekTuru.Senet, CekKonumu.Icrada, "Market"), (cek.Tur, cek.Konum, cek.CiroEdilenCari));
    }

    [Fact]
    public async Task Eski_sunucunun_cek_yaniti_varsayilanlarla_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"yon":"Verilen","cekNo":null,"banka":null,"kisi":"X","tutar":1,"duzenlemeTarihi":"2026-09-01","vadeTarihi":"2026-09-02","kanal":"Ortak","durum":"Portfoyde","islemTarihi":null,"not":null}]""");
        var cek = Assert.Single(await c.CeklerAsync());
        Assert.Equal((CekTuru.Cek, CekKonumu.Elde, (string?)null), (cek.Tur, cek.Konum, cek.CiroEdilenCari));
    }

    [Fact]
    public async Task Cek_yaz_tur_ve_konumu_metin_olarak_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, CekJson);
        await c.CekOlusturAsync(new CekYaz(CekYonu.Alinan, null, null, "K", 1m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2),
            "MEZAT", CekDurumu.Portfoyde, null, null, CekTuru.Senet, CekKonumu.Teminatta));
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("Senet", doc.RootElement.GetProperty("tur").GetString());
        Assert.Equal("Teminatta", doc.RootElement.GetProperty("konum").GetString());
    }

    [Fact]
    public async Task Cek_risk_turu_query_olarak_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"toplam":100,"adet":1,"kesideciler":[{"ad":"A","tutar":100,"adet":1,"oran":1}],"bankalar":[]}""");
        var r = await c.CekRiskAsync(CekTuru.Senet);
        Assert.Equal("/api/cekler/risk", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?tur=Senet", h.SonIstek.RequestUri.Query);
        Assert.Equal(1m, Assert.Single(r.Kesideciler).Oran);
        h.Kuyrukla(HttpStatusCode.OK, """{"toplam":0,"adet":0,"kesideciler":[],"bankalar":[]}""");
        await c.CekRiskAsync();
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Tekrarlayan_ikinci_adim_uclari()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"tekrarlayanGiderId":3,"kalem":"Kira","kanal":"Ortak","ay":"2026-08-01","vade":"2026-08-05"}]""");
        var atlanan = Assert.Single(await c.AtlananGiderlerAsync());
        Assert.Equal("/api/tekrarlayangiderler/atlananlar", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(new DateOnly(2026, 8, 5), atlanan.Vade);

        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.TekrarlayanAtlamayiGeriAlAsync(3, new DateOnly(2026, 8, 1));
        Assert.Equal("/api/tekrarlayangiderler/3/atlamayi-geri-al", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("""{"ay":"2026-08-01"}""", h.SonGovde);

        h.Kuyrukla(HttpStatusCode.OK, """[{"kod":"kdv","ad":"KDV","aciklama":"Her ay 28'i","eklendi":false}]""");
        Assert.Equal("KDV", Assert.Single(await c.TekrarlayanHazirlarAsync()).Ad);
        h.Kuyrukla(HttpStatusCode.Created, """[{"id":9,"kalem":"KDV","kanal":"Ortak","tutar":0,"ayinGunu":28,"aktif":true,"baslangicAyi":"2026-09-01","siklik":"Aylik","krediKartiId":null,"tutarDegisken":true}]""");
        var eklenen = Assert.Single(await c.TekrarlayanHazirEkleAsync("kdv"));
        Assert.Equal("""{"kod":"kdv"}""", h.SonGovde);
        Assert.True(eklenen.TutarDegisken);

        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"'KDV' için zaten bir tekrarlayan gider var."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.TekrarlayanHazirEkleAsync("kdv"));
        Assert.Equal("'KDV' için zaten bir tekrarlayan gider var.", ex.SunucuMesaji);
    }

    [Fact]
    public async Task Tekrarlayan_yaz_ve_onay_yeni_alanlari_gonderir_bos_alanlari_atlar()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"kalem":"Market","kanal":"MEZAT","tutar":0,"ayinGunu":3,"aktif":true,"baslangicAyi":"2026-09-01","siklik":"UcAylik","krediKartiId":4,"tutarDegisken":true}""");
        var t = await c.TekrarlayanGiderOlusturAsync(new TekrarlayanGiderYaz("Market", "MEZAT", 0m, 3, true, null, TekrarSikligi.UcAylik, 4, true));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.Equal("UcAylik", doc.RootElement.GetProperty("siklik").GetString());
            Assert.Equal(4, doc.RootElement.GetProperty("krediKartiId").GetInt32());
            Assert.True(doc.RootElement.GetProperty("tutarDegisken").GetBoolean());
            Assert.False(doc.RootElement.TryGetProperty("baslangicAyi", out _));
        }
        Assert.Equal((TekrarSikligi.UcAylik, 4, true), (t.Siklik, t.KrediKartiId, t.TutarDegisken));

        h.Kuyrukla(HttpStatusCode.Created, """{"id":2,"tarih":"2026-09-05","cari":"Kira","tutarTl":5,"kanal":"MEZAT","tip":"SabitGider","not":null}""");
        await c.TekrarlayanOnaylaAsync(1, new TekrarlayanOnayYaz(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 5m));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.False(doc.RootElement.TryGetProperty("kanal", out _));
            Assert.False(doc.RootElement.TryGetProperty("not", out _));
        }
        h.Kuyrukla(HttpStatusCode.Created, """{"id":2,"tarih":"2026-09-05","cari":"Kira","tutarTl":5,"kanal":"MEZAT","tip":"SabitGider","not":null}""");
        await c.TekrarlayanOnaylaAsync(1, new TekrarlayanOnayYaz(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 5m, "TOPTAN", ""));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.Equal("TOPTAN", doc.RootElement.GetProperty("kanal").GetString());
            Assert.Equal("", doc.RootElement.GetProperty("not").GetString());
        }
    }

    [Fact]
    public async Task Kart_mutabakat_uclari()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"baslangic":"2026-08-16","kesim":"2026-09-15","sonOdeme":"2026-09-25","hesaplananBorc":550,"mutabakatId":null,"ekstreTutari":null,"fark":null,"durum":null}]""");
        var d = Assert.Single(await c.KartDonemleriAsync(7, 6));
        Assert.Equal("?krediKartiId=7&adet=6", h.SonIstek!.RequestUri!.Query);
        Assert.Null(d.Durum);

        const string detay = """
            {"krediKartiId":7,"kartAdi":"Bonus","baslangic":"2026-08-16","kesim":"2026-09-15","sonOdeme":"2026-09-25",
             "devredenBorc":300,"donemHarcama":350,"donemOdeme":100,"hesaplananBorc":550,
             "islemler":[{"id":1,"tarih":"2026-08-20","cari":"Market","tutar":300,"not":null,"tikli":true}],
             "odemeler":[{"id":2,"tarih":"2026-08-25","tutar":100,"not":null}],
             "mutabakatId":4,"ekstreTutari":560,"fark":10,"tiksizToplam":50,"not":"x","durum":"Acik","kayittakiHesaplanan":550}
            """;
        h.Kuyrukla(HttpStatusCode.OK, detay);
        var m = await c.KartMutabakatAsync(7, new DateOnly(2026, 9, 15));
        Assert.Equal("?krediKartiId=7&kesim=2026-09-15", h.SonIstek!.RequestUri!.Query);
        Assert.Equal(KartMutabakatDurumu.Acik, m.Durum);
        Assert.True(Assert.Single(m.Islemler).Tikli);

        h.Kuyrukla(HttpStatusCode.OK, detay);
        await c.KartMutabakatKaydetAsync(new KartMutabakatYaz(7, new DateOnly(2026, 9, 15), 560m, [1], null, false));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        using (var doc = JsonDocument.Parse(h.SonGovde!))
        {
            Assert.Equal("2026-09-15", doc.RootElement.GetProperty("kesim").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("tikliIslemIdleri")[0].GetInt32());
        }
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.KartMutabakatSilAsync(4);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.Equal("/api/kartmutabakat/4", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Kasa_sayimi_satirlar_fark_neden_degisti_son()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-09-20","sayilanTutar":400,"hesaplananTutar":390,"fark":10,"guncelHesaplanan":390,"not":null,"kayitZamaniUtc":"2026-09-20T10:00:00Z","satirlar":[{"tur":"Nakit","ad":"Kasa","tutar":400,"kupurler":[{"kurus":20000,"adet":2}]}],"farkDurumu":"Acik","farkAciklamasi":null}""");
        var s = await c.KasaSayimKaydetAsync(new KasaSayimYaz(new DateOnly(2026, 9, 20), 400m, null,
            [new SayimSatiriDto(SayimSatirTuru.Nakit, "Kasa", 400m, [new KupurAdetDto(20000, 2)])]));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal(20000, doc.RootElement.GetProperty("satirlar")[0].GetProperty("kupurler")[0].GetProperty("kurus").GetInt32());
        Assert.Equal(SayimSatirTuru.Nakit, Assert.Single(s.Satirlar!).Tur);

        // Satırsız sayım eski gövdeyi gönderir (satirlar alanı yok).
        h.Kuyrukla(HttpStatusCode.Created, """{"id":2,"tarih":"2026-09-20","sayilanTutar":1,"hesaplananTutar":1,"fark":0,"guncelHesaplanan":null,"not":null,"kayitZamaniUtc":"2026-09-20T10:00:00Z"}""");
        var eski = await c.KasaSayimKaydetAsync(new KasaSayimYaz(new DateOnly(2026, 9, 20), 1m, null));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.False(doc.RootElement.TryGetProperty("satirlar", out _));
        Assert.Null(eski.Satirlar);
        Assert.Equal(SayimFarkDurumu.Acik, eski.FarkDurumu);

        h.Kuyrukla(HttpStatusCode.OK, """{"id":1,"tarih":"2026-09-20","sayilanTutar":400,"hesaplananTutar":390,"fark":10,"guncelHesaplanan":390,"not":null,"kayitZamaniUtc":"2026-09-20T10:00:00Z","farkDurumu":"Aciklandi","farkAciklamasi":"bozuk para"}""");
        var f = await c.SayimFarkiAsync(1, new SayimFarkYaz(SayimFarkDurumu.Aciklandi, "bozuk para"));
        Assert.Equal("/api/kasasayimlari/1/fark", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Put, h.SonIstek.Method);
        Assert.Equal(SayimFarkDurumu.Aciklandi, f.FarkDurumu);

        h.Kuyrukla(HttpStatusCode.OK, """{"sayimId":1,"tarih":"2026-09-20","hesaplananTutar":390,"guncelHesaplanan":430,"degisim":40,"degisiklikler":[{"id":8,"zamanUtc":"2026-09-21T10:00:00Z","rol":"editor","tur":"İşlem","eylem":"Eklendi","ozet":"İşlem eklendi"}]}""");
        var n = await c.SayimNedenDegistiAsync(1);
        Assert.Equal("/api/kasasayimlari/1/nedendegisti", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(40m, n.Degisim);

        h.Kuyrukla(HttpStatusCode.OK, """{"tarih":null,"gecenGun":null,"sayimId":null}""");
        var son = await c.SonSayimAsync();
        Assert.Null(son.Tarih);
    }
}
