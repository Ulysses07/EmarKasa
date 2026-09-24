using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket A uçları: nakit tahmini, eksik gelen, geçmiş özeti ve geçmişe dönük işareti.</summary>
public class PanelPaketATests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    private const string TahminJson = """
        {"bugun":"2026-09-24","gun":30,"baslangicKasa":58900,
         "gunler":[
           {"tarih":"2026-09-24","giris":0,"cikis":0,"kasa":58900,"kalemler":[]},
           {"tarih":"2026-09-25","giris":1000,"cikis":15000,"kasa":44900,"kalemler":[
             {"tarih":"2026-09-20","tur":"AlinanCek","aciklama":"Geciken · alınan çek","tutar":1000,"cekId":7,"gecikmis":true},
             {"tarih":"2026-09-05","tur":"TekrarlayanGider","aciklama":"Kira · tekrarlayan gider (onay bekliyor)","tutar":-15000,"cekId":null,"gecikmis":true}]}],
         "enDusukTarih":"2026-09-25","enDusukKasa":44900,"sonKasa":44900,"toplamGiris":1000,"toplamCikis":15000,
         "haricKalemler":[{"tarih":"2026-10-05","tur":"AlinanCek","aciklama":"Riskli · alınan çek","tutar":7500,"cekId":3,"gecikmis":false}]}
        """;

    [Fact]
    public async Task Nakit_tahmini_eslenir_ve_parametreler_gonderilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, TahminJson);
        var t = await c.NakitTahminAsync(30, [9, 3, 3]);

        Assert.Equal(new DateOnly(2026, 9, 24), t.Bugun);
        Assert.Equal(58_900m, t.BaslangicKasa);
        Assert.Equal(2, t.Gunler.Count);
        var k = t.Gunler[1].Kalemler[0];
        Assert.Equal(new TahminKalemiDto(new DateOnly(2026, 9, 20), TahminKalemTuru.AlinanCek, "Geciken · alınan çek", 1_000m, 7, true), k);
        Assert.Equal(TahminKalemTuru.TekrarlayanGider, t.Gunler[1].Kalemler[1].Tur);
        Assert.Null(t.Gunler[1].Kalemler[1].CekId);
        Assert.Equal((new DateOnly(2026, 9, 25), 44_900m), (t.EnDusukTarih, t.EnDusukKasa));
        Assert.Equal(3, Assert.Single(t.HaricKalemler).CekId);

        var uri = h.SonIstek!.RequestUri!;
        Assert.EndsWith("/api/rapor/tahmin", uri.AbsolutePath);
        Assert.Equal("?gun=30&haric=3,9", Uri.UnescapeDataString(uri.Query));
    }

    [Fact]
    public async Task Nakit_tahmini_haric_yoksa_parametre_gonderilmez()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, TahminJson);
        await c.NakitTahminAsync(90);
        Assert.Equal("?gun=90", h.SonIstek!.RequestUri!.Query);
        h.Kuyrukla(HttpStatusCode.OK, TahminJson);
        await c.NakitTahminAsync(60, []);
        Assert.Equal("?gun=60", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Nakit_tahmini_sunucu_hatasi_mesajla_firlatir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"gun 1 ile 366 arasında olmalı."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.NakitTahminAsync(0));
        Assert.Equal(HttpStatusCode.BadRequest, ex.DurumKodu);
        Assert.Contains("366", ex.SunucuMesaji);
    }

    [Fact]
    public async Task Eksik_gelenler_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"donemStart":"2026-09-14","donemEnd":"2026-09-20","kanallar":["MEZAT","TOPTAN"]}]""");
        var l = await c.EksikGelenlerAsync();
        var e = Assert.Single(l);
        Assert.Equal((new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20)), (e.DonemStart, e.DonemEnd));
        Assert.Equal(["MEZAT", "TOPTAN"], e.Kanallar);
        Assert.EndsWith("/api/gelenler/eksik", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Gecmis_ozeti_eslenir_sonId_istege_bagli()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"sonId":120,"sonZamanUtc":"2026-09-24T07:00:00Z","toplam":12,"gecmiseDonuk":2,
             "gecmiseDonukSatirlar":[{"id":118,"zamanUtc":"2026-09-24T06:59:00Z","tur":"İşlem","ozet":"İşlem eklendi: 15.08.2026"}]}
            """);
        var o = await c.GecmisOzetAsync(100);
        Assert.Equal((120, 12, 2), (o.SonId, o.Toplam, o.GecmiseDonuk));
        Assert.Equal(new DateTime(2026, 9, 24, 7, 0, 0, DateTimeKind.Utc), o.SonZamanUtc!.Value.ToUniversalTime());
        Assert.Equal(118, Assert.Single(o.GecmiseDonukSatirlar).Id);
        Assert.Equal("?sonId=100", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.OK, """{"sonId":0,"sonZamanUtc":null,"toplam":0,"gecmiseDonuk":0,"gecmiseDonukSatirlar":[]}""");
        var bos = await c.GecmisOzetAsync();
        Assert.Null(bos.SonZamanUtc);
        Assert.EndsWith("/api/gecmis/ozet", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Gecmis_satirinda_gecmise_donuk_isareti_okunur_eski_sunucuda_false()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":2,"zamanUtc":"2026-09-24T09:00:00Z","rol":"editor","tur":"İşlem","kayitId":5,"eylem":"Eklendi","ozet":"x",
              "eskiJson":null,"yeniJson":"{}","geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":false,"gecmiseDonuk":true},
             {"id":1,"zamanUtc":"2026-09-24T09:00:00Z","rol":"editor","tur":"İşlem","kayitId":4,"eylem":"Eklendi","ozet":"y",
              "eskiJson":null,"yeniJson":"{}","geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":false}]
            """, ("X-Toplam-Kayit", "2"));
        var s = await c.GecmisAsync(null, 50, 0);
        Assert.True(s.Kayitlar[0].GecmiseDonuk);
        Assert.False(s.Kayitlar[1].GecmiseDonuk);
    }
}
