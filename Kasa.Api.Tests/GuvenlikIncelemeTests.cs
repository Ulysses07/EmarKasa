using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>
/// Her isteğin kendi bağlantısıyla açtığı dosya DB'si: eşzamanlı istek testleri (paylaşılan tek
/// in-memory bağlantı aynı anda iki istekten kullanılamaz).
/// </summary>
public sealed class DosyaliKasaWebFactory : KasaWebFactory
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), $"kasa-test-{Guid.NewGuid():N}.db");

    protected override void VeritabaniAyarla(DbContextOptionsBuilder o) => o.UseSqlite($"Data Source={_yol}");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
        foreach (var ek in new[] { "", "-wal", "-shm" })
            try { File.Delete(_yol + ek); } catch (IOException) { }
    }
}

/// <summary>Paket E inceleme bulguları: giriş günlüğünün sınırı, iki adımlı girişin güvenliği, eski token'lar.</summary>
public class GuvenlikIncelemeTests
{
    private const string JwtKey = "test-jwt-anahtari-en-az-32-bayt-olmali!!";

    /// <summary>Editör token'ı (yerleşik editör, kişi claim'i yok). <paramref name="verilisUtc"/> null: bu sürümden önceki biçim (iat yok).</summary>
    private static string EditorTokeni(DateTime bitisUtc, DateTime? verilisUtc)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "editor"),
            new(JwtYardimci.SurumClaim, "0"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (verilisUtc is { } v)
            claims.Add(new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(v).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var t = new JwtSecurityToken(claims: claims, expires: bitisUtc, signingCredentials: new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(t);
    }

    private static HttpClient CihazliBearer(KasaWebFactory f, string token, string cihaz)
    {
        var c = Bearer(f, token);
        c.DefaultRequestHeaders.Add(CihazAdi.Baslik, Uri.EscapeDataString(cihaz));
        return c;
    }

    private static async Task<List<JsonElement>> GirislerAsync(HttpClient editor, bool yalnizBasarisiz = false)
        => (await editor.GetFromJsonAsync<List<JsonElement>>($"/api/guvenlik/girisler?limit=1000&basarisiz={yalnizBasarisiz.ToString().ToLowerInvariant()}"))!;

    private static string? Deger(JsonElement e, string ad) => e.GetProperty(ad).ValueKind == JsonValueKind.Null ? null : e.GetProperty(ad).GetString();

    // ── Giriş günlüğü sınırsız büyümesin ──

    [Fact]
    public async Task Uydurma_adlarla_basarisiz_denemeler_saatte_tek_satirda_sayilir()
    {
        using var f = new KasaWebFactory();
        for (var i = 0; i < 25; i++)
        {
            var (r, _) = await GirisAsync(f, $"uydurma-{i}-" + new string('x', 150), "yanlis", cihaz: "SALDIRGAN", ip: "198.51.100.9");
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        var (uzun, _) = await GirisAsync(f, new string('ş', 150), "yanlis", ip: "198.51.100.10");
        Assert.Equal(HttpStatusCode.Unauthorized, uzun.StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "editor", "yanlis", ip: "198.51.100.9")).Yanit.StatusCode);

        var editor = await EditorAsync(f);
        var basarisiz = await GirislerAsync(editor, yalnizBasarisiz: true);
        Assert.Equal(3, basarisiz.Count);   // 29 deneme, 3 satır

        var bilinmeyen = basarisiz.Single(g => Deger(g, "ip") == "198.51.100.9" && Deger(g, "rol") is null);
        Assert.Equal(25, bilinmeyen.GetProperty("tekrar").GetInt32());
        Assert.Equal(GuvenlikKurallari.CesitliAdlar, Deger(bilinmeyen, "kullaniciAdi"));
        Assert.Equal("SALDIRGAN", Deger(bilinmeyen, "cihaz"));
        Assert.NotEqual(JsonValueKind.Null, bilinmeyen.GetProperty("sonZamanUtc").ValueKind);

        var tek = basarisiz.Single(g => Deger(g, "ip") == "198.51.100.10");
        Assert.Equal(1, tek.GetProperty("tekrar").GetInt32());
        Assert.Equal(GuvenlikKurallari.GunlukAdEnCok, Deger(tek, "kullaniciAdi")!.Length);
        Assert.Equal(JsonValueKind.Null, tek.GetProperty("sonZamanUtc").ValueKind);

        // Bilinen hesap: ayrı satır, rolüyle; ad aynı kaldığı için "çeşitli" yazmaz.
        var editorSatiri = basarisiz.Single(g => Deger(g, "ip") == "198.51.100.9" && Deger(g, "rol") == "editor");
        Assert.Equal(3, editorSatiri.GetProperty("tekrar").GetInt32());
        Assert.Equal("editor", Deger(editorSatiri, "kullaniciAdi"));
        Assert.Equal(GirisNedenleri.HataliSifre, Deger(editorSatiri, "neden"));
        Assert.Equal("EDİTOR", Deger(editorSatiri, "adSoyad"));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(3, db.GirisKayitlari.Count(g => !g.Basarili));
        Assert.All(db.GirisKayitlari.AsNoTracking().ToList(), g => Assert.True(g.KullaniciAdi.Length <= GuvenlikKurallari.GunlukAdEnCok));
    }

    [Fact]
    public async Task Uzun_ad_kisaltilsa_da_baska_hesaba_girilmez()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var ad = new string('a', 50);
        await KullaniciEkleAsync(editor, "Uzun Adlı", ad, "viewer", "uzun-sifre-1");
        // Günlükte 50 karaktere kısaltılır ama giriş tam adla eşleşmeli.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, ad + "fazla", "uzun-sifre-1")).Yanit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, ad, "uzun-sifre-1")).Yanit.StatusCode);
    }

    [Fact]
    public async Task Risk_karti_toplanan_denemeleri_tek_tek_sayar()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var dunOgle = Saat.Bugun().AddDays(-1).ToDateTime(new TimeOnly(9, 0)); // TR 12:00
            db.GirisKayitlari.AddRange(
                new GirisKaydiEntity { ZamanUtc = dunOgle, KullaniciAdi = GuvenlikKurallari.CesitliAdlar, Basarili = false, Neden = GirisNedenleri.HataliSifre, Tekrar = 13 },
                new GirisKaydiEntity { ZamanUtc = dunOgle, KullaniciAdi = "editor", Basarili = false, Neden = GirisNedenleri.HataliKod },
                new GirisKaydiEntity { ZamanUtc = dunOgle, KullaniciAdi = "editor", Basarili = false, Neden = GirisNedenleri.KodBekleniyor, Tekrar = 4 });
            db.SaveChanges();
        }
        var r = await editor.GetFromJsonAsync<JsonElement>("/api/sistem/risk");
        Assert.Equal(14, r.GetProperty("dunBasarisizGiris").GetInt32());
        Assert.Contains(r.GetProperty("maddeler").EnumerateArray(), m => m.GetProperty("baslik").GetString() == "Dün 14 başarısız giriş");
    }

    [Fact]
    public void Bakim_basarisiz_denemeleri_kisa_saklar_ve_sayiyla_sinirlar()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        var simdi = new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
        db.GirisKayitlari.AddRange(
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-100), KullaniciAdi = "eski-basarili", Basarili = true },
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-40), KullaniciAdi = "eski-basarisiz" },
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-3), KullaniciAdi = "b1" },
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-2), KullaniciAdi = "b2" },
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-1), KullaniciAdi = "b3" },
            new GirisKaydiEntity { ZamanUtc = simdi.AddHours(-1), KullaniciAdi = "yeni-basarili", Basarili = true });
        db.SaveChanges();
        var cfg = new ConfigurationBuilder().Build();

        GuvenlikBakimServisi.Calistir(db, cfg, simdi);
        Assert.Equal(["b1", "b2", "b3", "eski-basarili", "yeni-basarili"],
            db.GirisKayitlari.AsNoTracking().Select(g => g.KullaniciAdi).OrderBy(a => a).ToList());

        // Sayı sınırı: en yeni başarısızlar kalır, başarılı girişlere dokunulmaz.
        GuvenlikBakimServisi.Calistir(db, cfg, simdi, basarisizEnCok: 2);
        Assert.Equal(["b2", "b3", "eski-basarili", "yeni-basarili"],
            db.GirisKayitlari.AsNoTracking().Select(g => g.KullaniciAdi).OrderBy(a => a).ToList());
    }

    // ── İki adımlı giriş: yalnız token'la açılamaz, kod bir kez geçer ──

    [Fact]
    public async Task Iki_adimli_giris_yalniz_oturum_tokeniyle_acilamaz()
    {
        using var f = new KasaWebFactory();
        var (r, _) = await GirisAsync(f, "editor", EditorSifre, cihaz: "EMAR-LAPTOP");
        var laptop = Bearer(f, await TokenAsync(r));   // kaybolan laptoptaki token
        var masaustu = await EditorAsync(f, cihaz: "EMAR-MASAUSTU");

        var b = await laptop.PostAsync("/api/hesap/iki-adim/baslat", null);
        b.EnsureSuccessStatusCode();
        var sir = (await JsonAsync(b)).GetProperty("sir").GetString()!;

        var sifresiz = await laptop.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir) });
        Assert.Equal(HttpStatusCode.BadRequest, sifresiz.StatusCode);
        Assert.Equal("Şifre hatalı.", await HataAsync(sifresiz));
        Assert.Equal(HttpStatusCode.BadRequest, (await laptop.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = "yanlis" })).StatusCode);

        // Hiçbir şey değişmedi: diğer oturum sürer, şifreyle kodsuz girilir.
        Assert.Equal(HttpStatusCode.OK, (await masaustu.GetAsync("/api/kanallar")).StatusCode);
        Assert.False((await masaustu.GetFromJsonAsync<JsonElement>("/api/hesap")).GetProperty("ikiAdimAcik").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "editor", EditorSifre)).Yanit.StatusCode);

        // Şifreyi bilen sahibi açabilir (kurulum hesaba bağlıdır, hangi cihazda başladığına değil).
        (await masaustu.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = EditorSifre })).EnsureSuccessStatusCode();
        Assert.True((await masaustu.GetFromJsonAsync<JsonElement>("/api/hesap")).GetProperty("ikiAdimAcik").GetBoolean());
    }

    [Fact]
    public async Task Kurtarma_kodlarini_yenilemek_sifre_ister()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var sir = (await JsonAsync(await editor.PostAsync("/api/hesap/iki-adim/baslat", null))).GetProperty("sir").GetString()!;
        (await editor.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = EditorSifre })).EnsureSuccessStatusCode();

        var sifresiz = await editor.PostAsJsonAsync("/api/hesap/kurtarma-kodlari", new { kod = SuAnkiKod(sir) });
        Assert.Equal(HttpStatusCode.BadRequest, sifresiz.StatusCode);
        Assert.Equal("Şifre hatalı.", await HataAsync(sifresiz));
        (await editor.PostAsJsonAsync("/api/hesap/kurtarma-kodlari", new { kod = SuAnkiKod(sir), sifre = EditorSifre })).EnsureSuccessStatusCode();
    }

    private static async Task<(string Sir, List<string> Kodlar)> IkiAdimAcAsync(KasaWebFactory f)
    {
        var editor = await EditorAsync(f);
        var sir = (await JsonAsync(await editor.PostAsync("/api/hesap/iki-adim/baslat", null))).GetProperty("sir").GetString()!;
        var o = await editor.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = EditorSifre });
        o.EnsureSuccessStatusCode();
        return (sir, (await JsonAsync(o)).GetProperty("kodlar").EnumerateArray().Select(x => x.GetString()!).ToList());
    }

    [Fact]
    public async Task Ayni_kurtarma_kodu_eszamanli_girislerde_bir_kez_gecer()
    {
        using var f = new DosyaliKasaWebFactory();
        var (_, kodlar) = await IkiAdimAcAsync(f);

        var sonuclar = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => GirisAsync(f, "editor", EditorSifre, kod: kodlar[0])));
        Assert.Equal(1, sonuclar.Count(s => s.Yanit.StatusCode == HttpStatusCode.OK));

        var editor = await GirisliAsync(f, "editor", EditorSifre, kod: kodlar[1]);
        Assert.Equal(8, (await editor.GetFromJsonAsync<JsonElement>("/api/hesap")).GetProperty("kurtarmaKoduKalan").GetInt32());
    }

    [Fact]
    public async Task Ayni_telefon_kodu_eszamanli_girislerde_bir_kez_gecer()
    {
        using var f = new DosyaliKasaWebFactory();
        var (sir, _) = await IkiAdimAcAsync(f);

        var kod = SuAnkiKod(sir);
        var sonuclar = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => GirisAsync(f, "editor", EditorSifre, kod: kod)));
        Assert.Equal(1, sonuclar.Count(s => s.Yanit.StatusCode == HttpStatusCode.OK));
    }

    // ── Eski token'lar: kısaltılan editör süresi ve cihaz adı ──

    [Fact]
    public async Task Kisaltilan_editor_suresine_eski_ve_yeni_tokenlar_uyar()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var simdi = DateTime.UtcNow;
        // Bu sürümden önceki token'larda iat yok: ömür sabit 30 gün olduğundan veriliş = bitiş − 30 gün.
        var eski8 = CihazliBearer(f, EditorTokeni(simdi.AddDays(22), null), "ESKI-8");
        var eski3 = CihazliBearer(f, EditorTokeni(simdi.AddDays(27), null), "ESKI-3");
        var yeni8 = CihazliBearer(f, EditorTokeni(simdi.AddDays(22), simdi.AddDays(-8)), "YENI-8");
        foreach (var c in new[] { eski8, eski3, yeni8 })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/kanallar")).StatusCode);
        var cihazlar = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!.Select(o => Deger(o, "cihaz")).ToList();
        Assert.Contains("ESKI-8", cihazlar);
        Assert.Contains("YENI-8", cihazlar);

        (await editor.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 7 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await eski8.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await yeni8.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await eski3.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode);

        // Açık oturum listesi de aynı kurala uyar.
        var liste = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!;
        cihazlar = liste.Select(o => Deger(o, "cihaz")).ToList();
        Assert.DoesNotContain("ESKI-8", cihazlar);
        Assert.DoesNotContain("YENI-8", cihazlar);
        var eski = liste.Single(o => Deger(o, "cihaz") == "ESKI-3");
        Assert.True(eski.GetProperty("eski").GetBoolean());
        // Eski satırın açılışı token'dan tahmin edilir (bitiş − 30 gün).
        Assert.InRange((simdi.AddDays(-3) - eski.GetProperty("olusturmaUtc").GetDateTime().ToUniversalTime()).Duration().TotalMinutes, 0, 5);
    }

    [Fact]
    public async Task Eski_token_cihaz_adini_istek_basligindan_alir()
    {
        using var f = new KasaWebFactory();
        var eski = CihazliBearer(f, JwtYardimci.Uret("editor", JwtKey, 0), "EMAR-LAPTOP");
        (await eski.PostAsJsonAsync("/api/kanallar", new { ad = "ESKİ-TOKEN-KANAL" })).EnsureSuccessStatusCode();

        var oturum = (await eski.GetFromJsonAsync<List<JsonElement>>("/api/oturumlar"))!.Single(o => o.GetProperty("buOturum").GetBoolean());
        Assert.True(oturum.GetProperty("eski").GetBoolean());
        Assert.Equal("EMAR-LAPTOP", Deger(oturum, "cihaz"));

        var gecmis = (await eski.GetFromJsonAsync<List<JsonElement>>("/api/gecmis?tur=Kanal"))!;
        var satir = gecmis.First(s => s.GetProperty("ozet").GetString()!.Contains("ESKİ-TOKEN-KANAL"));
        Assert.Equal("EMAR-LAPTOP", Deger(satir, "cihaz"));
        Assert.Equal("EDİTOR", Deger(satir, "kullanici"));   // yerleşik editöre ait
    }

    private sealed class AyarliSaat(DateTimeOffset an) : TimeProvider
    {
        public DateTimeOffset An { get; set; } = an;
        public override DateTimeOffset GetUtcNow() => An;
    }

    [Fact]
    public async Task Cihazsiz_acilmis_eski_oturum_baslik_gelince_cihazini_alir()
    {
        using var f = new KasaWebFactory();
        _ = f.CreateClient();
        var saat = new AyarliSaat(DateTimeOffset.UtcNow);
        var izleyici = new OturumIzleyici(f.Services.GetRequiredService<IServiceScopeFactory>(), saat, NullLogger<OturumIzleyici>.Instance);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(JwtYardimci.Uret("editor", JwtKey, 0));
        var kimlik = new ClaimsPrincipal(new ClaimsIdentity(token.Claims.Append(new Claim(ClaimTypes.Role, "editor")), "Bearer"));

        var ilk = new DefaultHttpContext();   // eski uygulama: başlık yok
        izleyici.Gorundu(ilk, kimlik, token);
        var sonra = new DefaultHttpContext();
        sonra.Request.Headers[CihazAdi.Baslik] = "EMAR-LAPTOP";
        saat.An += GuvenlikKurallari.SonGorulmeAraligi + TimeSpan.FromSeconds(1);
        izleyici.Gorundu(sonra, kimlik, token);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var satir = db.OturumKayitlari.AsNoTracking().Single(o => o.Jti == token.Id);
        Assert.True(satir.Eski);
        Assert.Equal("EMAR-LAPTOP", satir.Cihaz);
        await Task.CompletedTask;
    }

    // ── Kullanıcı listesinde kendi hesabı ──

    [Fact]
    public async Task Kendi_hesabinin_oturumlarini_ve_yetkisini_listeden_degistiremez()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var ikinciId = await KullaniciEkleAsync(editor, "İkinci Editör", "ikinci", "editor", "ikinci-sifre-1");
        var liste = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/kullanicilar"))!;
        var ben = liste.Single(k => k.GetProperty("ben").GetBoolean());
        Assert.True(ben.GetProperty("yerlesik").GetBoolean());
        Assert.False(liste.Single(k => k.GetProperty("id").GetInt32() == ikinciId).GetProperty("ben").GetBoolean());

        var kapat = await editor.PostAsync($"/api/kullanicilar/{ben.GetProperty("id").GetInt32()}/oturumlari-kapat", null);
        Assert.Equal(HttpStatusCode.BadRequest, kapat.StatusCode);
        Assert.Contains("Çıkış", await HataAsync(kapat));
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode);

        var ikinci = await GirisliAsync(f, "ikinci", "ikinci-sifre-1");
        Assert.True((await ikinci.GetFromJsonAsync<List<JsonElement>>("/api/kullanicilar"))!
            .Single(k => k.GetProperty("id").GetInt32() == ikinciId).GetProperty("ben").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ikinci.PutAsJsonAsync($"/api/kullanicilar/{ikinciId}", new { adSoyad = "İkinci Editör", rol = "viewer", aktif = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ikinci.PutAsJsonAsync($"/api/kullanicilar/{ikinciId}", new { adSoyad = "İkinci Editör", rol = "editor", aktif = false })).StatusCode);
        (await ikinci.PutAsJsonAsync($"/api/kullanicilar/{ikinciId}", new { adSoyad = "İkinci E.", rol = "editor", aktif = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await ikinci.GetAsync("/api/kanallar")).StatusCode);   // ad değişimi oturumu kapatmaz

        // Başkasının oturumlarını kapatmak çalışır.
        (await editor.PostAsync($"/api/kullanicilar/{ikinciId}/oturumlari-kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ikinci.GetAsync("/api/kanallar")).StatusCode);
    }
}
