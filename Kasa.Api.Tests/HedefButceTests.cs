using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>05 · Kanal gelir hedefleri ve kalem bütçeleri: gerçekleşen, yüzde, geçen aydan kopyala.</summary>
public class HedefButceTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public HedefButceTests(PaketBFactory f) => _f = f;

    private async Task<(HttpClient C, int Mezat, int Perakende, int Kira)> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        var kalemler = await Oku(c, "/api/giderkalemleri");
        int kira;
        if (kalemler.EnumerateArray().FirstOrDefault(k => k.GetProperty("ad").GetString() == "Kira") is { ValueKind: JsonValueKind.Object } k)
            kira = k.GetProperty("id").GetInt32();
        else
            kira = (await (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Kira", aktif = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var kanallar = (await Oku(c, "/api/kanallar")).EnumerateArray().ToDictionary(x => x.GetProperty("ad").GetString()!, x => x.GetProperty("id").GetInt32());
        return (c, kanallar["MEZAT"], kanallar["PERAKENDE"], kira);
    }

    private static JsonElement Kanal(JsonElement d, string ad) => d.GetProperty("kanallar").EnumerateArray().Single(k => k.GetProperty("kanal").GetString() == ad);
    private static JsonElement Kalem(JsonElement d, string ad) => d.GetProperty("kalemler").EnumerateArray().Single(k => k.GetProperty("kalem").GetString() == ad);

    [Fact]
    public async Task Hedef_ve_butce_gerceklesenle_yuzde_verir_null_siler()
    {
        var (c, mezat, perakende, kira) = await HazirlaAsync();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 30_000m);
        (await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Müşteri", tutar = 10_000m, duzenlemeTarihi = "2026-07-01", vadeTarihi = "2026-08-10",
            kanal = "MEZAT", durum = "TahsilEdildi", islemTarihi = "2026-08-10",
        })).EnsureSuccessStatusCode();
        await IslemEkle(c, new DateOnly(2026, 8, 5), 45_000m, "Ortak", "SabitGider", "kira");   // kalem adı harf farkıyla
        (await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = "Kira", kanal = "Ortak", tutar = 40_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-07-01" })).EnsureSuccessStatusCode();

        var r = await c.PutAsJsonAsync("/api/hedef-butce", new
        {
            ay = "2026-08-15",
            kanallar = new[] { new { kanalId = mezat, tutar = (decimal?)50_000m }, new { kanalId = perakende, tutar = (decimal?)0m } },
            kalemler = new[] { new { giderKalemiId = kira, tutar = (decimal?)42_000m } },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await r.Content.ReadFromJsonAsync<JsonElement>();
        var m = Kanal(d, "MEZAT");
        Assert.Equal(50_000m, D(m, "hedef"));
        Assert.Equal(40_000m, D(m, "gerceklesen"));          // gelen + çek tahsilatı
        Assert.Equal(80.0m, D(m, "yuzde"));
        var p = Kanal(d, "PERAKENDE");
        Assert.Equal(0m, D(p, "hedef"));
        Assert.Equal(JsonValueKind.Null, p.GetProperty("yuzde").ValueKind);   // hedef 0 → yüzde yok
        Assert.Equal(JsonValueKind.Null, Kanal(d, "TOPTAN").GetProperty("hedef").ValueKind);
        var k = Kalem(d, "Kira");
        Assert.Equal(42_000m, D(k, "butce"));
        Assert.Equal(45_000m, D(k, "gerceklesen"));
        Assert.Equal(107.1m, D(k, "yuzde"));
        Assert.Equal(40_000m, D(k, "sablon"));

        // Aylık raporla aynı gerçekleşen.
        var aylik = await Oku(c, "/api/rapor/aylik?yil=2026&ay=8");
        var am = aylik.GetProperty("kanallar").EnumerateArray().Single(x => x.GetProperty("kanal").GetString() == "MEZAT");
        Assert.Equal(D(am, "gelen") + D(am, "cekGelen"), D(m, "gerceklesen"));

        // null siler; gönderilmeyen satıra dokunulmaz.
        var r2 = await c.PutAsJsonAsync("/api/hedef-butce", new { ay = "2026-08-01", kanallar = new[] { new { kanalId = perakende, tutar = (decimal?)null } } });
        var d2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, Kanal(d2, "PERAKENDE").GetProperty("hedef").ValueKind);
        Assert.Equal(50_000m, D(Kanal(d2, "MEZAT"), "hedef"));
        Assert.Equal(42_000m, D(Kalem(d2, "Kira"), "butce"));

        // Kanal adı değişse de hedef Id'ye bağlı kalır.
        var kanal = (await Oku(c, "/api/kanallar")).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == mezat);
        (await c.PutAsJsonAsync($"/api/kanallar/{mezat}", new { ad = "MEZAT YENİ", aktif = true, sira = kanal.GetProperty("sira").GetInt32(), acilisDevri = 0m })).EnsureSuccessStatusCode();
        Assert.Equal(50_000m, D(Kanal(await Oku(c, "/api/hedef-butce?yil=2026&ay=8"), "MEZAT YENİ"), "hedef"));
        (await c.PutAsJsonAsync($"/api/kanallar/{mezat}", new { ad = "MEZAT", aktif = true, sira = kanal.GetProperty("sira").GetInt32(), acilisDevri = 0m })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Gecen_aydan_kopyala_var_olani_korur_bos_ayda_400()
    {
        var (c, mezat, perakende, kira) = await HazirlaAsync();
        (await c.PutAsJsonAsync("/api/hedef-butce", new
        {
            ay = "2026-07-01",
            kanallar = new[] { new { kanalId = mezat, tutar = (decimal?)10m }, new { kanalId = perakende, tutar = (decimal?)20m } },
            kalemler = new[] { new { giderKalemiId = kira, tutar = (decimal?)30m } },
        })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/hedef-butce", new { ay = "2026-08-01", kanallar = new[] { new { kanalId = mezat, tutar = (decimal?)99m } } })).EnsureSuccessStatusCode();

        var r = await c.PostAsJsonAsync("/api/hedef-butce/kopyala", new { ay = "2026-08-01" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var s = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, s.GetProperty("kopyalanan").GetInt32());
        Assert.Equal(1, s.GetProperty("atlanan").GetInt32());
        var d = await Oku(c, "/api/hedef-butce?yil=2026&ay=8");
        Assert.Equal(99m, D(Kanal(d, "MEZAT"), "hedef"));
        Assert.Equal(20m, D(Kanal(d, "PERAKENDE"), "hedef"));
        Assert.Equal(30m, D(Kalem(d, "Kira"), "butce"));

        var bos = await c.PostAsJsonAsync("/api/hedef-butce/kopyala", new { ay = "2026-06-01" });
        Assert.Equal(HttpStatusCode.BadRequest, bos.StatusCode);
        Assert.Equal("Mayıs 2026 için hedef ya da bütçe yok; kopyalanacak bir şey yok.", await HataMetni(bos));
    }

    [Fact]
    public async Task Dogrulama_ve_yetki()
    {
        var (c, mezat, _, kira) = await HazirlaAsync();
        async Task Hata400(object govde, string mesaj)
        {
            var r = await c.PutAsJsonAsync("/api/hedef-butce", govde);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(mesaj, await HataMetni(r));
        }
        await Hata400(new { ay = "2026-08-01", kanallar = new[] { new { kanalId = mezat, tutar = (decimal?)-1m } } }, "Gelir hedefi negatif olamaz.");
        await Hata400(new { ay = "2026-08-01", kanallar = new[] { new { kanalId = mezat, tutar = (decimal?)1.555m } } }, "Gelir hedefi en fazla 2 ondalık basamak içerebilir.");
        await Hata400(new { ay = "2026-08-01", kanallar = new[] { new { kanalId = 99999, tutar = (decimal?)1m } } }, "Kanal bulunamadı (#99999).");
        await Hata400(new { ay = "2026-08-01", kalemler = new[] { new { giderKalemiId = kira, tutar = (decimal?)1m }, new { giderKalemiId = kira, tutar = (decimal?)2m } } }, "Aynı kalem birden çok kez gönderildi.");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/hedef-butce?yil=2026&ay=0")).StatusCode);

        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/hedef-butce?yil=2026&ay=8")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync("/api/hedef-butce", new { ay = "2026-08-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/hedef-butce/kopyala", new { ay = "2026-08-01" })).StatusCode);
    }
}
