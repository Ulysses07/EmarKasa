using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KrediApiTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KrediApiTests(KasaWebFactory factory) => _factory = factory;

    private record KrediYanit(
        int Id, string Ad, decimal CekilenTutar, DateOnly CekimTarihi,
        int TaksitSayisi, decimal AylikOdeme, int OdemeGunu, string Kanal);

    [Fact]
    public async Task Editor_kredi_ekleyip_listeleyip_guncelleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/krediler", new
        {
            ad = "İhtiyaç Kredisi", cekilenTutar = 50_000.50m, cekimTarihi = "2026-08-03",
            taksitSayisi = 12, aylikOdeme = 4_800.25m, odemeGunu = 15, kanal = "Instagram",
        });
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var eklenen = await olustur.Content.ReadFromJsonAsync<KrediYanit>();
        Assert.NotNull(eklenen);
        Assert.Equal("İhtiyaç Kredisi", eklenen!.Ad);
        Assert.Equal(50_000.50m, eklenen.CekilenTutar);
        Assert.Equal(new DateOnly(2026, 8, 3), eklenen.CekimTarihi);
        Assert.Equal(12, eklenen.TaksitSayisi);
        Assert.Equal(4_800.25m, eklenen.AylikOdeme);
        Assert.Equal(15, eklenen.OdemeGunu);
        Assert.Equal("Instagram", eklenen.Kanal);

        var guncelle = await client.PutAsJsonAsync($"/api/krediler/{eklenen.Id}", new
        {
            ad = "İhtiyaç Kredisi", cekilenTutar = 50_000.50m, cekimTarihi = "2026-08-03",
            taksitSayisi = 12, aylikOdeme = 5_000m, odemeGunu = 20, kanal = "Ortak",
        });
        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);

        var liste = await client.GetFromJsonAsync<List<KrediYanit>>("/api/krediler");
        var g = liste!.Single(k => k.Id == eklenen.Id);
        Assert.Equal(5_000m, g.AylikOdeme);
        Assert.Equal(20, g.OdemeGunu);
        Assert.Equal("Ortak", g.Kanal);
        Assert.Equal(new DateOnly(2026, 8, 3), g.CekimTarihi);

        var sil = await client.DeleteAsync($"/api/krediler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        var sonListe = await client.GetFromJsonAsync<List<KrediYanit>>("/api/krediler");
        Assert.DoesNotContain(sonListe!, k => k.Id == eklenen.Id);
    }

    [Fact]
    public async Task Izleyici_kredi_okur_ama_ekleyemez()
    {
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        var okuma = await izleyici.GetAsync("/api/krediler");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        var yazma = await izleyici.PostAsJsonAsync("/api/krediler", new
        {
            ad = "X", cekilenTutar = 1m, cekimTarihi = "2026-08-03",
            taksitSayisi = 3, aylikOdeme = 1m, odemeGunu = 1, kanal = "Ortak",
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }
}
