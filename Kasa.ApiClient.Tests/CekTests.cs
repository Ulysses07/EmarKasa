using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class CekTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    private const string CekJson = """
        {"id":5,"yon":"Alinan","cekNo":"0012","banka":"Ziraat","kisi":"Ahmet","tutar":1500.5,
         "duzenlemeTarihi":"2026-09-01","vadeTarihi":"2026-10-15","kanal":"MEZAT","durum":"TahsilEdildi",
         "islemTarihi":"2026-09-20","not":null}
        """;

    [Fact]
    public async Task Cekler_filtreleri_query_olarak_gonderir_ve_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, $"[{CekJson}]");

        var liste = await c.CeklerAsync(CekYonu.Alinan, CekDurumu.TahsilEdildi, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var uri = h.SonIstek!.RequestUri!;
        Assert.EndsWith("/api/cekler", uri.AbsolutePath);
        Assert.Equal("?yon=Alinan&durum=TahsilEdildi&baslangic=2026-09-01&bitis=2026-09-30", uri.Query);
        var cek = Assert.Single(liste);
        Assert.Equal(CekYonu.Alinan, cek.Yon);
        Assert.Equal(CekDurumu.TahsilEdildi, cek.Durum);
        Assert.Equal(new DateOnly(2026, 9, 20), cek.IslemTarihi);
        Assert.Equal(1500.5m, cek.Tutar);
    }

    [Fact]
    public async Task Cekler_filtresiz_duz_yol_ister()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.CeklerAsync();
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Cek_olustur_enumlari_string_bos_islem_tarihini_null_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, CekJson);

        await c.CekOlusturAsync(new CekYaz(CekYonu.Verilen, null, "İş Bankası", "Tedarikçi A", 250m,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), "Ortak", CekDurumu.Portfoyde, null, null));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/cekler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("Verilen", doc.RootElement.GetProperty("yon").GetString());
        Assert.Equal("Portfoyde", doc.RootElement.GetProperty("durum").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("islemTarihi").ValueKind);
        Assert.Equal("2026-10-01", doc.RootElement.GetProperty("vadeTarihi").GetString());
    }

    [Fact]
    public async Task Cek_guncelle_ve_sil_dogru_yolu_kullanir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, CekJson);
        await c.CekGuncelleAsync(5, new CekYaz(CekYonu.Alinan, "0012", "Ziraat", "Ahmet", 1500.5m,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15), "MEZAT", CekDurumu.TahsilEdildi, new DateOnly(2026, 9, 20), null));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/cekler/5", h.SonIstek.RequestUri!.AbsolutePath);
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal("2026-09-20", doc.RootElement.GetProperty("islemTarihi").GetString());

        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.CekSilAsync(5);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/cekler/5", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Cek_ozeti_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, $$"""
            {"portfoydekiAlinanToplam":600.0,"portfoydekiAlinanAdet":3,"odenecekVerilenToplam":100.0,
             "odenecekVerilenAdet":2,"yaklasanGun":30,"yaklasanlar":[{{CekJson}}],"vadesiGecenler":[]}
            """);
        var o = await c.CekOzetAsync();
        Assert.EndsWith("/api/cekler/ozet", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(600m, o.PortfoydekiAlinanToplam);
        Assert.Equal(2, o.OdenecekVerilenAdet);
        Assert.Equal(30, o.YaklasanGun);
        Assert.Single(o.Yaklasanlar);
        Assert.Empty(o.VadesiGecenler);
    }

    [Fact]
    public async Task Rapor_cek_alanlari_eslenir_eski_sunucuda_sifir_gelir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"donem":{"start":"2026-09-07","end":"2026-09-13","yil":2026,"ay":9},
              "kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":500,"devir":500,"cekGelen":500,"cekGiden":0}],
              "toplamGelen":0,"toplamGiden":0,"kasaSonucu":500,"kasaDevir":1500,"toplamCekGelen":500,"toplamCekGiden":0},
             {"donem":{"start":"2026-09-14","end":"2026-09-20","yil":2026,"ay":9},
              "kanallar":[{"kanal":"MEZAT","gelen":1,"giden":0,"sonuc":1,"devir":501}],
              "toplamGelen":1,"toplamGiden":0,"kasaSonucu":1,"kasaDevir":1501}]
            """);
        var liste = await c.HaftalikAsync();
        Assert.Equal(500m, liste[0].ToplamCekGelen);
        Assert.Equal(500m, liste[0].Kanallar[0].CekGelen);
        Assert.True(liste[0].CekVar);
        Assert.True(liste[0].Kanallar[0].CekVar);
        Assert.Equal(0m, liste[1].ToplamCekGelen);      // alan yoksa (eski sunucu) sıfır
        Assert.False(liste[1].CekVar);

        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":300,"cekGelen":500,"cekGiden":200}]}""");
        var r = await c.AylikAsync(2026, 9);
        Assert.Equal((500m, 200m), (r.Kanallar[0].CekGelen, r.Kanallar[0].CekGiden));
        Assert.True(r.Kanallar[0].CekVar);
    }
}
