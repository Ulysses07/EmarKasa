using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;

namespace Kasa.Api.Tests;

/// <summary>
/// Eski (takipsiz) kredinin geçmiş kasa etkisi korunur (gap-tarihsel-spec-ve-emekli-web-6). Eski kredinin çekimi ve
/// taksitleri kayıttan bellekte türetildiği için silme ya da tutar/tarih/taksit/kanal düzeltmesi bütün geçmiş
/// raporları kilitsiz ve izsiz yeniden yazardı. Çekimi bugün ya da daha önce olan eski kredi silinemez, mali alanları
/// değiştirilemez (409); yalnız adı düzeltilebilir. Çekimi ileride olan (henüz etkisi olmayan) kredi serbesttir ama
/// geçmiş tarihe taşınamaz. Sunucunun "bugün"ü sabittir.
/// </summary>
public class EskiKrediKorumaTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);
    private static readonly DateOnly Baslangic = new(2026, 6, 29);

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 100_000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static readonly KrediYazDto Gecmis = new("Eski kredi", 120_000m, new DateOnly(2026, 7, 6), 6, 11_000m, 15, "MEZAT");
    private static readonly KrediYazDto Gelecek = new("İleri kredi", 60_000m, new DateOnly(2026, 10, 10), 6, 10_500m, 15, "MEZAT");
    private static async Task<string> Raporlar(HttpClient c) =>
        await c.GetStringAsync("/api/rapor/panel") + await c.GetStringAsync("/api/rapor/haftalik")
        + await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=7") + await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9");
    private static async Task<KrediEntity> Kayit(HttpClient c, int id) => (await c.GetFromJsonAsync<KrediEntity[]>("/api/krediler"))!.Single(k => k.Id == id);
    private static async Task<string> Hata(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

    [Fact]
    public async Task Gecmis_etkili_eski_kredi_silinemez_rapor_ve_liste_degismez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gecmis);
        // Çekim genel kasaya girdi, 15 Temmuz/Ağustos/Eylül taksitleri düştü.
        Assert.Equal(100_000m + 120_000m - 3 * 11_000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        var once = await Raporlar(c);

        var r = await c.DeleteAsync($"/api/krediler/{kredi.Id}");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Geçmiş kasa etkisi olan eski kredi silinemez", await Hata(r));
        Assert.Equal(Gecmis.CekilenTutar, (await Kayit(c, kredi.Id)).CekilenTutar);
        Assert.Equal(once, await Raporlar(c));
    }

    [Fact]
    public async Task Bugun_cekilen_eski_kredi_gecmis_etkili_sayilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gecmis with { CekimTarihi = Bugun });
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/krediler/{kredi.Id}")).StatusCode);
        Assert.Single(await c.GetFromJsonAsync<KrediEntity[]>("/api/krediler") ?? []);
    }

    [Theory]
    [InlineData("cekilenTutar")]
    [InlineData("cekimTarihi")]
    [InlineData("taksitSayisi")]
    [InlineData("aylikOdeme")]
    [InlineData("odemeGunu")]
    [InlineData("kanal")]
    public async Task Gecmis_etkili_eski_kredinin_mali_alani_degistirilemez(string alan)
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gecmis);
        var once = await Raporlar(c);
        var degisik = alan switch
        {
            "cekilenTutar" => Gecmis with { CekilenTutar = 100_000m },
            "cekimTarihi" => Gecmis with { CekimTarihi = new(2026, 7, 7) },
            "taksitSayisi" => Gecmis with { TaksitSayisi = 12 },
            "aylikOdeme" => Gecmis with { AylikOdeme = 12_000m },
            "odemeGunu" => Gecmis with { OdemeGunu = 20 },
            "kanal" => Gecmis with { Kanal = "PERAKENDE" },
            _ => throw new ArgumentOutOfRangeException(nameof(alan)),
        };

        var r = await c.PutAsJsonAsync($"/api/krediler/{kredi.Id}", degisik);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("yalnız ad düzeltilebilir", await Hata(r));
        var sonra = await Kayit(c, kredi.Id);
        Assert.Equal((Gecmis.CekilenTutar, Gecmis.CekimTarihi, Gecmis.TaksitSayisi, Gecmis.AylikOdeme, Gecmis.OdemeGunu, Gecmis.Kanal),
            (sonra.CekilenTutar, sonra.CekimTarihi, sonra.TaksitSayisi, sonra.AylikOdeme, sonra.OdemeGunu, sonra.Kanal));
        Assert.Equal(once, await Raporlar(c));
    }

    [Fact]
    public async Task Gecmis_etkili_eski_kredinin_yalniz_adi_duzeltilebilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gecmis);
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;

        (await c.PutAsJsonAsync($"/api/krediler/{kredi.Id}", Gecmis with { Ad = "Ziraat ihtiyaç" })).EnsureSuccessStatusCode();

        Assert.Equal("Ziraat ihtiyaç", (await Kayit(c, kredi.Id)).Ad);
        var sonra = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(panel.GuncelKasa, sonra.GuncelKasa);
        Assert.Equal(panel.Kanallar.Select(k => (k.Kanal, k.Bakiye)), sonra.Kanallar.Select(k => (k.Kanal, k.Bakiye)));
    }

    [Fact]
    public async Task Gecersiz_duzeltme_once_alan_hatasi_verir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gecmis);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/krediler/{kredi.Id}", Gecmis with { OdemeGunu = 0 })).StatusCode);
    }

    [Fact]
    public async Task Gecmisi_olmayan_eski_kredi_duzeltilir_ve_silinir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gelecek);
        var once = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
        Assert.Equal(100_000m, once);

        (await c.PutAsJsonAsync($"/api/krediler/{kredi.Id}", Gelecek with { AylikOdeme = 11_000m, OdemeGunu = 20, CekimTarihi = new(2026, 10, 12), Kanal = "PERAKENDE" })).EnsureSuccessStatusCode();
        var duzelen = await Kayit(c, kredi.Id);
        Assert.Equal((11_000m, 20, "PERAKENDE"), (duzelen.AylikOdeme, duzelen.OdemeGunu, duzelen.Kanal));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/krediler/{kredi.Id}")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<KrediEntity[]>("/api/krediler"))!);
        Assert.Equal(once, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    [Fact]
    public async Task Gecmisi_olmayan_eski_kredi_gecmis_tarihe_tasinamaz()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kredi = EskiFinansTohumu.Kredi(f, Gelecek);
        var once = await Raporlar(c);

        var r = await c.PutAsJsonAsync($"/api/krediler/{kredi.Id}", Gelecek with { CekimTarihi = new(2026, 9, 1) });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("geçmiş tarihe taşınamaz", await Hata(r));
        Assert.Equal(Gelecek.CekimTarihi, (await Kayit(c, kredi.Id)).CekimTarihi);
        Assert.Equal(once, await Raporlar(c));
    }
}
