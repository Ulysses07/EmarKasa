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
    public async Task Yalniz_bitmis_ay_yayinlanir_yayinsiz_ay_durumu()
    {
        var c = await HazirlaAsync();
        var r = await Yayinla(c, 2026, 10);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Ekim 2026 henüz bitmedi; yalnız bitmiş bir ay yayınlanabilir.", await HataMetni(r));
        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=9");
        Assert.False(d.GetProperty("yayinlandi").GetBoolean());
        Assert.False(d.GetProperty("kilitlenebilir").GetBoolean());
        Assert.True((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("kilitlenebilir").GetBoolean());
        // İçinde bulunulan ay yayınlanamaz: rakamları kayıt değişmeden de değişir.
        var buAy = await Yayinla(c, 2026, 9);
        Assert.Equal(HttpStatusCode.BadRequest, buAy.StatusCode);
        Assert.Equal("Eylül 2026 henüz bitmedi; yalnız bitmiş bir ay yayınlanabilir.", await HataMetni(buAy));
        Assert.False((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=9")).GetProperty("yayinlandi").GetBoolean());
    }

    [Fact]
    public async Task Bitmis_ayin_yayini_zaman_gecince_degismez()
    {
        // Kartsız K.K (geçen ay) ve ileri tarihli kayıt: bitmiş ayda ikisi de yayın anında sayılmıştır.
        var c = await HazirlaAsync();
        await IslemEkle(c, new DateOnly(2026, 7, 20), 500m, "MEZAT", "KrediKarti", "Market");
        await IslemEkle(c, new DateOnly(2026, 8, 29), 700m);
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        try
        {
            _f.Saat.Ayarla(new DateOnly(2026, 10, 2));
            var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
            Assert.Empty(d.GetProperty("farklar").EnumerateArray());
            Assert.Empty(d.GetProperty("degisiklikler").EnumerateArray());
        }
        finally { _f.Saat.Ayarla(new DateOnly(2026, 9, 24)); }
    }

    private static Dictionary<string, (decimal? Eski, decimal? Yeni)> Farklar(JsonElement d)
        => d.GetProperty("farklar").EnumerateArray().ToDictionary(f => f.GetProperty("kalem").GetString()!,
            f => (f.GetProperty("eski").ValueKind == JsonValueKind.Null ? (decimal?)null : f.GetProperty("eski").GetDecimal(),
                  f.GetProperty("yeni").ValueKind == JsonValueKind.Null ? (decimal?)null : f.GetProperty("yeni").GetDecimal()));

    [Fact]
    public async Task Kanal_adi_degisimi_ve_yeni_kanal_yayinda_fark_sayilmaz()
    {
        var c = await HazirlaAsync();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        var mezat = (await Oku(c, "/api/kanallar")).EnumerateArray().First(k => k.GetProperty("ad").GetString() == "MEZAT");
        var id = mezat.GetProperty("id").GetInt32();
        var sira = mezat.GetProperty("sira").GetInt32();
        int? yeniKanal = null;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "MEZAT2", aktif = true, sira, acilisDevri = 0m })).StatusCode);
            // Yayından sonra eklenen (o ay hareketsiz, pay almayan) kanalın boş satırı da fark değildir.
            var r = await c.PostAsJsonAsync("/api/kanallar", new { ad = "SONRADAN", aktif = true, sira = 90, acilisDevri = 0m });
            yeniKanal = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
            Assert.Empty(d.GetProperty("farklar").EnumerateArray());
            Assert.Empty(d.GetProperty("degisiklikler").EnumerateArray());

            // Gerçek bir değişiklik bugünkü adla gösterilir.
            await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT2", 1_500m);
            var f = Farklar(await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8"));
            Assert.Equal((1_000m, 1_500m), f["MEZAT2 · Gelen"]);
            Assert.DoesNotContain(f.Keys, k => k.StartsWith("MEZAT ·", StringComparison.Ordinal));
        }
        finally
        {
            if (yeniKanal is { } y) (await c.DeleteAsync($"/api/kanallar/{y}")).EnsureSuccessStatusCode();
            _f.Temizle();
            (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "MEZAT", aktif = true, sira, acilisDevri = 0m })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Onceki_ay_duzeltmesi_yayindaki_ayin_kasasini_degistirir_ve_nedeni_listelenir()
    {
        var c = await HazirlaAsync();
        var temmuzId = await IslemEkle(c, new DateOnly(2026, 7, 10), 100m);
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);

        // Yalnız notu değişen Temmuz işlemi Ağustos'un rakamını değiştirmez: listelenmez.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-10", cari = "X", tutarTl = 100m, kanal = "MEZAT", tip = "Cari", not = "düzeltme notu" })).StatusCode);
        var d0 = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        Assert.Empty(d0.GetProperty("farklar").EnumerateArray());
        Assert.Empty(d0.GetProperty("degisiklikler").EnumerateArray());

        // Tutar düzeltmesi: Ağustos'un açılış ve kapanış kasası değişir, neden olan Temmuz satırı listelenir.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-10", cari = "X", tutarTl = 20_000m, kanal = "MEZAT", tip = "Cari" })).StatusCode);
        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        var f = Farklar(d);
        Assert.Equal((-100m, -20_000m), f["Kasa açılışı"]);
        Assert.Equal((900m, -19_000m), f["Kasa kapanışı"]);
        var g = d.GetProperty("degisiklikler").EnumerateArray().ToList();
        Assert.Equal(2, g.Count);                                   // not ve tutar düzeltmesi (en yeni önce)
        Assert.All(g, x => Assert.Equal(temmuzId, x.GetProperty("kayitId").GetInt32()));
        Assert.Contains("20.000,00", g[0].GetProperty("ozet").GetString());
    }

    [Fact]
    public async Task Rakam_degismeden_dokunulan_kayit_fark_uretmez_ama_listelenir()
    {
        var c = await HazirlaAsync();
        var id = await IslemEkle(c, new DateOnly(2026, 8, 4), 100m);
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{id}", new { tarih = "2026-08-04", cari = "X", tutarTl = 100m, kanal = "MEZAT", tip = "Cari", not = "not" })).StatusCode);
        var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
        Assert.Empty(d.GetProperty("farklar").EnumerateArray());   // uygulamada kırmızı şerit değil, nötr not
        Assert.Single(d.GetProperty("degisiklikler").EnumerateArray());
    }

    [Fact]
    public async Task Ortak_payi_degisince_kanal_sirasi_degisikligi_neden_olarak_listelenir()
    {
        var c = await HazirlaAsync();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        await GelenYaz(c, new DateOnly(2026, 8, 3), "PERAKENDE", 1_000m);
        await IslemEkle(c, new DateOnly(2026, 8, 5), 100.01m, "Ortak");
        Assert.Equal(HttpStatusCode.OK, (await Yayinla(c, 2026, 8)).StatusCode);
        var mezat = (await Oku(c, "/api/kanallar")).EnumerateArray().First(k => k.GetProperty("ad").GetString() == "MEZAT");
        var id = mezat.GetProperty("id").GetInt32();
        var sira = mezat.GetProperty("sira").GetInt32();
        try
        {
            // Yayınlanmış ama kilitsiz ay: sıra değişimi serbest, farkı ve nedeni şeritte.
            Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "MEZAT", aktif = true, sira = 99, acilisDevri = 0m })).StatusCode);
            var d = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8");
            var f = Farklar(d);
            Assert.Equal((50.01m, 50m), f["MEZAT · Ortak pay"]);
            Assert.Equal((50m, 50.01m), f["PERAKENDE · Ortak pay"]);
            Assert.False(f.ContainsKey("Kasa kapanışı"));
            var g = Assert.Single(d.GetProperty("degisiklikler").EnumerateArray());
            Assert.Equal("Kanal", g.GetProperty("tur").GetString());
        }
        finally
        {
            (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "MEZAT", aktif = true, sira, acilisDevri = 0m })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public void Karsilastirma_id_ile_eslesir_eski_goruntude_id_yoksa_adla()
    {
        var eski = new AyAnlikGoruntusu([new KanalAylik("A", 10m, 0m, 0m, 0m, 0m, 10m)], 0m, 10m, new Dictionary<string, int> { ["A"] = 1 });
        var yeni = new AyAnlikGoruntusu([new KanalAylik("B", 10m, 0m, 0m, 0m, 0m, 10m), new KanalAylik("C", 0m, 0m, 0m, 0m, 0m, 0m)], 0m, 10m,
            new Dictionary<string, int> { ["B"] = 1, ["C"] = 2 });
        Assert.Empty(RaporServisi.Karsilastir(eski, yeni));          // A → B yeniden adlandırma; C boş yeni kanal
        var degisen = yeni with { Kanallar = [new KanalAylik("B", 12m, 0m, 0m, 0m, 0m, 12m)] };
        Assert.Equal(new[] { new AyFarkiDto("B · Gelen", 10m, 12m), new AyFarkiDto("B · Ay sonucu", 10m, 12m) }, RaporServisi.Karsilastir(eski, degisen));
        // Eski (Id'siz) görüntü: adla eşlenir.
        var eskiIdsiz = eski with { KanalIdleri = null };
        Assert.Contains(new AyFarkiDto("A · Gelen", 10m, null), RaporServisi.Karsilastir(eskiIdsiz, yeni));
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
