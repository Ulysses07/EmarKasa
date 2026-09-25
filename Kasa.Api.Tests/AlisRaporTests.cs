using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Core;

namespace Kasa.Api.Tests;

public class AlisRaporTests
{
    private static DateOnly Bugun => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Taslak_onay_ve_duzeltme_kasayi_tekrarlamaz_belirsiz_odeme_gorunur_kalir()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var a = await Taslak(c);
        Assert.Equal(0m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        a = await Post(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Bugun, 40m));
        var bekleyen = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(-40m, bekleyen.GuncelKasa);
        Assert.Equal(-40m, bekleyen.BuAySonucu);
        Assert.Equal(40m, bekleyen.DagilimBekleyenTutar);
        Assert.All(bekleyen.Kanallar, k => Assert.Equal(0m, k.Bakiye));
        Assert.True(Assert.Single(a.Odemeler).DagilimBekliyor);
        a = await Onayla(c, a);
        var onayli = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(-40m, onayli.GuncelKasa);
        Assert.Equal(bekleyen.BuAySonucu, onayli.BuAySonucu);
        Assert.Equal(0m, onayli.DagilimBekleyenTutar);
        Assert.Equal(-24m, onayli.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
        Assert.Equal(-16m, onayli.Kanallar.Single(k => k.Kanal == "PERAKENDE").Bakiye);

        a = await Post(c, $"/api/alis/{a.Id}/iade", new AlisDurumYaz(a.Surum, "Kanal dağılımı düzeltilecek"));
        Assert.Equal(40m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.DagilimBekleyenTutar);
        var duzeltme = await c.PutAsJsonAsync($"/api/alis/{a.Id}", Yaz(a.Surum, 20m, 80m));
        duzeltme.EnsureSuccessStatusCode();
        a = (await duzeltme.Content.ReadFromJsonAsync<AlisDto>())!;
        a = await Onayla(c, a);
        var son = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(-40m, son.GuncelKasa);
        Assert.Equal(-8m, son.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
        Assert.Equal(-32m, son.Kanallar.Single(k => k.Kanal == "PERAKENDE").Bakiye);
        Assert.Single((await c.GetFromJsonAsync<JsonElement>("/api/islemler")).EnumerateArray());
        Assert.Single(a.Odemeler);
    }

    [Fact]
    public async Task Kismi_odemeler_tam_dagilimi_tamamlar_kanal_adi_degisse_de_bag_korunur()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var a = await Onayla(c, await Taslak(c));
        a = await Post(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Bugun, 33.33m));
        a = await Post(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Bugun, 66.67m));
        Assert.Equal(0m, a.Kalan);
        Assert.Equal(60m, a.Odemeler.SelectMany(o => o.Dagilimlar).Where(d => d.KanalId == 1).Sum(d => d.Tutar));
        Assert.Equal(40m, a.Odemeler.SelectMany(o => o.Dagilimlar).Where(d => d.KanalId == 2).Sum(d => d.Tutar));
        (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT YENİ"))).EnsureSuccessStatusCode();
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(-100m, panel.GuncelKasa);
        Assert.Equal(-60m, panel.Kanallar.Single(k => k.Kanal == "MEZAT YENİ").Bakiye);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync("/api/kanallar/1")).StatusCode);
        var ay = (await c.GetFromJsonAsync<AylikRapor>($"/api/rapor/aylik?yil={Bugun.Year}&ay={Bugun.Month}"))!;
        Assert.Equal(-100m, ay.Kanallar.Sum(k => k.AySonucu));
    }

    [Fact]
    public async Task Mevcut_gideri_baglamak_ikinci_gider_uretmez_ve_gider_dogrudan_degistirilemez()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih = Bugun, cari = "Tedarikçi", tutarTl = 100m, kanal = "Ortak", tip = "Cari" });
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var a = await Onayla(c, await Taslak(c));
        a = await Post(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Bugun, 100m, MevcutIslemId: id));
        Assert.Equal(-100m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        Assert.Single((await c.GetFromJsonAsync<JsonElement>("/api/islemler")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/islemler/{id}",
            new { tarih = Bugun, cari = "Tedarikçi", tutarTl = 200m, kanal = "MEZAT", tip = "Cari" })).StatusCode);
    }

    [Fact]
    public async Task Kartli_alis_ile_kart_borc_odemesi_kasaya_iki_defa_yazilmaz()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var kartId = LegacyFinanceSeed.Kart(f, new("Alış kartı", Bugun, Bugun, 1000m, 0m)).Id;
        var a = await Onayla(c, await Taslak(c));
        a = await Post(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Bugun, 100m, kartId));
        Assert.Equal(0m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        var gelecek = Bugun.AddMonths(1);
        var yol = $"/api/rapor/aylik?yil={gelecek.Year}&ay={gelecek.Month}";
        var once = (await c.GetFromJsonAsync<AylikRapor>(yol))!;
        Assert.Equal(100m, once.Kanallar.Sum(k => k.KrediKarti));
        (await c.PostAsJsonAsync("/api/kartodemeler", new KartOdemeYazDto(kartId, Bugun, 100m))).EnsureSuccessStatusCode();
        var sonra = (await c.GetFromJsonAsync<AylikRapor>(yol))!;
        Assert.Equal(once.Kanallar.Sum(k => k.AySonucu), sonra.Kanallar.Sum(k => k.AySonucu));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kredikartlari/{kartId}")).StatusCode);
    }

    private static AlisYaz Yaz(int surum = 0, decimal mezat = 60m, decimal perakende = 40m) => new(surum,
        Bugun, "Tedarikçi", null, [new("Mal", 100m, [new(1, mezat), new(2, perakende)])]);
    private static Task<AlisDto> Taslak(HttpClient c) => Post(c, "/api/alis", Yaz());
    private static async Task<AlisDto> Onayla(HttpClient c, AlisDto a)
    {
        a = await Post(c, $"/api/alis/{a.Id}/gonder", new AlisDurumYaz(a.Surum));
        return await Post(c, $"/api/alis/{a.Id}/onayla", new AlisDurumYaz(a.Surum));
    }
    private static async Task<AlisDto> Post<T>(HttpClient c, string yol, T girdi)
    {
        var r = await c.PostAsJsonAsync(yol, girdi);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<AlisDto>())!;
    }
}
