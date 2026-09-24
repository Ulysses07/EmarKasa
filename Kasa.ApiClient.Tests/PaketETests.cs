using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Paket E istemci uçları: iki adımlı giriş, hesap, kullanıcılar, oturumlar, sorular, risk kartı.</summary>
public class PaketETests
{
    private static (KasaApiClient c, SahteHandler h, BellekTokenStore s, HttpClient http) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        var s = new BellekTokenStore();
        return (new KasaApiClient(http, s), h, s, http);
    }

    private static JsonElement Govde(SahteHandler h) => JsonDocument.Parse(h.SonGovde!).RootElement;

    [Fact]
    public async Task Giris_basarida_token_saklar_ad_ve_kullanici_doner()
    {
        var (c, h, s, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer","token":"jwt-1","ad":"Ali Ortak","kullaniciId":7}""");
        var g = await c.GirisAsync("ali", "sifre", null);
        Assert.False(g.KodGerekli);
        Assert.Equal("viewer", g.Rol);
        Assert.Equal("Ali Ortak", g.Ad);
        Assert.Equal(7, g.KullaniciId);
        Assert.Equal("jwt-1", await s.OkuAsync());
        var b = Govde(h);
        Assert.Equal("ali", b.GetProperty("kullanici").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("kod").ValueKind);
    }

    [Fact]
    public async Task Iki_adim_kodu_istenince_token_saklanmaz_kod_gonderilir()
    {
        var (c, h, s, _) = Kur();
        h.Kuyrukla(HttpStatusCode.Unauthorized, """{"hata":"İki adımlı giriş kodunu girin.","kodGerekli":true}""");
        var g = await c.GirisAsync("editor", "sifre", null);
        Assert.True(g.KodGerekli);
        Assert.Equal("İki adımlı giriş kodunu girin.", g.Mesaj);
        Assert.Null(await s.OkuAsync());

        h.Kuyrukla(HttpStatusCode.Unauthorized, """{"hata":"Kod hatalı.","kodGerekli":true}""");
        var hatali = await c.GirisAsync("editor", "sifre", " 123 456 ");
        Assert.True(hatali.KodGerekli);
        Assert.Equal("Kod hatalı.", hatali.Mesaj);
        Assert.Equal("123 456", Govde(h).GetProperty("kod").GetString());

        h.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-2","ad":"EMAR","kullaniciId":1}""");
        Assert.Equal("editor", (await c.GirisAsync("editor", "sifre", "123456")).Rol);
        Assert.Equal("jwt-2", await s.OkuAsync());
    }

    [Fact]
    public async Task Giris_duz_401_ve_pasif_hesap_mesajla_hata_firlatir()
    {
        var (c, h, _, _) = Kur();
        h.Kuyrukla(HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.GirisAsync("x", "y", null));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.DurumKodu);
        Assert.Null(ex.SunucuMesaji);

        h.Kuyrukla(HttpStatusCode.Unauthorized, """{"hata":"Bu hesap pasif. Editörle görüşün."}""");
        ex = await Assert.ThrowsAsync<KasaApiException>(() => c.GirisAsync("x", "y", null));
        Assert.Equal("Bu hesap pasif. Editörle görüşün.", ex.SunucuMesaji);

        h.Kuyrukla(HttpStatusCode.TooManyRequests);
        ex = await Assert.ThrowsAsync<KasaApiException>(() => c.GirisAsync("x", "y", null));
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.DurumKodu);
    }

    [Fact]
    public async Task Cihaz_adi_basligi_url_kodlu_eklenir()
    {
        var (c, h, _, http) = Kur();
        KasaApiClient.CihazAdiEkle(http, "Şükrü-PC");
        h.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"t"}""");
        await c.GirisAsync("editor", "s", null);
        Assert.Equal(Uri.EscapeDataString("Şükrü-PC"), h.SonIstek!.Headers.GetValues(KasaApiClient.CihazBasligi).Single());
        KasaApiClient.CihazAdiEkle(http, "  ");
        Assert.False(http.DefaultRequestHeaders.Contains(KasaApiClient.CihazBasligi));
    }

    [Fact]
    public async Task Sifre_degisince_yeni_token_saklanir_yanlis_mevcut_sifre_oturumu_dusurmez()
    {
        var (c, h, s, _) = Kur();
        await s.YazAsync("eski");
        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"Mevcut şifre hatalı."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.SifremiDegistirAsync("yanlis", "yeni-sifre"));
        Assert.Equal("Mevcut şifre hatalı.", ex.SunucuMesaji);
        Assert.Equal("eski", await s.OkuAsync());

        h.Kuyrukla(HttpStatusCode.OK, """{"token":"yeni"}""");
        await c.SifremiDegistirAsync("dogru", "yeni-sifre");
        Assert.Equal("yeni", await s.OkuAsync());
        Assert.EndsWith("/api/hesap/sifre", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("dogru", Govde(h).GetProperty("mevcutSifre").GetString());
    }

    [Fact]
    public async Task Iki_adim_onayi_kurtarma_kodlarini_doner_token_yeniler()
    {
        var (c, h, s, _) = Kur();
        await s.YazAsync("eski");
        h.Kuyrukla(HttpStatusCode.OK, """{"sir":"JBSWY3DPEHPK3PXP","adres":"otpauth://totp/x"}""");
        var k = await c.IkiAdimBaslatAsync();
        Assert.Equal("JBSWY3DPEHPK3PXP", k.Sir);

        h.Kuyrukla(HttpStatusCode.OK, """{"kodlar":["AAAAA-BBBBB","CCCCC-DDDDD"],"token":"yeni"}""");
        var kodlar = await c.IkiAdimOnaylaAsync("hesap-sifresi", "123456");
        Assert.Equal(["AAAAA-BBBBB", "CCCCC-DDDDD"], kodlar);
        Assert.Equal("yeni", await s.OkuAsync());
        Assert.Equal("hesap-sifresi", Govde(h).GetProperty("sifre").GetString());
        Assert.Equal("123456", Govde(h).GetProperty("kod").GetString());

        h.Kuyrukla(HttpStatusCode.OK, """{"kodlar":["EEEEE-FFFFF"],"token":null}""");
        Assert.Equal(["EEEEE-FFFFF"], await c.KurtarmaKodlariYenileAsync("hesap-sifresi", "654321"));
        Assert.Equal("yeni", await s.OkuAsync());
        Assert.Equal("hesap-sifresi", Govde(h).GetProperty("sifre").GetString());
        Assert.EndsWith("/api/hesap/kurtarma-kodlari", h.SonIstek!.RequestUri!.AbsolutePath);

        h.Kuyrukla(HttpStatusCode.OK);
        await c.IkiAdimKapatAsync("sifre", "111111");
        Assert.EndsWith("/api/hesap/iki-adim/kapat", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("111111", Govde(h).GetProperty("kod").GetString());
    }

    [Fact]
    public async Task Kullanici_uclari_eslenir()
    {
        var (c, h, _, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"adSoyad":"EMAR","kullaniciAdi":"emar","rol":"editor","aktif":true,"yerlesik":true,"ikiAdimAcik":true,
              "olusturmaUtc":"2026-09-01T00:00:00Z","sonGirisUtc":"2026-09-24T08:00:00Z","sonCihaz":"EMAR-LAPTOP","acikOturum":2}]
            """);
        var k = Assert.Single(await c.KullanicilarAsync());
        Assert.Equal("EMAR-LAPTOP", k.SonCihaz);
        Assert.Equal(2, k.AcikOturum);
        Assert.False(k.Ben);   // eski sunucu alanı göndermez

        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"adSoyad":"EMAR","kullaniciAdi":"emar","rol":"editor","aktif":true,"yerlesik":true,"ikiAdimAcik":false,
              "olusturmaUtc":"2026-09-01T00:00:00Z","sonGirisUtc":null,"sonCihaz":null,"acikOturum":1,"ben":true}]
            """);
        Assert.True(Assert.Single(await c.KullanicilarAsync()).Ben);

        h.Kuyrukla(HttpStatusCode.Created, """{"id":5,"adSoyad":"Ali","kullaniciAdi":"ali","rol":"viewer","aktif":true,"yerlesik":false,"ikiAdimAcik":false,"olusturmaUtc":"2026-09-24T00:00:00Z","sonGirisUtc":null,"sonCihaz":null,"acikOturum":0}""");
        var yeni = await c.KullaniciEkleAsync(new KullaniciEkle("Ali", "ali", "viewer", "sifre-123"));
        Assert.Equal(5, yeni.Id);
        Assert.Equal("sifre-123", Govde(h).GetProperty("sifre").GetString());

        h.Kuyrukla(HttpStatusCode.OK);
        await c.KullaniciGuncelleAsync(5, new KullaniciGuncelle("Ali Veli", "viewer", false));
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.False(Govde(h).GetProperty("aktif").GetBoolean());

        h.Kuyrukla(HttpStatusCode.OK);
        await c.KullaniciSifreAsync(5, "yeni-sifre-1");
        Assert.EndsWith("/api/kullanicilar/5/sifre", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.OK);
        await c.KullaniciOturumlariniKapatAsync(5);
        Assert.EndsWith("/api/kullanicilar/5/oturumlari-kapat", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.OK);
        await c.KullaniciIkiAdimKapatAsync(5);
        Assert.EndsWith("/api/kullanicilar/5/iki-adim-kapat", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.KullaniciSilAsync(5);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        h.Kuyrukla(HttpStatusCode.OK);
        await c.IzleyiciSifresiniKaldirAsync();
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/ayarlar/izleyici-sifre", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Oturum_ve_giris_gunlugu_uclari()
    {
        var (c, h, _, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":"abc","kullaniciId":1,"adSoyad":"EMAR","rol":"editor","cihaz":"EMAR-LAPTOP","ip":"1.2.3.4",
              "olusturmaUtc":"2026-09-24T08:00:00Z","sonGorulmeUtc":"2026-09-24T09:00:00Z","bitisUtc":"2026-10-24T08:00:00Z","eski":false,"buOturum":true}]
            """);
        var o = Assert.Single(await c.OturumlarAsync());
        Assert.True(o.BuOturum);
        h.Kuyrukla(HttpStatusCode.OK);
        await c.OturumKapatAsync("abc");
        Assert.EndsWith("/api/oturumlar/abc/kapat", h.SonIstek!.RequestUri!.AbsolutePath);

        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":3,"zamanUtc":"2026-09-24T08:00:00Z","kullaniciAdi":"editor","adSoyad":null,"rol":null,"basarili":false,"neden":"Hatalı şifre","ip":"1.2.3.4","cihaz":"X"}]
            """, ("X-Toplam-Kayit", "42"));
        var s = await c.GirisKayitlariAsync(yalnizBasarisiz: true, 50, 0);
        Assert.Equal(42, s.Toplam);
        var tek = Assert.Single(s.Kayitlar);
        Assert.Equal("Hatalı şifre", tek.Neden);
        Assert.Equal(1, tek.Tekrar);            // alan yoksa tek deneme
        Assert.Null(tek.SonZamanUtc);
        Assert.Contains("basarisiz=true", h.SonIstek!.RequestUri!.Query);

        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":4,"zamanUtc":"2026-09-24T08:00:00Z","kullaniciAdi":"(çeşitli adlar)","adSoyad":null,"rol":null,"basarili":false,
              "neden":"Hatalı şifre","ip":"1.2.3.4","cihaz":null,"tekrar":37,"sonZamanUtc":"2026-09-24T08:58:00Z"}]
            """, ("X-Toplam-Kayit", "1"));
        var toplanan = Assert.Single((await c.GirisKayitlariAsync(yalnizBasarisiz: false, 50, 0)).Kayitlar);
        Assert.Equal(37, toplanan.Tekrar);
        Assert.Equal(new DateTime(2026, 9, 24, 8, 58, 0, DateTimeKind.Utc), toplanan.SonZamanUtc!.Value.ToUniversalTime());

        h.Kuyrukla(HttpStatusCode.OK, """{"editorOturumGun":7,"izleyiciOturumGun":30,"girisGunluguGun":180}""");
        Assert.Equal(7, (await c.GuvenlikAyariAsync()).EditorOturumGun);
        h.Kuyrukla(HttpStatusCode.OK);
        await c.GuvenlikAyariKaydetAsync(7);
        Assert.Equal(7, Govde(h).GetProperty("editorOturumGun").GetInt32());
    }

    [Fact]
    public async Task Soru_uclari_eslenir_filtreler_kodlanir()
    {
        var (c, h, _, _) = Kur();
        const string soru = """{"id":4,"hedefTur":"Islem","hedefId":12,"hafta":null,"hedefOzet":"10.06.2026 · Market","metin":"Bu ne?","soranId":null,"soranAd":"Ortak","soranRol":"viewer","sorulmaUtc":"2026-09-24T08:00:00Z","cevap":null,"cevaplayanAd":null,"cevaplanmaUtc":null,"durum":"Acik","kapanmaUtc":null}""";
        h.Kuyrukla(HttpStatusCode.OK, "[" + soru + "]");
        var liste = await c.SorularAsync(SoruDurumu.Acik, SoruHedefTuru.Hafta, 3, new DateOnly(2026, 6, 8));
        Assert.Equal(SoruHedefTuru.Islem, Assert.Single(liste).HedefTur);
        var q = h.SonIstek!.RequestUri!.Query;
        Assert.Contains("durum=Acik", q);
        Assert.Contains("hedefTur=Hafta", q);
        Assert.Contains("hedefId=3", q);
        Assert.Contains("hafta=2026-06-08", q);

        h.Kuyrukla(HttpStatusCode.Created, soru);
        var yeni = await c.SoruSorAsync(new SoruYaz(SoruHedefTuru.Islem, 12, null, "Bu ne?"));
        Assert.Equal(SoruDurumu.Acik, yeni.Durum);
        Assert.Equal("Islem", Govde(h).GetProperty("hedefTur").GetString());

        h.Kuyrukla(HttpStatusCode.OK, soru.Replace("\"Acik\"", "\"Kapali\""));
        Assert.Equal(SoruDurumu.Kapali, (await c.SoruCevaplaAsync(4, "Kargo.", kapat: true)).Durum);
        Assert.True(Govde(h).GetProperty("kapat").GetBoolean());

        h.Kuyrukla(HttpStatusCode.OK, """{"acikSayisi":3,"cevapBekleyen":2,"sonAciklar":[]}""");
        Assert.Equal(3, (await c.SoruOzetAsync()).AcikSayisi);
        h.Kuyrukla(HttpStatusCode.OK, soru);
        await c.SoruAcAsync(4);
        Assert.EndsWith("/api/sorular/4/ac", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.OK, soru);
        await c.SoruKapatAsync(4);
        Assert.EndsWith("/api/sorular/4/kapat", h.SonIstek!.RequestUri!.AbsolutePath);
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.SoruSilAsync(4);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
    }

    [Fact]
    public async Task Risk_karti_ve_gecmiste_kisi_cihaz_eslenir()
    {
        var (c, h, _, _) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"durum":"kirmizi","maddeler":[{"seviye":"Kirmizi","konu":"Disk","baslik":"Disk dolmak üzere","aciklama":"x"}],
             "hesaplanmaUtc":"2026-09-24T09:00:00Z","sonDogrulama":{"zamanUtc":"2026-09-24T02:00:00Z","dosya":"kasa-2026-09-24.db","dosyaZamaniUtc":"2026-09-24T00:05:00Z","basarili":true,"mesaj":"ok"},
             "diskBosMb":120,"dunBasarisizGiris":0}
            """);
        var r = await c.SistemRiskiAsync();
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(r.Maddeler).Seviye);
        Assert.True(r.SonDogrulama!.Basarili);

        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"zamanUtc":"2026-09-24T09:00:00Z","rol":"editor","tur":"Kanal","kayitId":1,"eylem":"Eklendi","ozet":"x",
              "eskiJson":null,"yeniJson":null,"geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":false,"kullanici":"EMAR","cihaz":"EMAR-LAPTOP"}]
            """);
        var d = Assert.Single((await c.GecmisAsync(null, 10, 0)).Kayitlar);
        Assert.Equal("EMAR", d.Kullanici);
        Assert.Equal("EMAR-LAPTOP", d.Cihaz);

        h.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer","ad":null,"kullaniciId":null}""");
        var ben = await c.BenAsync();
        Assert.Equal("viewer", ben.Rol);
        Assert.Null(ben.Ad);
    }
}
