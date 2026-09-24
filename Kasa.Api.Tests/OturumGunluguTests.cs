using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>38 · Kim, hangi cihazdan girdi: giriş günlüğü ve açık oturumlar.</summary>
public class OturumGunluguTests : IClassFixture<KasaWebFactory>
{
    private const string JwtKey = "test-jwt-anahtari-en-az-32-bayt-olmali!!";
    private readonly KasaWebFactory _f;
    public OturumGunluguTests(KasaWebFactory f) => _f = f;

    [Fact]
    public async Task Giris_gunlugu_basarili_ve_basarisiz_denemeyi_cihaz_ve_ip_ile_yazar()
    {
        var (hatali, _) = await GirisAsync(_f, "editor", "yanlis", cihaz: "EMAR-LAPTOP", ip: "203.0.113.7");
        Assert.Equal(HttpStatusCode.Unauthorized, hatali.StatusCode);
        var editor = await GirisliAsync(_f, "editor", EditorSifre, cihaz: "EMAR-LAPTOP");

        var r = await editor.GetAsync("/api/guvenlik/girisler?limit=50");
        r.EnsureSuccessStatusCode();
        Assert.True(int.Parse(r.Headers.GetValues("X-Toplam-Kayit").Single()) >= 2);
        var liste = (await r.Content.ReadFromJsonAsync<List<JsonElement>>())!;
        var basarisiz = liste.First(g => !g.GetProperty("basarili").GetBoolean() && g.GetProperty("ip").GetString() == "203.0.113.7");
        Assert.Equal("Hatalı şifre", basarisiz.GetProperty("neden").GetString());
        Assert.Equal("EMAR-LAPTOP", basarisiz.GetProperty("cihaz").GetString());
        Assert.Equal("editor", basarisiz.GetProperty("kullaniciAdi").GetString());
        Assert.Equal("editor", basarisiz.GetProperty("rol").GetString());   // hesap bilinince başarısızda da rol
        Assert.True(basarisiz.GetProperty("tekrar").GetInt32() >= 1);
        var basarili = liste.First(g => g.GetProperty("basarili").GetBoolean());
        Assert.Equal("editor", basarili.GetProperty("rol").GetString());
        Assert.Equal("EMAR-LAPTOP", basarili.GetProperty("cihaz").GetString());

        var yalnizBasarisiz = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/guvenlik/girisler?basarisiz=true"))!;
        Assert.All(yalnizBasarisiz, g => Assert.False(g.GetProperty("basarili").GetBoolean()));
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.GetAsync("/api/guvenlik/girisler?limit=0")).StatusCode);
    }

    [Fact]
    public async Task Ortak_izleyici_sifresi_eskisi_gibi_calisir_ve_gunluge_yazilir()
    {
        var editor = await EditorAsync(_f);
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ortak-izle-1" })).EnsureSuccessStatusCode();
        var (r, izleyici) = await GirisAsync(_f, null, "ortak-izle-1", cihaz: "ORTAK-TABLET");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("viewer", (await JsonAsync(r)).GetProperty("rol").GetString());
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/kanallar")).StatusCode);

        var gunluk = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/guvenlik/girisler"))!;
        Assert.Contains(gunluk, g => g.GetProperty("neden").GetString() == "Ortak izleyici şifresi" && g.GetProperty("cihaz").GetString() == "ORTAK-TABLET");

        var oturumlar = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        Assert.Contains(oturumlar, o => o.GetProperty("cihaz").GetString() == "ORTAK-TABLET"
                                        && o.GetProperty("adSoyad").GetString() == "Ortak izleyici şifresi");

        // Ortak şifre kaldırılınca o oturum kapanır, şifre artık çalışmaz.
        (await editor.DeleteAsync("/api/ayarlar/izleyici-sifre")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await izleyici.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(_f, null, "ortak-izle-1")).Yanit.StatusCode);
        var ayar = await editor.GetFromJsonAsync<JsonElement>("/api/ayarlar");
        Assert.False(ayar.GetProperty("izleyiciSifreVarMi").GetBoolean());
    }

    [Fact]
    public async Task Acik_oturumlar_listelenir_ve_tek_oturum_kapatilir()
    {
        var (r1, _) = await GirisAsync(_f, "editor", EditorSifre, cihaz: "OTURUM-A");
        var (r2, _) = await GirisAsync(_f, "editor", EditorSifre, cihaz: "OTURUM-B");
        var a = Bearer(_f, await TokenAsync(r1));
        var b = Bearer(_f, await TokenAsync(r2));

        var liste = (await a.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        var oa = liste.Single(o => o.GetProperty("cihaz").GetString() == "OTURUM-A");
        var ob = liste.Single(o => o.GetProperty("cihaz").GetString() == "OTURUM-B");
        Assert.True(oa.GetProperty("buOturum").GetBoolean());
        Assert.False(ob.GetProperty("buOturum").GetBoolean());
        Assert.Equal("editor", ob.GetProperty("rol").GetString());

        (await a.PostAsync($"/api/oturumlar/{ob.GetProperty("id").GetString()}/kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/kanallar")).StatusCode);
        liste = (await a.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        Assert.DoesNotContain(liste, o => o.GetProperty("cihaz").GetString() == "OTURUM-B");
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/api/oturumlar/yok-boyle-bir-oturum/kapat", null)).StatusCode);
    }

    [Fact]
    public async Task Cikis_oturumu_listeden_duser()
    {
        var (r, _) = await GirisAsync(_f, "editor", EditorSifre, cihaz: "CIKIS-PC");
        var c = Bearer(_f, await TokenAsync(r));
        (await c.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        var editor = await EditorAsync(_f);
        var liste = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        Assert.DoesNotContain(liste, o => o.GetProperty("cihaz").GetString() == "CIKIS-PC");
    }

    [Fact]
    public async Task Bu_surumden_onceki_token_calisir_ve_eski_oturum_olarak_gorunur()
    {
        // Eski biçim: yalnız rol + sv + jti (kişi claim'i yok).
        var eski = Bearer(_f, JwtYardimci.Uret("editor", JwtKey, 0));
        Assert.Equal(HttpStatusCode.OK, (await eski.GetAsync("/api/kanallar")).StatusCode);
        var ben = await eski.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("editor", ben.GetProperty("rol").GetString());
        Assert.Equal(JsonValueKind.Number, ben.GetProperty("kullaniciId").ValueKind); // yerleşik editöre ait

        var liste = (await eski.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        Assert.Contains(liste, o => o.GetProperty("eski").GetBoolean() && o.GetProperty("buOturum").GetBoolean());
    }

    [Fact]
    public async Task Cihaz_adi_temizlenir()
    {
        Assert.Equal("EMAR-LAPTOP", CihazAdi.Temizle(Uri.EscapeDataString("EMAR-LAPTOP")));
        Assert.Equal("Şükrü'nün PC", CihazAdi.Temizle(Uri.EscapeDataString("Şükrü'nün PC")));
        Assert.Equal("A B", CihazAdi.Temizle("A\r\n\tB"));
        Assert.Null(CihazAdi.Temizle("   "));
        Assert.Equal(CihazAdi.EnCok, CihazAdi.Temizle(new string('x', 500))!.Length);
        await Task.CompletedTask;
    }
}

/// <summary>"Tüm oturumları kapat" kişisel hesapları da çıkarır; ortak izleyici şifresinin değişmesi çıkarmaz.</summary>
public class OturumSurumuTests
{
    [Fact]
    public async Task Tum_oturumlari_kapat_herkesi_izleyici_sifresi_yalniz_ortaklari_cikarir()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        await KullaniciEkleAsync(editor, "Kişisel İzleyici", "kisisel", "viewer", "kisisel-sifre-1");
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ortak-1" })).EnsureSuccessStatusCode();
        var kisisel = await GirisliAsync(f, "kisisel", "kisisel-sifre-1");
        var ortak = await GirisliAsync(f, null, "ortak-1");

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ortak-2" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ortak.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await kisisel.GetAsync("/api/kanallar")).StatusCode);

        (await editor.PostAsync("/api/ayarlar/oturumlari-kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await kisisel.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await editor.GetAsync("/api/kanallar")).StatusCode);
    }

    [Fact]
    public async Task Kisinin_oturumlarini_kapat_yalniz_o_kisiyi_cikarir()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var id = await KullaniciEkleAsync(editor, "Bir", "bir.kisi", "viewer", "bir-sifre-1");
        await KullaniciEkleAsync(editor, "İki", "iki.kisi", "viewer", "iki-sifre-1");
        var bir = await GirisliAsync(f, "bir.kisi", "bir-sifre-1");
        var iki = await GirisliAsync(f, "iki.kisi", "iki-sifre-1");
        (await editor.PostAsync($"/api/kullanicilar/{id}/oturumlari-kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bir.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await iki.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode);
    }
}
