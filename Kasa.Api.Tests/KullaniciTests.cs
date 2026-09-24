using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>13 · Her ortağa ayrı giriş: kişisel hesaplar, yetki ve geçmişte kişi adı.</summary>
public class KullaniciTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _f;
    public KullaniciTests(KasaWebFactory f) => _f = f;

    [Fact]
    public async Task Ortak_kendi_kullanici_adiyla_giris_yapar_ve_yalniz_okur()
    {
        var editor = await EditorAsync(_f);
        await KullaniciEkleAsync(editor, "Ali Ortak", "ali.ortak", "viewer", "ali-sifre-1");

        // Kullanıcı adı Türkçe büyük/küçük harf duyarsız.
        var (r, ali) = await GirisAsync(_f, "ALİ.ORTAK", "ali-sifre-1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await JsonAsync(r);
        Assert.Equal("viewer", j.GetProperty("rol").GetString());
        Assert.Equal("Ali Ortak", j.GetProperty("ad").GetString());

        var ben = await ali.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("viewer", ben.GetProperty("rol").GetString());
        Assert.Equal("Ali Ortak", ben.GetProperty("ad").GetString());

        Assert.Equal(HttpStatusCode.OK, (await ali.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ali.PostAsJsonAsync("/api/kanallar", new { ad = "ALI-KANAL" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ali.GetAsync("/api/kullanicilar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ali.GetAsync("/api/guvenlik/girisler")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ali.GetAsync("/api/oturumlar")).StatusCode);

        // Yanlış şifre 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(_f, "ali.ortak", "yanlis-sifre")).Yanit.StatusCode);
    }

    [Fact]
    public async Task Kullanici_adi_tekil_bicimi_ve_sifre_uzunlugu_dogrulanir()
    {
        var editor = await EditorAsync(_f);
        await KullaniciEkleAsync(editor, "Veli", "veli", "viewer", "veli-sifre-1");

        async Task<HttpStatusCode> Ekle(string kullaniciAdi, string sifre = "yeterli-sifre", string rol = "viewer", string ad = "X")
            => (await editor.PostAsJsonAsync("/api/kullanicilar", new { adSoyad = ad, kullaniciAdi, rol, sifre })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("VELİ"));          // aynı ad (Türkçe büyük harf)
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("Editor"));        // .env editörünün adı
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("iki kelime"));    // boşluk
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("ab"));            // kısa
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("gecerli.ad", sifre: "kisa"));
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("gecerli.ad", rol: "admin"));
        Assert.Equal(HttpStatusCode.BadRequest, await Ekle("gecerli.ad", ad: " "));
        Assert.Equal(HttpStatusCode.Created, await Ekle("gecerli.ad"));
    }

    [Fact]
    public async Task Pasif_yapilan_kullanicinin_oturumu_duser_ve_giremez()
    {
        var editor = await EditorAsync(_f);
        var id = await KullaniciEkleAsync(editor, "Pasif Olacak", "pasif.olacak", "viewer", "pasif-sifre-1");
        var kisi = await GirisliAsync(_f, "pasif.olacak", "pasif-sifre-1");
        Assert.Equal(HttpStatusCode.OK, (await kisi.GetAsync("/api/kanallar")).StatusCode);

        (await editor.PutAsJsonAsync($"/api/kullanicilar/{id}", new { adSoyad = "Pasif Olacak", rol = "viewer", aktif = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await kisi.GetAsync("/api/kanallar")).StatusCode);

        var (r, _) = await GirisAsync(_f, "pasif.olacak", "pasif-sifre-1");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("pasif", await HataAsync(r));

        var gunluk = await editor.GetFromJsonAsync<List<JsonElement>>($"/api/guvenlik/girisler?kullaniciId={id}");
        Assert.Equal("Hesap pasif", gunluk![0].GetProperty("neden").GetString());
    }

    [Fact]
    public async Task Rol_degisince_eski_oturum_kapanir_yeni_rolle_girer()
    {
        var editor = await EditorAsync(_f);
        var id = await KullaniciEkleAsync(editor, "Terfi", "terfi", "viewer", "terfi-sifre-1");
        var kisi = await GirisliAsync(_f, "terfi", "terfi-sifre-1");
        (await editor.PutAsJsonAsync($"/api/kullanicilar/{id}", new { adSoyad = "Terfi Eden", rol = "editor", aktif = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await kisi.GetAsync("/api/kanallar")).StatusCode);

        var yeni = await GirisliAsync(_f, "terfi", "terfi-sifre-1");
        Assert.Equal(HttpStatusCode.Created, (await yeni.PostAsJsonAsync("/api/kanallar", new { ad = "TERFI-KANAL" })).StatusCode);
    }

    [Fact]
    public async Task Sifre_sifirlaninca_eski_oturum_kapanir_yeni_sifre_calisir()
    {
        var editor = await EditorAsync(_f);
        var id = await KullaniciEkleAsync(editor, "Unutkan", "unutkan", "viewer", "unutkan-sifre-1");
        var kisi = await GirisliAsync(_f, "unutkan", "unutkan-sifre-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/kullanicilar/{id}/sifre", new { yeniSifre = "kisa" })).StatusCode);
        (await editor.PostAsJsonAsync($"/api/kullanicilar/{id}/sifre", new { yeniSifre = "unutkan-sifre-2" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await kisi.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(_f, "unutkan", "unutkan-sifre-1")).Yanit.StatusCode);
        await GirisliAsync(_f, "unutkan", "unutkan-sifre-2");
    }

    [Fact]
    public async Task Yerlesik_editor_korunur()
    {
        var editor = await EditorAsync(_f);
        var liste = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/kullanicilar"))!;
        var yerlesik = liste.Single(k => k.GetProperty("yerlesik").GetBoolean());
        var id = yerlesik.GetProperty("id").GetInt32();
        Assert.Equal("editor", yerlesik.GetProperty("kullaniciAdi").GetString());
        Assert.Equal("editor", yerlesik.GetProperty("rol").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync($"/api/kullanicilar/{id}", new { adSoyad = "EMAR", rol = "viewer", aktif = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync($"/api/kullanicilar/{id}", new { adSoyad = "EMAR", rol = "editor", aktif = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.DeleteAsync($"/api/kullanicilar/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/kullanicilar/{id}/sifre", new { yeniSifre = "baska-sifre-1" })).StatusCode);
        // Adı değişebilir; eski .env şifresi çalışmaya devam eder.
        (await editor.PutAsJsonAsync($"/api/kullanicilar/{id}", new { adSoyad = "EMAR", rol = "editor", aktif = true })).EnsureSuccessStatusCode();
        var ben = await (await EditorAsync(_f)).GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("EMAR", ben.GetProperty("ad").GetString());
        Assert.Equal(id, ben.GetProperty("kullaniciId").GetInt32());
    }

    [Fact]
    public async Task Kullanici_silinir_kendi_hesabi_silinmez()
    {
        var editor = await EditorAsync(_f);
        var id = await KullaniciEkleAsync(editor, "Gecici", "gecici", "viewer", "gecici-sifre-1");
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"/api/kullanicilar/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(_f, "gecici", "gecici-sifre-1")).Yanit.StatusCode);

        var ikinci = await KullaniciEkleAsync(editor, "İkinci Editör", "ikinci.editor", "editor", "ikinci-sifre-1");
        var ikinciIstemci = await GirisliAsync(_f, "ikinci.editor", "ikinci-sifre-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await ikinciIstemci.DeleteAsync($"/api/kullanicilar/{ikinci}")).StatusCode);
    }

    [Fact]
    public async Task Gecmiste_kisi_adi_ve_cihaz_gorunur()
    {
        var editor = await EditorAsync(_f);
        await KullaniciEkleAsync(editor, "Deniz Usta", "deniz", "editor", "deniz-sifre-1");
        var deniz = await GirisliAsync(_f, "deniz", "deniz-sifre-1", cihaz: "EMAR-LAPTOP");
        (await deniz.PostAsJsonAsync("/api/kanallar", new { ad = "DENİZ-KANAL" })).EnsureSuccessStatusCode();

        var satirlar = await editor.GetFromJsonAsync<List<JsonElement>>("/api/gecmis?tur=Kanal");
        var satir = satirlar!.First(s => s.GetProperty("ozet").GetString()!.Contains("DENİZ-KANAL"));
        Assert.Equal("editor", satir.GetProperty("rol").GetString());
        Assert.Equal("Deniz Usta", satir.GetProperty("kullanici").GetString());
        Assert.Equal("EMAR-LAPTOP", satir.GetProperty("cihaz").GetString());
    }

    [Fact]
    public async Task Kullanici_ekleme_gecmise_sifresiz_yazilir()
    {
        var editor = await EditorAsync(_f);
        await KullaniciEkleAsync(editor, "Gizli Sifreli", "gizli.sifreli", "viewer", "cok-gizli-kisisel");
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var hash = db.Kullanicilar.AsNoTracking().Single(k => k.KullaniciAdi == "gizli.sifreli").SifreHash!;
        var satirlar = db.Degisiklikler.AsNoTracking().Where(d => d.Tur == GecmisTurleri.Kullanici).ToList();
        Assert.Contains(satirlar, s => s.Ozet.Contains("Gizli Sifreli"));
        foreach (var s in satirlar)
        {
            var metin = string.Join("\n", s.Ozet, s.EskiJson, s.YeniJson);
            Assert.DoesNotContain("cok-gizli-kisisel", metin);
            foreach (var p in hash.Split('.')) Assert.DoesNotContain(p, metin);
            Assert.DoesNotContain("SifreHash", metin, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OturumSurumu", metin, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TotpSir", metin, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Kullanici_listesi_son_giris_ve_acik_oturum_sayisini_verir()
    {
        var editor = await EditorAsync(_f);
        var id = await KullaniciEkleAsync(editor, "Liste Kişi", "liste.kisi", "viewer", "liste-sifre-1");
        await GirisliAsync(_f, "liste.kisi", "liste-sifre-1", cihaz: "OFIS-PC");
        await GirisliAsync(_f, "liste.kisi", "liste-sifre-1", cihaz: "EV-PC");
        var k = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/kullanicilar"))!.Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(2, k.GetProperty("acikOturum").GetInt32());
        Assert.Equal("EV-PC", k.GetProperty("sonCihaz").GetString());
        Assert.NotEqual(JsonValueKind.Null, k.GetProperty("sonGirisUtc").ValueKind);
    }
}
