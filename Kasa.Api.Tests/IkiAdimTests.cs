using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>TOTP ve base32: RFC 6238 / RFC 4648 test vektörleri, kurtarma kodları.</summary>
public class TotpTests
{
    private static readonly byte[] RfcSir = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Rfc6238_sha1_vektorleri(long unix, string beklenen)
        => Assert.Equal(beklenen, Totp.Kod(RfcSir, Totp.AdimNo(DateTimeOffset.FromUnixTimeSeconds(unix)), hane: 8));

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_rfc4648_vektorleri(string ham, string beklenen)
    {
        Assert.Equal(beklenen, Base32.Yaz(Encoding.ASCII.GetBytes(ham)));
        Assert.Equal(ham, Encoding.ASCII.GetString(Base32.Oku(beklenen.ToLowerInvariant() + "====")!));
        Assert.Null(Base32.Oku("MZ1W"));
    }

    [Fact]
    public void Dogrulama_bir_adim_saat_farkini_kabul_eder_kullanilani_reddeder()
    {
        var sir = Totp.SirUret();
        Assert.Equal(32, sir.Length); // 20 bayt
        var an = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var adim = Totp.AdimNo(an);
        var bayt = Base32.Oku(sir)!;
        Assert.Equal(adim, Totp.Dogrula(sir, Totp.Kod(bayt, adim), an));
        Assert.Equal(adim - 1, Totp.Dogrula(sir, Totp.Kod(bayt, adim - 1), an));
        Assert.Equal(adim + 1, Totp.Dogrula(sir, Totp.Kod(bayt, adim + 1).Insert(3, " "), an));
        Assert.Null(Totp.Dogrula(sir, Totp.Kod(bayt, adim - 2), an));
        Assert.Null(Totp.Dogrula(sir, Totp.Kod(bayt, adim), an, sonKullanilan: adim));
        Assert.Null(Totp.Dogrula(sir, "12345", an));
        Assert.Null(Totp.Dogrula(sir, "abcdef", an));
        Assert.StartsWith("otpauth://totp/Emar%20Kasa:editor?secret=" + sir, Totp.Adres(sir, "editor"));
    }

    [Fact]
    public void Kurtarma_kodlari_bir_kez_kullanilir()
    {
        var kodlar = KurtarmaKodlari.Uret();
        Assert.Equal(KurtarmaKodlari.Adet, kodlar.Distinct().Count());
        Assert.All(kodlar, k => Assert.Matches("^[2-9A-HJ-NP-Z]{5}-[2-9A-HJ-NP-Z]{5}$", k));
        var kayit = KurtarmaKodlari.Hashle(kodlar);
        Assert.DoesNotContain(kodlar[0], kayit);
        Assert.Equal(10, KurtarmaKodlari.Kalan(kayit));

        Assert.True(KurtarmaKodlari.Kullan(kayit, kodlar[3].ToLowerInvariant().Replace("-", " "), out var kalan));
        Assert.Equal(9, KurtarmaKodlari.Kalan(kalan));
        Assert.False(KurtarmaKodlari.Kullan(kalan, kodlar[3], out _));
        Assert.False(KurtarmaKodlari.Kullan(kalan, "AAAAA-AAAAA", out _));
        Assert.False(KurtarmaKodlari.Kullan(kalan, "123456", out _));
    }
}

/// <summary>39 · Editör şifresi ve iki adımlı giriş (her test kendi DB'siyle: editör şifresi değişir).</summary>
public class IkiAdimTests
{
    private static async Task<(string Sir, List<string> Kodlar, HttpClient Editor)> IkiAdimAcAsync(KasaWebFactory f)
    {
        var editor = await EditorAsync(f, cihaz: "EMAR-LAPTOP");
        var b = await editor.PostAsync("/api/hesap/iki-adim/baslat", null);
        b.EnsureSuccessStatusCode();
        var j = await JsonAsync(b);
        var sir = j.GetProperty("sir").GetString()!;
        Assert.StartsWith("otpauth://totp/", j.GetProperty("adres").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = "000000" == SuAnkiKod(sir) ? "111111" : "000000", sifre = EditorSifre })).StatusCode);
        var o = await editor.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = EditorSifre });
        o.EnsureSuccessStatusCode();
        var oj = await JsonAsync(o);
        var kodlar = oj.GetProperty("kodlar").EnumerateArray().Select(x => x.GetString()!).ToList();
        // Açılınca diğer oturumlar kapanır; bu istemci yeni token'la (çerez) devam eder.
        Assert.False(string.IsNullOrEmpty(oj.GetProperty("token").GetString()));
        return (sir, kodlar, editor);
    }

    [Fact]
    public async Task Iki_adim_acilinca_giris_kod_ister_kod_bir_kez_kullanilir()
    {
        using var f = new KasaWebFactory();
        var eskiOturum = await EditorAsync(f);
        var (sir, kodlar, editor) = await IkiAdimAcAsync(f);
        Assert.Equal(10, kodlar.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await eskiOturum.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode);
        var hesap = await editor.GetFromJsonAsync<JsonElement>("/api/hesap");
        Assert.True(hesap.GetProperty("ikiAdimAcik").GetBoolean());
        Assert.Equal(10, hesap.GetProperty("kurtarmaKoduKalan").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsync("/api/hesap/iki-adim/baslat", null)).StatusCode);

        // Kodsuz: 401 + kodGerekli (.env şifresi hâlâ geçerli).
        var (r, _) = await GirisAsync(f, "editor", EditorSifre);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.True((await JsonAsync(r)).GetProperty("kodGerekli").GetBoolean());
        // Yanlış şifre + kod: kod sorulmaz, düz 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "editor", "yanlis", kod: SuAnkiKod(sir))).Yanit.StatusCode);

        var kod = SuAnkiKod(sir);
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "editor", EditorSifre, kod: kod)).Yanit.StatusCode);
        // Aynı kod ikinci kez kabul edilmez.
        var tekrar = (await GirisAsync(f, "editor", EditorSifre, kod: kod)).Yanit;
        Assert.Equal(HttpStatusCode.Unauthorized, tekrar.StatusCode);
        Assert.True((await JsonAsync(tekrar)).GetProperty("kodGerekli").GetBoolean());

        // Kurtarma kodu bir kez.
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "editor", EditorSifre, kod: kodlar[0])).Yanit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "editor", EditorSifre, kod: kodlar[0])).Yanit.StatusCode);
        hesap = await editor.GetFromJsonAsync<JsonElement>("/api/hesap");
        Assert.Equal(9, hesap.GetProperty("kurtarmaKoduKalan").GetInt32());

        var gunluk = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/guvenlik/girisler"))!.Select(g => g.GetProperty("neden").GetString()).ToList();
        Assert.Contains("Kod bekleniyor", gunluk);
        Assert.Contains("Hatalı kod", gunluk);
        Assert.Contains("Kurtarma kodu kullanıldı", gunluk);
    }

    [Fact]
    public async Task Bes_hatali_koddan_sonra_kod_girisi_kilitlenir()
    {
        using var f = new KasaWebFactory();
        var (sir, _, _) = await IkiAdimAcAsync(f);
        var yanlis = SuAnkiKod(sir) == "123456" ? "654321" : "123456";
        for (var i = 0; i < GuvenlikKurallari.KodKilitDenemesi; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "editor", EditorSifre, kod: yanlis)).Yanit.StatusCode);
        var r = (await GirisAsync(f, "editor", EditorSifre, kod: SuAnkiKod(sir))).Yanit;
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Çok fazla", await HataAsync(r));
    }

    [Fact]
    public async Task Iki_adim_sifre_ve_kodla_kapatilir_kurtarma_kodlari_yenilenir()
    {
        using var f = new KasaWebFactory();
        var (sir, kodlar, editor) = await IkiAdimAcAsync(f);
        var yeni = await editor.PostAsJsonAsync("/api/hesap/kurtarma-kodlari", new { kod = SuAnkiKod(sir), sifre = EditorSifre });
        yeni.EnsureSuccessStatusCode();
        var yeniKodlar = (await JsonAsync(yeni)).GetProperty("kodlar").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Empty(yeniKodlar.Intersect(kodlar));
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/hesap/kurtarma-kodlari", new { kod = kodlar[0], sifre = EditorSifre })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/hesap/iki-adim/kapat", new { sifre = "yanlis", kod = yeniKodlar[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/hesap/iki-adim/kapat", new { sifre = EditorSifre, kod = kodlar[1] })).StatusCode);
        (await editor.PostAsJsonAsync("/api/hesap/iki-adim/kapat", new { sifre = EditorSifre, kod = yeniKodlar[0] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "editor", EditorSifre)).Yanit.StatusCode);
    }

    [Fact]
    public async Task Baska_editor_ortagin_iki_adimini_kapatabilir()
    {
        using var f = new KasaWebFactory();
        var emar = await EditorAsync(f);
        var id = await KullaniciEkleAsync(emar, "Ortak Editör", "ortak.editor", "editor", "ortak-editor-1");
        var ortak = await GirisliAsync(f, "ortak.editor", "ortak-editor-1");
        var sir = (await JsonAsync(await ortak.PostAsync("/api/hesap/iki-adim/baslat", null))).GetProperty("sir").GetString()!;
        (await ortak.PostAsJsonAsync("/api/hesap/iki-adim/onayla", new { kod = SuAnkiKod(sir), sifre = "ortak-editor-1" })).EnsureSuccessStatusCode();
        Assert.True((await emar.GetFromJsonAsync<List<JsonElement>>("/api/kullanicilar"))!
            .Single(k => k.GetProperty("id").GetInt32() == id).GetProperty("ikiAdimAcik").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "ortak.editor", "ortak-editor-1")).Yanit.StatusCode);

        (await emar.PostAsync($"/api/kullanicilar/{id}/iki-adim-kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "ortak.editor", "ortak-editor-1")).Yanit.StatusCode);
    }

    [Fact]
    public async Task Editor_sifresini_degistirir_env_sifresi_gecersiz_olur()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f, cihaz: "EMAR-LAPTOP");
        var digerCihaz = await EditorAsync(f, cihaz: "OFIS");
        var hesap = await editor.GetFromJsonAsync<JsonElement>("/api/hesap");
        Assert.True(hesap.GetProperty("envSifresi").GetBoolean());
        Assert.True(hesap.GetProperty("yerlesik").GetBoolean());

        // Yanlış mevcut şifre 400 (401 değil: uygulama 401'de oturumu siler).
        var yanlis = await editor.PostAsJsonAsync("/api/hesap/sifre", new { mevcutSifre = "yanlis", yeniSifre = "yeni-guclu-sifre" });
        Assert.Equal(HttpStatusCode.BadRequest, yanlis.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/hesap/sifre", new { mevcutSifre = EditorSifre, yeniSifre = "kisa" })).StatusCode);

        var r = await editor.PostAsJsonAsync("/api/hesap/sifre", new { mevcutSifre = EditorSifre, yeniSifre = "yeni-guclu-sifre" });
        r.EnsureSuccessStatusCode();
        var token = await TokenAsync(r);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode);          // çerez yenilendi
        Assert.Equal(HttpStatusCode.OK, (await Bearer(f, token).GetAsync("/api/kanallar")).StatusCode); // gövdedeki token
        Assert.Equal(HttpStatusCode.Unauthorized, (await digerCihaz.GetAsync("/api/kanallar")).StatusCode);
        Assert.False((await editor.GetFromJsonAsync<JsonElement>("/api/hesap")).GetProperty("envSifresi").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await GirisAsync(f, "editor", EditorSifre)).Yanit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GirisAsync(f, "editor", "yeni-guclu-sifre")).Yanit.StatusCode);

        // Geçmişte şifre değişimi görünür, şifre görünmez.
        var gecmis = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/gecmis?tur=" + Uri.EscapeDataString("Kullanıcı")))!;
        var satir = Assert.Single(gecmis);
        Assert.Contains("Şifre değiştirildi", satir.GetProperty("ozet").GetString());
        Assert.DoesNotContain("yeni-guclu-sifre", satir.GetRawText());
        Assert.Equal("EMAR-LAPTOP", satir.GetProperty("cihaz").GetString());
    }

    [Fact]
    public async Task Editor_oturum_suresi_7_gun_secilebilir_izleyici_30_gun_kalir()
    {
        using var f = new KasaWebFactory();
        var editor = await EditorAsync(f);
        var ayar = await editor.GetFromJsonAsync<JsonElement>("/api/guvenlik/ayar");
        Assert.Equal(30, ayar.GetProperty("editorOturumGun").GetInt32());
        Assert.Equal(30, ayar.GetProperty("izleyiciOturumGun").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 31 })).StatusCode);
        (await editor.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 7 })).EnsureSuccessStatusCode();

        static TimeSpan Kalan(string token)
        {
            var govde = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            govde = govde.PadRight(govde.Length + (4 - govde.Length % 4) % 4, '=');
            var exp = JsonDocument.Parse(Convert.FromBase64String(govde)).RootElement.GetProperty("exp").GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(exp) - DateTimeOffset.UtcNow;
        }

        var et = await TokenAsync((await GirisAsync(f, "editor", EditorSifre)).Yanit);
        Assert.InRange(Kalan(et).TotalDays, 6.9, 7.01);
        await KullaniciEkleAsync(editor, "İzleyen", "izleyen", "viewer", "izleyen-sifre-1");
        var it = await TokenAsync((await GirisAsync(f, "izleyen", "izleyen-sifre-1")).Yanit);
        Assert.InRange(Kalan(it).TotalDays, 29.9, 30.01);

        var izleyici = Bearer(f, it);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.GetAsync("/api/hesap")).StatusCode);
    }

    [Fact]
    public void Sunucudan_sifirlama_env_sifresini_geri_getirir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kasa:EditorKullanici"] = "emar", ["Kasa:EditorSifre"] = "env-sifre",
        }).Build();
        KimlikTohumu.Hazirla(db, cfg, NullLogger.Instance, TimeProvider.System);
        var k = db.Kullanicilar.Single();
        Assert.True(k.Yerlesik);
        Assert.Equal("emar", k.KullaniciAdi);
        Assert.Equal("EMAR", k.AdSoyad);
        Assert.Equal(Roller.Editor, k.Rol);
        Assert.Equal(30, db.GuvenlikAyarlari.Single().EditorOturumGun);

        k.SifreHash = SifreHasher.Hashle("unutulan");
        k.TotpSir = Totp.SirUret();
        db.SaveChanges();
        KimlikTohumu.Hazirla(db, cfg, NullLogger.Instance, TimeProvider.System);
        Assert.NotNull(db.Kullanicilar.AsNoTracking().Single().SifreHash); // ayar yokken dokunulmaz

        var sifirla = new ConfigurationBuilder().AddConfiguration(cfg)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kasa:EditorSifresiniSifirla"] = "true", ["Kasa:EditorKullanici"] = "emar2" }).Build();
        KimlikTohumu.Hazirla(db, sifirla, NullLogger.Instance, TimeProvider.System);
        var s = db.Kullanicilar.AsNoTracking().Single();
        Assert.Null(s.SifreHash);
        Assert.Null(s.TotpSir);
        Assert.Equal(1, s.OturumSurumu);
        Assert.Equal("emar2", s.KullaniciAdi); // .env'deki ad değişince izler
    }
}
