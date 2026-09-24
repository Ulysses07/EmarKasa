using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Servisler;
using Kasa.Core;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>11 · "Ayı yayınla": anlık görüntü, yayından sonra değişen rakamlar ve o aya dokunan geçmiş satırları.</summary>
public class AyYayinTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public AyYayinTests(PaketBFactory f) => _f = f;

    private async Task<HttpClient> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        return c;
    }

    private static Task<HttpResponseMessage> Yayinla(HttpClient c, int yil, int ay)
        => c.PostAsJsonAsync("/api/ay-kapanisi/yayinla", new { yil, ay });

    [Fact]
    public async Task Yayindan_sonra_degisen_rakam_ve_o_aya_dokunan_gecmis_listelenir()
    {
        var c = await HazirlaAsync();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        await IslemEkle(c, new DateOnly(2026, 8, 4), 100m);

        var y = await Yayinla(c, 2026, 8);
        Assert.Equal(HttpStatusCode.OK, y.StatusCode);
        var d0 = await y.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(d0.GetProperty("yayinlandi").GetBoolean());
        Assert.Empty(d0.GetProperty("farklar").EnumerateArray());
        Assert.Empty(d0.GetProperty("degisiklikler").EnumerateArray());

        // Yayından sonra: Ağustos'a işlem, Eylül'e işlem (Ağustos'a dokunmaz), Temmuz K.K (Ağustos'a dokunur).
        await IslemEkle(c, new DateOnly(2026, 8, 20), 250m, "PERAKENDE");
        await IslemEkle(c, new DateOnly(2026, 9, 2), 999m);
        await IslemEkle(c, new DateOnly(2026, 7, 15), 60m, "TOPTAN", "KrediKarti", "Market");

        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        var farklar = d.GetProperty("farklar").EnumerateArray()
            .ToDictionary(f => f.GetProperty("kalem").GetString()!, f => (Eski: f.GetProperty("eski").GetDecimal(), Yeni: f.GetProperty("yeni").GetDecimal()));
        Assert.Equal((0m, 250m), farklar["PERAKENDE · Cari gider"]);
        Assert.Equal((0m, -250m), farklar["PERAKENDE · Ay sonucu"]);
        Assert.Equal((0m, 60m), farklar["TOPTAN · Kredi kartı"]);
        Assert.True(farklar.ContainsKey("Kasa kapanışı"));
        Assert.False(farklar.ContainsKey("MEZAT · Gelen"));

        var ozetler = d.GetProperty("degisiklikler").EnumerateArray().Select(g => g.GetProperty("ozet").GetString()!).ToList();
        Assert.Equal(2, ozetler.Count);
        Assert.Contains(ozetler, o => o.Contains("250,00"));
        Assert.Contains(ozetler, o => o.Contains("60,00"));
        Assert.DoesNotContain(ozetler, o => o.Contains("999,00"));

        // Yeniden yayınlamak görüntüyü yeniler: fark kalmaz. Geçmişte yayın eklendi/güncellendi.
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        var d2 = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        Assert.Empty(d2.GetProperty("farklar").EnumerateArray());
        Assert.Empty(d2.GetProperty("degisiklikler").EnumerateArray());
        var yayinGecmisi = (await Oku(c, "/api/gecmis?tur=Ay%20yay%C4%B1n%C4%B1")).EnumerateArray().Select(g => g.GetProperty("ozet").GetString()!).ToList();
        Assert.Contains("Ay yayını eklendi: Ağustos 2026", yayinGecmisi);
        Assert.Contains(yayinGecmisi, o => o.StartsWith("Ay yayını güncellendi: Ağustos 2026") && o.Contains("Anlık görüntü yenilendi"));
        // Anlık görüntünün kendisi geçmiş JSON'una yazılmaz.
        Assert.DoesNotContain(await (await c.GetAsync("/api/gecmis?tur=Ay%20yay%C4%B1n%C4%B1")).Content.ReadAsStringAsync(), "anlikJson");
    }

    [Fact]
    public async Task Kasa_acilis_ve_takip_degisimi_yayini_etkiler_kanal_adi_degisimi_etkilemez()
    {
        var c = await HazirlaAsync();
        await IslemEkle(c, new DateOnly(2026, 8, 4), 100m);
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);

        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1), 5_000m);
        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        var farklar = d.GetProperty("farklar").EnumerateArray().Select(f => f.GetProperty("kalem").GetString()).ToList();
        Assert.Contains("Kasa açılışı", farklar);
        Assert.Contains("Kasa kapanışı", farklar);
        Assert.Single(d.GetProperty("degisiklikler").EnumerateArray(), g => g.GetProperty("tur").GetString() == "Ayar");

        // Kanal adı değişimi yalnız etikettir; o aya dokunan satır sayılmaz.
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        var kanallar = await Oku(c, "/api/kanallar");
        var toptan = kanallar.EnumerateArray().First(k => k.GetProperty("ad").GetString() == "TOPTAN");
        var id = toptan.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "TOPTAN", aktif = false, sira = toptan.GetProperty("sira").GetInt32(), acilisDevri = 0m })).StatusCode);
        Assert.Empty((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("degisiklikler").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "TOPTAN", aktif = true, sira = toptan.GetProperty("sira").GetInt32(), acilisDevri = 0m })).StatusCode);
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
    }

    [Fact]
    public async Task Gelecek_ay_yayinlanamaz_yayinsiz_ay_durumu()
    {
        var c = await HazirlaAsync();
        var r = await Yayinla(c, 2026, 10);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Henüz başlamamış bir ay yayınlanamaz.", await HataMetni(r));
        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=9");
        Assert.False(d.GetProperty("yayinlandi").GetBoolean());
        Assert.False(d.GetProperty("kilitlenebilir").GetBoolean());
        Assert.True((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("kilitlenebilir").GetBoolean());
        // İçinde bulunulan ay yayınlanabilir (ara rapor).
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 9)).StatusCode);
    }

    [Fact]
    public void Karsilastirma_yeni_ve_kaybolan_kanali_gosterir()
    {
        var eski = new AyAnlikGoruntusu([new KanalAylik("A", 10m, 0m, 0m, 0m, 0m, 10m)], 100m, 110m);
        var yeni = new AyAnlikGoruntusu([new KanalAylik("B", 5m, 0m, 0m, 0m, 0m, 5m)], 100m, 105m);
        var f = RaporServisi.Karsilastir(eski, yeni);
        Assert.Contains(new AyFarkiDto("A · Gelen", 10m, null), f);
        Assert.Contains(new AyFarkiDto("B · Ay sonucu", null, 5m), f);
        Assert.Contains(new AyFarkiDto("Kasa kapanışı", 110m, 105m), f);
        Assert.DoesNotContain(f, x => x.Kalem == "Kasa açılışı");
        Assert.Empty(RaporServisi.Karsilastir(eski, eski));
    }
}
