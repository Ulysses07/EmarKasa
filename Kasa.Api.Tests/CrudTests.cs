using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Core;

namespace Kasa.Api.Tests;

public class CrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public CrudTests(KasaWebFactory factory) => _factory = factory;

    // Sunucu enum'ları string serileştiriyor (JsonStringEnumConverter); istemci de aynısını çözmeli.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);

    [Fact]
    public async Task Editor_islem_ekleyip_listeleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-06-29",
            cari = "PORT KARGO",
            tutarTl = 3874.03m,
            kanal = "MEZAT",
            tip = "Cari",
            not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var eklenen = await olustur.Content.ReadFromJsonAsync<IslemYanit>(Json);
        Assert.NotNull(eklenen);
        Assert.Equal("PORT KARGO", eklenen!.Cari);
        Assert.Equal(GiderTipi.Cari, eklenen.Tip);

        var liste = await client.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json);
        Assert.Contains(liste!, i => i.Id == eklenen.Id);

        var sil = await client.DeleteAsync($"/api/islemler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        var listeSonra = await client.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json);
        Assert.DoesNotContain(listeSonra!, i => i.Id == eklenen.Id);
    }

    [Fact]
    public async Task Izleyici_mutasyon_yapamaz_403()
    {
        // İzleyici şifresini editör olarak ayarla, sonra izleyici olarak login ol.
        var editor = await _factory.EditorClientAsync();
        var setSifre = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });
        setSifre.EnsureSuccessStatusCode();

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        // Okuma serbest:
        var okuma = await izleyici.GetAsync("/api/islemler");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        // Yazma yasak:
        var yazma = await izleyici.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-06-29", cari = "X", tutarTl = 1m, kanal = "MEZAT", tip = "Cari", not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }

    [Fact]
    public async Task Gelen_upsert_ayni_donem_kanal_icin_gunceller()
    {
        var client = await _factory.EditorClientAsync();

        await client.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-29", kanal = "MEZAT", tutarTl = 100m });
        await client.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-29", kanal = "MEZAT", tutarTl = 289_425m });

        var liste = await client.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler?donemStart=2026-06-29");
        var mezat = liste!.Where(g => g.Kanal == "MEZAT").ToList();
        Assert.Single(mezat);                       // upsert: tek satır
        Assert.Equal(289_425m, mezat[0].TutarTl);   // güncellenmiş değer
    }

    private record GelenYanit(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
}
