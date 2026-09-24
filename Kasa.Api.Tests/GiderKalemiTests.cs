using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Core;

namespace Kasa.Api.Tests;

/// <summary>Sabit gider kalemleri: ekleme, ad değiştirme (eski işlemlere yansır), silme koruması.</summary>
public class GiderKalemiTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public GiderKalemiTests(KasaWebFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private record Kalem(int Id, string Ad, bool Aktif);
    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);

    private static string Ad(string kok) => kok + " " + Guid.NewGuid().ToString("N")[..6];

    private static Task<HttpResponseMessage> IslemEkle(HttpClient c, string ad, string tip, int? kartId = null)
        => c.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-06-29", cari = ad, tutarTl = 100m, kanal = "Ortak", tip, not = (string?)null, krediKartiId = kartId,
        });

    [Fact]
    public async Task Kalem_eklenir_sabit_gider_islemi_yalniz_kayitli_kalemle_girilir()
    {
        var c = await _factory.EditorClientAsync();
        var ad = Ad("Kira");

        // Kalem yokken sabit gider girilemez; hata kalemi söyler.
        var once = await IslemEkle(c, ad, "SabitGider");
        Assert.Equal(HttpStatusCode.BadRequest, once.StatusCode);
        Assert.Contains("sabit gider kalemi yok", await once.Content.ReadAsStringAsync());

        var ekle = await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true });
        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);

        // Büyük/küçük harf farkı kayıtlı yazıma çevrilir.
        var islem = await IslemEkle(c, ad.ToUpper(new System.Globalization.CultureInfo("tr-TR")), "SabitGider");
        Assert.Equal(HttpStatusCode.Created, islem.StatusCode);
        Assert.Equal(ad, (await islem.Content.ReadFromJsonAsync<IslemYanit>(Json))!.Cari);

        // Aynı ad ikinci kez eklenemez.
        var cift = await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = ad.ToLower(new System.Globalization.CultureInfo("tr-TR")), aktif = true });
        Assert.Equal(HttpStatusCode.BadRequest, cift.StatusCode);

        var liste = await c.GetFromJsonAsync<List<Kalem>>("/api/giderkalemleri", Json);
        Assert.Contains(liste!, k => k.Ad == ad);
    }

    [Fact]
    public async Task Cari_tipindeki_islem_hala_cari_listesine_bakar()
    {
        var c = await _factory.EditorClientAsync();
        var ad = Ad("SGK");
        (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true })).EnsureSuccessStatusCode();
        var r = await IslemEkle(c, ad, "Cari");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("cari yok", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await IslemEkle(c, "Market", "Cari")).StatusCode);
    }

    [Fact]
    public async Task Kalem_adi_degisince_eski_sabit_gider_islemleri_yeni_adi_alir_cari_islemleri_degismez()
    {
        var c = await _factory.EditorClientAsync();
        var ad = Ad("Elektrik");
        var kalem = await (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true }))
            .Content.ReadFromJsonAsync<Kalem>(Json);
        // Aynı adlı bir cari ve o cariye ait bir cari işlemi de olsun.
        (await c.PostAsJsonAsync("/api/cariler", new { ad, aktif = true })).EnsureSuccessStatusCode();
        var sabit = await (await IslemEkle(c, ad, "SabitGider")).Content.ReadFromJsonAsync<IslemYanit>(Json);
        var cari = await (await IslemEkle(c, ad, "Cari")).Content.ReadFromJsonAsync<IslemYanit>(Json);

        var yeniAd = Ad("Elektrik faturası");
        var put = await c.PutAsJsonAsync($"/api/giderkalemleri/{kalem!.Id}", new { ad = yeniAd, aktif = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var liste = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json);
        Assert.Equal(yeniAd, liste!.Single(i => i.Id == sabit!.Id).Cari);
        Assert.Equal(ad, liste!.Single(i => i.Id == cari!.Id).Cari);
    }

    [Fact]
    public async Task Islemi_olan_kalem_silinemez_islemsiz_kalem_silinir()
    {
        var c = await _factory.EditorClientAsync();
        var kullanilan = await (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = Ad("Maaş"), aktif = true }))
            .Content.ReadFromJsonAsync<Kalem>(Json);
        (await IslemEkle(c, kullanilan!.Ad, "SabitGider")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/giderkalemleri/{kullanilan.Id}")).StatusCode);

        var bos = await (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = Ad("Aidat"), aktif = true }))
            .Content.ReadFromJsonAsync<Kalem>(Json);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/giderkalemleri/{bos!.Id}")).StatusCode);
    }

    [Fact]
    public async Task Cari_adi_degisince_ayni_adli_sabit_gider_islemine_dokunulmaz()
    {
        var c = await _factory.EditorClientAsync();
        var ad = Ad("Vergi");
        (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true })).EnsureSuccessStatusCode();
        var cariYanit = await c.PostAsJsonAsync("/api/cariler", new { ad, aktif = true });
        var cariId = (await cariYanit.Content.ReadFromJsonAsync<Kalem>(Json))!.Id;
        var sabit = await (await IslemEkle(c, ad, "SabitGider")).Content.ReadFromJsonAsync<IslemYanit>(Json);

        (await c.PutAsJsonAsync($"/api/cariler/{cariId}", new { ad = Ad("Vergi dairesi"), aktif = true })).EnsureSuccessStatusCode();
        var liste = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json);
        Assert.Equal(ad, liste!.Single(i => i.Id == sabit!.Id).Cari);
        // Carinin yalnız sabit gider işlemi vardı: cari silinebilir.
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cariler/{cariId}")).StatusCode);
    }

    [Fact]
    public async Task Izleyici_kalem_listesini_gorur_ama_degistiremez()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        var login = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izle123" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        izleyici.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/giderkalemleri")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync("/api/giderkalemleri", new { ad = Ad("X"), aktif = true })).StatusCode);
    }
}
