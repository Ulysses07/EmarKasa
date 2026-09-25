using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class SaglamlikTests
{
    [Theory]
    [InlineData(0, 1, 100, 10)]
    [InlineData(32, 1, 100, 10)]
    [InlineData(1, 0, 100, 10)]
    [InlineData(1, 601, 100, 10)]
    [InlineData(1, 1, -1, 10)]
    [InlineData(1, 1, 100, -1)]
    public async Task Gecersiz_eski_kredi_guncellemesi_kaydedilmez_raporlar_calismaya_devam_eder(int gun, int taksit, decimal tutar, decimal odeme)
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var seed = LegacyFinanceSeed.Kredi(f, Kredi(15));
        var r = await c.PutAsJsonAsync($"/api/krediler/{seed.Id}", new
        {
            ad = "Hatalı kredi", cekilenTutar = tutar, cekimTarihi = "2026-09-01",
            taksitSayisi = taksit, aylikOdeme = odeme, odemeGunu = gun, kanal = "MEZAT"
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var hata = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(hata.GetProperty("errors").EnumerateObject().Any());
        var unchanged = Assert.Single((await c.GetFromJsonAsync<KrediEntity[]>("/api/krediler"))!);
        Assert.Equal(seed.CekilenTutar, unchanged.CekilenTutar);
        Assert.Equal(seed.AylikOdeme, unchanged.AylikOdeme);
        Assert.Equal(seed.OdemeGunu, unchanged.OdemeGunu);
        Assert.Equal(seed.TaksitSayisi, unchanged.TaksitSayisi);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/rapor/panel")).StatusCode);
    }

    [Fact]
    public async Task Gecersiz_guncelleme_gecerli_krediyi_bozmaz()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var id = LegacyFinanceSeed.Kredi(f, Kredi(15)).Id;
        var r = await c.PutAsJsonAsync($"/api/krediler/{id}", Kredi(0));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var liste = await c.GetFromJsonAsync<JsonElement[]>("/api/krediler");
        Assert.Equal(15, Assert.Single(liste!).GetProperty("odemeGunu").GetInt32());
    }

    [Fact]
    public async Task Kanal_adi_degisince_gelir_gider_ve_kredi_ayni_kanala_bagli_kalir()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-01", kanal = "MEZAT", tutarTl = 1000m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-02", cari = "Mal", kanal = "MEZAT", tutarTl = 100m, tip = "Cari" })).EnsureSuccessStatusCode();
        LegacyFinanceSeed.Kredi(f, Kredi(15));
        var kanallar = await c.GetFromJsonAsync<KanalEntity[]>("/api/kanallar");
        var mezat = kanallar!.Single(k => k.Ad == "MEZAT");
        var once = await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel");

        var r = await c.PutAsJsonAsync($"/api/kanallar/{mezat.Id}", new { ad = "MEZAT YENİ", aktif = true });
        r.EnsureSuccessStatusCode();
        var sonra = await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel");
        Assert.Equal(once!.GuncelKasa, sonra!.GuncelKasa);
        Assert.Equal(once.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye,
            sonra.Kanallar.Single(k => k.Kanal == "MEZAT YENİ").Bakiye);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.All(await db.Islemler.ToListAsync(), i => { Assert.Equal(mezat.Id, i.KanalId); Assert.Equal("MEZAT YENİ", i.Kanal); });
        Assert.All(await db.Gelenler.ToListAsync(), g => { Assert.Equal(mezat.Id, g.KanalId); Assert.Equal("MEZAT YENİ", g.Kanal); });
        Assert.All(await db.Krediler.ToListAsync(), k => { Assert.Equal(mezat.Id, k.KanalId); Assert.Equal("MEZAT YENİ", k.Kanal); });
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{mezat.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("MEZAT", HttpStatusCode.Conflict)]
    [InlineData("mezat", HttpStatusCode.Conflict)]
    [InlineData("Ortak", HttpStatusCode.BadRequest)]
    [InlineData("__KREDI__", HttpStatusCode.BadRequest)]
    [InlineData(" ", HttpStatusCode.BadRequest)]
    public async Task Gecersiz_ve_tekrar_kanal_adlari_reddedilir(string ad, HttpStatusCode durum)
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        Assert.Equal(durum, (await c.PostAsJsonAsync("/api/kanallar", new { ad })).StatusCode);
    }

    [Fact]
    public async Task Gelen_upsert_ayni_kimligi_korur_ve_yalniz_son_tutari_sayar()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        var ilk = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-01", kanal = "MEZAT", tutarTl = 100m });
        ilk.EnsureSuccessStatusCode();
        var ilkId = (await ilk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        for (var i = 1; i <= 5; i++)
            (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-01", kanal = "MEZAT", tutarTl = i * 100m })).EnsureSuccessStatusCode();
        var gelir = Assert.Single((await c.GetFromJsonAsync<JsonElement[]>("/api/gelenler"))!);
        Assert.Equal(ilkId, gelir.GetProperty("id").GetInt32());
        Assert.Equal(500m, gelir.GetProperty("tutarTl").GetDecimal());
        Assert.Equal(500m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    [Theory]
    [InlineData("Olmayan kanal", 1)]
    [InlineData("MEZAT", 1.001)]
    public async Task Bilinmeyen_kanal_ve_kurus_alti_tutar_reddedilir(string kanal, decimal tutar)
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-01", cari = "Test", kanal, tutarTl = tutar, tip = "Cari" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Olmayan_karta_odeme_kontrollu_400_doner()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = 999, tarih = "2026-06-01", tutar = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Gelecek_islem_panelin_guncel_kasasini_ve_taksit_ufkunu_ilerletmez()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = bugun, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = bugun.AddMonths(2), cari = "Gelecek", kanal = "MEZAT", tutarTl = 100m, tip = "Cari" })).EnsureSuccessStatusCode();
        LegacyFinanceSeed.Kredi(f, new("Plan", 0m, bugun, 1, 200m, bugun.Day, "MEZAT"));
        Assert.Equal(1000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    [Fact]
    public async Task Donem_baslangici_olmayan_gelir_kaydedilip_raporda_kaybolamaz()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        var r = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-02", kanal = "MEZAT", tutarTl = 1000m });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonElement[]>("/api/gelenler"))!);
    }

    [Fact]
    public async Task Takip_baslangici_degistirilerek_gelirler_rapordan_cikarilamaz()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-02", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-02", kanal = "MEZAT", tutarTl = 1000m })).EnsureSuccessStatusCode();
        var r = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    private static KrediYazDto Kredi(int gun) => new("Kredi", 0m, new DateOnly(2026, 6, 1), 1, 0m, gun, "MEZAT");
}
