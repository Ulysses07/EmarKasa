using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.DenetimIziTests;
using static Kasa.Api.Tests.VekilVeHizSiniriTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Güvenlik olayları (giriş, hız sınırı, şifre/kurtarma, izleyici şifresi, alıcı oturum iptali) merkezi denetim tablosuna
/// gerçek istemci IP'siyle yazılır. Parola, parola özeti, kurtarma kodu, oturum belirteci ve tanıdık cihaz belirteci hiçbir
/// satırın hiçbir alanında bulunmaz.
/// </summary>
public class GuvenlikOlayiTests
{
    [Fact]
    public async Task Basarisiz_ve_basarili_giris_olay_olur_parola_ve_belirtecler_hicbir_alana_yazilmaz()
    {
        await using var f = new KasaWebFactory();
        using var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = " Editor ", sifre = "yanlis-parola-XYZ" })).StatusCode);
        var giris = await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" });
        Assert.Equal(HttpStatusCode.OK, giris.StatusCode);
        var govde = await giris.Content.ReadFromJsonAsync<JsonElement>();
        var cerezler = string.Join(";", giris.Headers.GetValues("Set-Cookie"));
        (await c.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();
        using var alici = f.CreateClient();
        var aliciGiris = await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici-1", sifre = "alici-sifre-1" });
        Assert.Equal(HttpStatusCode.OK, aliciGiris.StatusCode);
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();

        var olaylar = Olaylar(f, GuvenlikOlaylari.Varlik);
        var basarisiz = Assert.Single(olaylar, o => o.Tur == GuvenlikOlaylari.GirisBasarisiz);
        Assert.Equal("anonim", basarisiz.AktorRol);
        Assert.Equal("editor", (string?)J(basarisiz.YeniJson)["kullanici"]);
        Assert.Equal("POST /api/auth/login", (string?)J(basarisiz.YeniJson)["uc"]);
        var basarili = olaylar.Where(o => o.Tur == GuvenlikOlaylari.GirisBasarili).ToList();
        Assert.Equal(["editor", "alici", "viewer"], basarili.Select(o => o.AktorRol));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var aliciKaydi = db.Alicilar.AsNoTracking().Single();
            Assert.Equal(aliciKaydi.Id, basarili[1].AktorId);
            var tumu = TumMetin(db);
            string[] gizliler = ["yanlis-parola-XYZ", "kasa123", "alici-sifre-1", "izleyici-sifre-123", govde.GetProperty("token").GetString()!,
                govde.GetProperty("cihaz").GetString()!, aliciKaydi.SifreHash, db.Ayarlar.AsNoTracking().Single().IzleyiciSifreHash!,
                (await aliciGiris.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!];
            Assert.All(gizliler, g => Assert.DoesNotContain(g, tumu, StringComparison.Ordinal));
            Assert.All(cerezler.Split(';', ',').Select(p => p.Trim()).Where(p => p.StartsWith("kasa_", StringComparison.Ordinal)),
                cerez => Assert.DoesNotContain(cerez.Split('=', 2)[1], tumu, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Olay_guvenilen_vekilin_bildirdigi_gercek_istemci_ipsini_tasir_ve_uyari_olarak_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilFabrikasi(loglar: loglar);
        using var vekilden = Istemci(f, "203.0.113.9");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(vekilden, "editor", "yanlis")).StatusCode);
        // Güvenilmeyen bağlantının kendi X-Forwarded-For'u yok sayılır: bağlantı adresi yazılır.
        using var dogrudan = Istemci(f, "198.51.100.1", baglanti: "192.0.2.44");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(dogrudan, "editor", "yanlis")).StatusCode);

        Assert.Equal(["203.0.113.9", "192.0.2.44"], Olaylar(f, GuvenlikOlaylari.Varlik).Where(o => o.Tur == GuvenlikOlaylari.GirisBasarisiz).Select(o => o.IstemciIp));
        Assert.Contains(loglar.Uyarilar, u => u.Contains("Güvenlik olayı GirisBasarisiz", StringComparison.Ordinal) && u.Contains("203.0.113.9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hiz_siniri_reddi_olaydir_ayni_istemci_ve_ucta_dakikada_bir_yazilir()
    {
        await using var f = new VekilFabrikasi();
        using var saldirgan = Istemci(f, "198.51.100.20");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(saldirgan, "editor", "yanlis")).StatusCode);
        for (var i = 0; i < 3; i++)
            await Reddedildi(await Giris(saldirgan, "editor", "yanlis"));
        using var baska = Istemci(f, "198.51.100.21");
        for (var i = 0; i < 4; i++)
            await Giris(baska, "editor", "yanlis");

        var retler = Olaylar(f, GuvenlikOlaylari.Varlik).Where(o => o.Tur == GuvenlikOlaylari.HizSiniri).ToList();
        Assert.Equal(["198.51.100.20", "198.51.100.21"], retler.Select(o => o.IstemciIp));
        Assert.All(retler, o => Assert.Equal("giris", (string?)J(o.YeniJson)["ayrinti"]!["politika"]));
        Assert.Equal(6, Olaylar(f, GuvenlikOlaylari.Varlik).Count(o => o.Tur == GuvenlikOlaylari.GirisBasarisiz));
    }

    /// <summary>Şifre doğrulama kuyruğu doluyken verilen 429 sunucunun yoğun olmasıdır, saldırı reddi değil: hız sınırı olarak
    /// değil ayrı türle (istemci ağı ve uç başına dakikada bir) yazılır.</summary>
    [Fact]
    public async Task Dogrulama_kuyrugu_doluyken_verilen_429_hiz_siniri_degil_yogunluk_olayidir()
    {
        await using var f = new VekilFabrikasi(new()
        {
            ["Kasa:HizSiniri:AgBasarisizIzni"] = "1",
            ["Kasa:HizSiniri:HedefBasarisizIzni"] = "2",
            ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = "1",
            ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "1",
        });
        using var c = Istemci(f, "198.51.100.231");
        var sinir = f.Services.GetRequiredService<GirisSiniri>();
        using (var tutulan = await sinir.DogrulamaIzniAsync(CancellationToken.None))
        {
            var bekleyen = sinir.DogrulamaIzniAsync(CancellationToken.None);
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.TooManyRequests, (await Giris(c, "editor", "yanlis")).StatusCode);
            tutulan.Dispose();
            (await bekleyen).Dispose();
        }

        var olaylar = Olaylar(f, GuvenlikOlaylari.Varlik);
        Assert.DoesNotContain(olaylar, o => o.Tur == GuvenlikOlaylari.HizSiniri);
        var yogun = Assert.Single(olaylar, o => o.Tur == GuvenlikOlaylari.GirisYogun);
        Assert.Equal(("198.51.100.231", "editor"), (yogun.IstemciIp, (string?)J(yogun.YeniJson)["kullanici"]));
    }

    /// <summary>Kullanıcı adı yalnız bilinen bir hesaba (editör ya da alıcı) aitse olaya ve loga yazılır: ad alanına
    /// yanlışlıkla girilen parola düz metin olarak hiçbir yere düşmez.</summary>
    [Fact]
    public async Task Bilinmeyen_kullanici_adi_olaya_ve_loga_duz_metin_yazilmaz_bilinen_hesap_adi_yazilir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilFabrikasi(loglar: loglar);
        using (var editor = Istemci(f, "198.51.100.50"))
        {
            Assert.Equal(HttpStatusCode.OK, (await Giris(editor, "editor", "kasa123")).StatusCode);
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-7", "Alıcı", "alici-sifre-7"))).EnsureSuccessStatusCode();
        }
        using var c = Istemci(f, "198.51.100.51");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "Parolam-Gizli-42", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, " Alici-7 ", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);

        var adlar = Olaylar(f, GuvenlikOlaylari.Varlik).Where(o => o.Tur == GuvenlikOlaylari.GirisBasarisiz).Select(o => (string?)J(o.YeniJson)["kullanici"]);
        Assert.Equal([GuvenlikOlaylari.BilinmeyenAd, "alici-7", "editor"], adlar);
        Assert.Contains(loglar.Uyarilar, u => u.Contains("Güvenlik olayı GirisBasarisiz", StringComparison.Ordinal) && u.Contains(GuvenlikOlaylari.BilinmeyenAd, StringComparison.Ordinal));
        Assert.DoesNotContain(loglar.Uyarilar, u => u.Contains("parolam-gizli-42", StringComparison.OrdinalIgnoreCase));
        using var scope = f.Services.CreateScope();
        Assert.DoesNotContain("parolam-gizli-42", TumMetin(scope.ServiceProvider.GetRequiredService<KasaDbContext>()), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Geçersiz oturum belirteci ve rolün yetmediği uç (403) olay tablosuna yazılmaz ama 'Kasa.Guvenlik' logunda
    /// (tür ve istemci ağı başına dakikada bir) görünür; varsayılan 'Microsoft.AspNetCore: Warning' ayarı çerçevenin kendi
    /// kayıtlarını düşürür.</summary>
    [Fact]
    public async Task Gecersiz_belirtec_ve_yetki_reddi_guvenlik_loguna_seyrek_yazilir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilFabrikasi(loglar: loglar);
        using var sahte = Istemci(f, "198.51.100.60");
        sahte.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiZWRpdG9yIn0.c2FodGUtaW16YQ");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await sahte.GetAsync("/api/islemler")).StatusCode);
        using (var editor = Istemci(f, "198.51.100.61"))
        {
            Assert.Equal(HttpStatusCode.OK, (await Giris(editor, "editor", "kasa123")).StatusCode);
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();
        }
        using var izleyici = Istemci(f, "198.51.100.62");
        Assert.Equal(HttpStatusCode.OK, (await Giris(izleyici, "", "izleyici-sifre-123")).StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.GetAsync("/api/denetim")).StatusCode);

        Assert.Single(loglar.Uyarilar, u => u.Contains("Güvenlik olayı GecersizBelirtec", StringComparison.Ordinal)
            && u.Contains("198.51.100.60", StringComparison.Ordinal) && u.Contains("GET /api/islemler", StringComparison.Ordinal));
        Assert.Single(loglar.Uyarilar, u => u.Contains("Güvenlik olayı YetkiReddi", StringComparison.Ordinal) && u.Contains("viewer", StringComparison.Ordinal)
            && u.Contains("198.51.100.62", StringComparison.Ordinal) && u.Contains("GET /api/denetim", StringComparison.Ordinal));
        Assert.DoesNotContain(Olaylar(f, GuvenlikOlaylari.Varlik), o => o.Tur is "GecersizBelirtec" or "YetkiReddi");
    }

    [Fact]
    public async Task Sifre_kurtarma_kodu_ve_kurtarma_olay_olur_kod_ve_sifreler_yazilmaz()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "yanlis", yeniSifre = "yepyeni-sifre-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "yanlis" })).StatusCode);
        var kodYaniti = await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" });
        kodYaniti.EnsureSuccessStatusCode();
        var kod = (await kodYaniti.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kod").GetString()!;
        using var anonim = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod = "YANLISKOD", yeniSifre = "kurtarilan-sifre-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonim.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = "kurtarilan-sifre-123" })).StatusCode);
        using var yeni = f.CreateClient();
        (await yeni.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kurtarilan-sifre-123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await yeni.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kurtarilan-sifre-123", yeniSifre = "son-editor-sifre-123" })).StatusCode);

        var turler = Olaylar(f, GuvenlikOlaylari.Varlik).Select(o => o.Tur).Where(t => !t.StartsWith("Giris", StringComparison.Ordinal)).ToList();
        Assert.Equal([GuvenlikOlaylari.SifreDegistirmeBasarisiz, GuvenlikOlaylari.KurtarmaKoduUretimiBasarisiz, GuvenlikOlaylari.KurtarmaKoduUretildi,
            GuvenlikOlaylari.KurtarmaBasarisiz, GuvenlikOlaylari.KurtarmaKullanildi, GuvenlikOlaylari.SifreDegisti], turler);
        var kurtarma = Assert.Single(Olaylar(f, GuvenlikOlaylari.Varlik), o => o.Tur == GuvenlikOlaylari.KurtarmaKullanildi);
        Assert.Equal(("anonim", "1"), (kurtarma.AktorRol, kurtarma.VarlikId));
        Assert.Equal("editor", Assert.Single(Olaylar(f, GuvenlikOlaylari.Varlik), o => o.Tur == GuvenlikOlaylari.SifreDegisti).AktorRol);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var tumu = TumMetin(db);
        foreach (var gizli in new[] { kod, "YANLISKOD", "yepyeni-sifre-123", "kurtarilan-sifre-123", "son-editor-sifre-123", db.EditorGuvenlik.AsNoTracking().Single().SifreHash! })
            Assert.DoesNotContain(gizli, tumu, StringComparison.Ordinal);
        Assert.Empty(Olaylar(f, "EditorGuvenlik"));
    }

    [Fact]
    public async Task Izleyici_sifresi_degisimi_ve_alici_sifre_ve_oturum_iptali_gizli_degerle_olay_olur()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-456" })).EnsureSuccessStatusCode();
        var olustur = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-2", "Alıcı", "alici-sifre-2"));
        olustur.EnsureSuccessStatusCode();
        var id = (await olustur.Content.ReadFromJsonAsync<AliciDto>())!.Id;
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-2", "Alıcı", null, Aktif: false))).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-2", "Alıcı", "alici-sifre-3", Aktif: true))).EnsureSuccessStatusCode();

        var izleyici = Olaylar(f, "Ayar").Where(o => o.Tur == "IzleyiciSifresiDegisti").ToList();
        Assert.Equal(["""{"IzleyiciSifreHash":null}""", """{"IzleyiciSifreHash":"***"}"""], izleyici.Select(o => o.OncekiJson));
        Assert.All(izleyici, o => Assert.Equal(("""{"IzleyiciSifreHash":"***"}""", "editor"), (o.YeniJson, o.AktorRol)));
        var alici = Olaylar(f, "Alici", id);
        Assert.Equal(["Ekle", "AliciOturumlariKapatildi", "AliciSifresiDegisti"], alici.Select(o => o.Tur));
        Assert.Equal("***", (string?)J(alici[0].YeniJson)["SifreHash"]);
        Assert.Equal((true, false), (J(alici[1].OncekiJson)["Aktif"]!.GetValue<bool>(), J(alici[1].YeniJson)["Aktif"]!.GetValue<bool>()));
        Assert.Equal(1, J(alici[1].YeniJson)["OturumSurumu"]!.GetValue<int>());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var tumu = TumMetin(db);
        foreach (var gizli in new[] { "izleyici-sifre-123", "izleyici-sifre-456", "alici-sifre-2", "alici-sifre-3",
                     db.Alicilar.AsNoTracking().Single().SifreHash, db.Ayarlar.AsNoTracking().Single().IzleyiciSifreHash! })
            Assert.DoesNotContain(gizli, tumu, StringComparison.Ordinal);
    }

    /// <summary>İstek gerekçe başlığı (X-Kasa-Gerekce) yalnız oturumlu editör ya da alıcının kasa değişikliğine iliştirilir:
    /// kimliksiz giriş ve kurtarma isteği değiştirilemez denetim izinin gerekçe alanına istediği metni yazamaz; oturumlu
    /// editörün güvenlik olayı da başlıktaki metni taşımaz. Aynı başlık editörün gider silmesinde gerekçedir.</summary>
    [Fact]
    public async Task Gerekce_basligi_guvenlik_olaylarina_ve_kimliksiz_isteklere_yazilmaz()
    {
        await using var f = MonthlyExpenseTests.Fabrika();
        const string sahte = "Editör testi, yok sayın";
        using var anonim = f.CreateClient();
        anonim.DefaultRequestHeaders.Add(DenetimBaglami.GerekceBasligi, Uri.EscapeDataString(sahte));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "yanlis" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod = "YANLISKOD", yeniSifre = "kurtarilan-sifre-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonim.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonim.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-sifre-123" })).StatusCode);

        var olaylar = Olaylar(f, GuvenlikOlaylari.Varlik);
        Assert.Equal([GuvenlikOlaylari.GirisBasarisiz, GuvenlikOlaylari.KurtarmaBasarisiz, GuvenlikOlaylari.GirisBasarili, GuvenlikOlaylari.SifreDegisti], olaylar.Select(o => o.Tur));
        Assert.All(olaylar, o => Assert.Null(o.Gerekce));

        using var editor = f.CreateClient();
        (await editor.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "yepyeni-sifre-123" })).EnsureSuccessStatusCode();
        var gider = await MonthlyExpenseTests.Post<IslemEntity>(editor, "/api/islemler", new IslemYazDto(MonthlyExpenseTests.Today, "Toptancı", 10m, "MEZAT", Kasa.Core.GiderTipi.Cari));
        using var silme = new HttpRequestMessage(HttpMethod.Delete, $"/api/islemler/{gider.Id}");
        silme.Headers.Add(DenetimBaglami.GerekceBasligi, Uri.EscapeDataString("Mükerrer giriş"));
        Assert.Equal(HttpStatusCode.NoContent, (await editor.SendAsync(silme)).StatusCode);
        Assert.Equal("Mükerrer giriş", Assert.Single(Olaylar(f, "Islem", gider.Id), o => o.Tur == "Sil").Gerekce);
    }

    /// <summary>Başarılı şifre değişimi, kurtarma kodu üretimi ve kurtarma olayıyla aynı transaction'dadır: olay yazılamazsa
    /// değişiklik de kaydedilmez (istek başarısız olur; eski şifre ve eski kurtarma kodu geçerli kalır). Olay tablosunda izi
    /// olmayan şifre değişikliği olmaz.</summary>
    [Fact]
    public async Task Sifre_ve_kurtarma_olayi_yazilamazsa_degisiklik_geri_alinir()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        var kodYaniti = await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" });
        kodYaniti.EnsureSuccessStatusCode();
        var kod = (await kodYaniti.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kod").GetString()!;
        (string? Sifre, string? Kurtarma, int Surum) Durum()
        {
            using var scope = f.Services.CreateScope();
            var kayit = scope.ServiceProvider.GetRequiredService<KasaDbContext>().EditorGuvenlik.AsNoTracking().Single();
            return (kayit.SifreHash, kayit.KurtarmaHash, kayit.Surum);
        }
        void Sql(string komut)
        {
            using var scope = f.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<KasaDbContext>().Database.ExecuteSqlRaw(komut);
        }
        var once = Durum();
        Sql("CREATE TRIGGER TR_Test_Guvenlik_Reddi BEFORE INSERT ON DenetimOlaylari WHEN NEW.Tur IN ('SifreDegisti', 'KurtarmaKoduUretildi', 'KurtarmaKullanildi') BEGIN SELECT RAISE(ABORT, 'test: olay yazilamadi'); END;");

        static async Task Reddedildi(Task<HttpResponseMessage> istek)
        {
            using var yanit = await istek;
            Assert.False(yanit.IsSuccessStatusCode, $"{yanit.StatusCode}: {await yanit.Content.ReadAsStringAsync()}");
        }
        await Reddedildi(editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-sifre-123" }));
        await Reddedildi(editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" }));
        using var anonim = f.CreateClient();
        await Reddedildi(anonim.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = "kurtarilan-sifre-123" }));
        Assert.Equal(once, Durum());
        Assert.DoesNotContain(Olaylar(f, GuvenlikOlaylari.Varlik), o => o.Tur is GuvenlikOlaylari.SifreDegisti or GuvenlikOlaylari.KurtarmaKullanildi);

        Sql("DROP TRIGGER TR_Test_Guvenlik_Reddi;");
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonim.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = "kurtarilan-sifre-123" })).StatusCode);
        Assert.Single(Olaylar(f, GuvenlikOlaylari.Varlik), o => o.Tur == GuvenlikOlaylari.KurtarmaKullanildi);
    }

    private static string TumMetin(KasaDbContext db) => string.Join("\n", db.DenetimOlaylari.AsNoTracking().AsEnumerable()
        .Select(o => string.Join("|", o.AktorRol, o.IstemciIp, o.Tur, o.Varlik, o.VarlikId, o.OncekiJson, o.YeniJson, o.Gerekce, o.TraceId)));
}
