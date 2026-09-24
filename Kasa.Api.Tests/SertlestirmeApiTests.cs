using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>2026-09 güvenlik + veri bütünlüğü denetimi düzeltmelerinin API testleri.</summary>
public class SertlestirmeApiTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public SertlestirmeApiTests(KasaWebFactory factory) => _factory = factory;

    private record GelenYanit(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
    private record CariYanit(int Id, string Ad, bool Aktif);
    private record IslemYanit(int Id, string Cari, string Kanal);
    private record HataYanit(string Hata);

    private static Task<HttpResponseMessage> IslemEkle(HttpClient c, string cari, string kanal = "MEZAT", decimal tutar = 10m, string tarih = "2026-07-10")
        => c.PostAsJsonAsync("/api/islemler", new { tarih, cari, tutarTl = tutar, kanal, tip = "Cari" });

    private static async Task<CariYanit> CariEkle(HttpClient c, string ad)
    {
        var r = await c.PostAsJsonAsync("/api/cariler", new { ad, aktif = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<CariYanit>())!;
    }

    [Fact]
    public async Task Gelen_donem_basina_normallesir_ve_takvim_disi_reddedilir()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        (await c.PostAsJsonAsync("/api/kanallar", new { ad = "NORM", sira = 5 })).EnsureSuccessStatusCode();

        // 10 Haziran Çarşamba → dönem 8 Haziran Pazartesi başlar; 12 Haziran aynı dönem.
        var r1 = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-10", kanal = "NORM", tutarTl = 100m });
        Assert.Equal(new DateOnly(2026, 6, 8), (await r1.Content.ReadFromJsonAsync<GelenYanit>())!.DonemStart);
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-12", kanal = "NORM", tutarTl = 250m })).EnsureSuccessStatusCode();
        // 30 Haziran Salı: hafta 29'unda başlar ama ay sınırı yok → 29 Haziran; 1 Temmuz → yeni dönem.
        var r2 = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-07-02", kanal = "NORM", tutarTl = 5m });
        Assert.Equal(new DateOnly(2026, 7, 1), (await r2.Content.ReadFromJsonAsync<GelenYanit>())!.DonemStart);

        var norm = (await c.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler"))!.Where(g => g.Kanal == "NORM").ToList();
        Assert.Equal(2, norm.Count);
        Assert.Equal(250m, norm.Single(g => g.DonemStart == new DateOnly(2026, 6, 8)).TutarTl);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-05-31", kanal = "NORM", tutarTl = 1m })).StatusCode);
        var ileri = Saat.Bugun().AddDays(10).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = ileri, kanal = "NORM", tutarTl = 1m })).StatusCode);
    }

    [Fact]
    public void Tekil_indexler_cift_kaniti_ve_gelen_satirini_db_seviyesinde_engeller()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        db.Gelenler.Add(new GelenEntity { DonemStart = new DateOnly(2020, 1, 6), Kanal = "TEKIL", TutarTl = 1m });
        db.SaveChanges();
        db.Gelenler.Add(new GelenEntity { DonemStart = new DateOnly(2020, 1, 6), Kanal = "TEKIL", TutarTl = 2m });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        db.Cariler.Add(new CariEntity { Ad = "X" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Kanal_adi_buyuk_kucuk_harf_duyarsiz_tekildir()
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "mezat" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "ORTAK" })).StatusCode);
    }

    [Fact]
    public async Task Cari_adi_degisince_islemler_tasinir_islemli_cari_silinemez()
    {
        var c = await _factory.EditorClientAsync();
        var cari = await CariEkle(c, "Mehmet Ltd");
        (await IslemEkle(c, "Mehmet Ltd")).EnsureSuccessStatusCode();

        var r = await c.PutAsJsonAsync($"/api/cariler/{cari.Id}", new { ad = "Mehmet AŞ", aktif = true });
        r.EnsureSuccessStatusCode();
        Assert.Single((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?cari=Mehmet AŞ"))!);
        Assert.Empty((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?cari=Mehmet Ltd"))!);

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/cariler/{cari.Id}")).StatusCode);
        var bos = await CariEkle(c, "Silinecek Cari");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cariler/{bos.Id}")).StatusCode);
    }

    [Fact]
    public async Task Cari_tekrar_eklenemez_ve_islem_kayitli_cariye_baglanir()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Işık Gıda");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/cariler", new { ad = "Işık Gıda" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/cariler", new { ad = "ışık gıda" })).StatusCode);

        var yok = await IslemEkle(c, "Olmayan Cari");
        Assert.Equal(HttpStatusCode.BadRequest, yok.StatusCode);
        Assert.Contains("cari yok", (await yok.Content.ReadFromJsonAsync<HataYanit>())!.Hata);

        // Türkçe büyük/küçük harf farkı tolere edilir, kayıtlı yazım saklanır.
        var r = await IslemEkle(c, "IŞIK GIDA");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("Işık Gıda", (await r.Content.ReadFromJsonAsync<IslemYanit>())!.Cari);
    }

    [Fact]
    public async Task Cariler_turkce_siralanir_ve_arama_buyuk_kucuk_harf_duyarsiz()
    {
        var c = await _factory.EditorClientAsync();
        foreach (var ad in new[] { "Zzeki TR", "Çelik TR", "İlker TR", "Şahin TR", "ahmet TR" })
            await CariEkle(c, ad);

        var liste = (await c.GetFromJsonAsync<List<CariYanit>>("/api/cariler?ara=tr"))!.Select(x => x.Ad).ToList();
        Assert.Equal(["ahmet TR", "Çelik TR", "İlker TR", "Şahin TR", "Zzeki TR"], liste);

        Assert.Equal(["İlker TR"], (await c.GetFromJsonAsync<List<CariYanit>>("/api/cariler?ara=ilker"))!.Select(x => x.Ad));
        Assert.Equal(["Çelik TR"], (await c.GetFromJsonAsync<List<CariYanit>>("/api/cariler?ara=ÇELİK"))!.Select(x => x.Ad));
    }

    [Theory]
    [InlineData(1.005)]
    [InlineData(200000000000)]
    public async Task Gecersiz_tutar_400(decimal tutar)
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await IslemEkle(c, "X", tutar: tutar)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "TUTAR-" + tutar, acilisDevri = tutar })).StatusCode);
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(1999, 5)]
    [InlineData(2101, 5)]
    public async Task Aylik_rapor_gecersiz_ay_yil_400(int yil, int ay)
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/rapor/aylik?yil={yil}&ay={ay}")).StatusCode);
    }

    [Fact]
    public async Task Takip_baslangici_araligi_dogrulanir()
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "1999-12-31", kasaAcilisDevri = 0m })).StatusCode);
        var cokIleri = Saat.Bugun().AddYears(1).AddDays(2).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = cokIleri, kasaAcilisDevri = 0m })).StatusCode);
    }

    [Fact]
    public async Task Hata_mesajindaki_kullanici_girdisi_kisaltilir()
    {
        var c = await _factory.EditorClientAsync();
        var uzun = new string('Q', 500);
        var r = await IslemEkle(c, "X", kanal: uzun);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var hata = (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata;
        Assert.True(hata.Length < 120, hata);
    }

    [Fact]
    public async Task Islemler_limit_offset_sayfalar_toplami_basliga_yazar()
    {
        var c = await _factory.EditorClientAsync();
        (await c.PostAsJsonAsync("/api/kanallar", new { ad = "SAYFA", sira = 7 })).EnsureSuccessStatusCode();
        for (int i = 1; i <= 3; i++)
            (await IslemEkle(c, "X", kanal: "SAYFA", tutar: i, tarih: $"2026-07-0{i}")).EnsureSuccessStatusCode();

        var hepsi = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?kanal=SAYFA");
        Assert.Equal(3, hepsi!.Count);

        var r = await c.GetAsync("/api/islemler?kanal=SAYFA&limit=2");
        Assert.Equal("3", r.Headers.GetValues("X-Toplam-Kayit").Single());
        Assert.Equal(2, (await r.Content.ReadFromJsonAsync<List<IslemYanit>>())!.Count);
        var son = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?kanal=SAYFA&limit=2&offset=2");
        Assert.Equal(hepsi[2].Id, Assert.Single(son!).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler?limit=0")).StatusCode);
    }

    [Fact]
    public async Task Cikis_sunulan_bearer_tokeni_iptal_eder_digerleri_calisir()
    {
        var anon = _factory.CreateClient();
        async Task<string> Token()
        {
            var r = await anon.PostAsJsonAsync("/api/auth/login", KasaWebFactory.EditorGirisi);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        }
        var t1 = await Token();
        var t2 = await Token();

        HttpClient Bearer(string t)
        {
            var c = _factory.CreateClient(new() { HandleCookies = false });
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
            return c;
        }
        var c1 = Bearer(t1);
        Assert.Equal(HttpStatusCode.OK, (await c1.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c1.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c1.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(t2).GetAsync("/api/kanallar")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<KasaDbContext>().IptalEdilenTokenlar.Any());
    }
}

/// <summary>Takvim bugünde biter; "bu hafta" ay sonunda bölünmüş haftanın tamamını kapsar.</summary>
public class TakvimSonuTests : IClassFixture<TakvimSonuTests.SabitSaatFactory>
{
    /// <summary>Bugün = 1 Ekim 2026 Perşembe (hafta 28 Eyl Pzt – 4 Eki Paz, 30 Eyl'de bölünür).</summary>
    public class SabitSaatFactory : KasaWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitSaat())));
        }
    }

    private sealed class SabitSaat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    }

    private record DonemYanit(DateOnly Start, DateOnly End);
    private record PanelYanit(decimal GuncelKasa, decimal BuHaftaSonucu, decimal BuAySonucu);

    private readonly SabitSaatFactory _factory;
    public TakvimSonuTests(SabitSaatFactory factory) => _factory = factory;

    [Fact]
    public async Task Ileri_tarihli_islem_takvimi_uzatmaz_bu_hafta_bolunmus_haftayi_toplar()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1), kasaAcilis: 1_000m);
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-25", kanal = "MEZAT", tutarTl = 7m })).EnsureSuccessStatusCode();   // geçen hafta
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-29", kanal = "MEZAT", tutarTl = 100m })).EnsureSuccessStatusCode(); // 28–30 Eyl
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-10-01", kanal = "MEZAT", tutarTl = 50m })).EnsureSuccessStatusCode();  // 1 Eki–
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-11-20", cari = "X", tutarTl = 999m, kanal = "MEZAT", tip = "Cari" })).EnsureSuccessStatusCode();

        var donemler = (await c.GetFromJsonAsync<List<DonemYanit>>("/api/donemler"))!;
        Assert.Equal(new DateOnly(2026, 10, 1), donemler[^1].End);

        var panel = (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!;
        Assert.Equal(150m, panel.BuHaftaSonucu);
        Assert.Equal(1_157m, panel.GuncelKasa);  // ileri tarihli 999 düşülmez
        Assert.Equal(50m, panel.BuAySonucu);
    }
}

/// <summary>Genel giriş sınırı ve güvenilir proxy (X-Forwarded-For) kuralları.</summary>
public class GirisGuvenlikTests
{
    public class GenelLimitFactory : KasaWebFactory { protected override int GirisGlobalLimiti => 4; }
    public class IpLimitFactory : KasaWebFactory { protected override int GirisLimiti => 3; }

    private static async Task<List<HttpStatusCode>> Dene(KasaWebFactory f, int n, Func<int, (string ip, string? xff)> kaynak)
    {
        var c = f.CreateClient();
        var kodlar = new List<HttpStatusCode>();
        for (int i = 0; i < n; i++)
        {
            var (ip, xff) = kaynak(i);
            using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { kullanici = "editor", sifre = "yanlis" }),
            };
            istek.Headers.Add(KasaWebFactory.TestIpBasligi, ip);
            if (xff is not null) istek.Headers.Add("X-Forwarded-For", xff);
            kodlar.Add((await c.SendAsync(istek)).StatusCode);
        }
        return kodlar;
    }

    [Fact]
    public async Task Genel_sinir_farkli_iplerden_gelse_de_429_doner()
    {
        using var f = new GenelLimitFactory();
        var kodlar = await Dene(f, 6, i => ($"203.0.113.{i + 1}", null));
        Assert.Equal(HttpStatusCode.Unauthorized, kodlar[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, kodlar[^1]);
    }

    [Fact]
    public async Task Guvenilmeyen_kaynaktan_XFF_yok_sayilir_guvenilir_proxyden_kullanilir()
    {
        using var f = new IpLimitFactory();
        // Dış IP her istekte XFF'i değiştirse de kendi IP'siyle sınırlanır.
        var sahte = await Dene(f, 5, i => ("203.0.113.9", $"10.9.0.{i + 1}"));
        Assert.Equal(HttpStatusCode.TooManyRequests, sahte[^1]);
        // Docker ağındaki proxy (varsayılan güvenilir) → XFF'teki gerçek istemci IP'si esas alınır.
        var proxy = await Dene(f, 5, i => ("172.18.0.2", $"198.51.100.{i + 1}"));
        Assert.All(proxy, k => Assert.Equal(HttpStatusCode.Unauthorized, k));
    }
}
