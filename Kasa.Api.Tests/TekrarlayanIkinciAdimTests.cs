using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Endpoints;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>Tekrarlayan takvim — sıklık (Paket D). Aylık şablonun davranışı değişmez.</summary>
public class TekrarlayanSiklikTakvimTests
{
    private static readonly HashSet<(int, DateOnly)> KararYok = new();

    [Theory]
    [InlineData(TekrarSikligi.Aylik, 1)]
    [InlineData(TekrarSikligi.UcAylik, 3)]
    [InlineData(TekrarSikligi.AltiAylik, 6)]
    [InlineData(TekrarSikligi.Yillik, 12)]
    public void Periyot(TekrarSikligi s, int ay) => Assert.Equal(ay, TekrarlayanTakvim.Periyot(s));

    [Fact]
    public void Ay_dahil_baslangictan_itibaren_her_periyotta()
    {
        var bas = new DateOnly(2026, 7, 1);
        Assert.True(TekrarlayanTakvim.AyDahil(TekrarSikligi.UcAylik, bas, new DateOnly(2026, 7, 1)));
        Assert.False(TekrarlayanTakvim.AyDahil(TekrarSikligi.UcAylik, bas, new DateOnly(2026, 8, 1)));
        Assert.True(TekrarlayanTakvim.AyDahil(TekrarSikligi.UcAylik, bas, new DateOnly(2026, 10, 1)));
        Assert.True(TekrarlayanTakvim.AyDahil(TekrarSikligi.UcAylik, bas, new DateOnly(2027, 1, 1)));
        Assert.False(TekrarlayanTakvim.AyDahil(TekrarSikligi.UcAylik, bas, new DateOnly(2026, 4, 1)));   // başlangıçtan önce
        Assert.True(TekrarlayanTakvim.AyDahil(TekrarSikligi.Yillik, bas, new DateOnly(2027, 7, 1)));
        Assert.False(TekrarlayanTakvim.AyDahil(TekrarSikligi.Yillik, bas, new DateOnly(2027, 6, 1)));
        Assert.True(TekrarlayanTakvim.AyDahil(TekrarSikligi.Aylik, bas, new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void Uc_aylik_sablon_yalniz_donem_ayinda_bekler()
    {
        var s = new TekrarlayanSablon(1, "KDV", "Ortak", 0m, 5, true, new DateOnly(2026, 4, 1), TekrarSikligi.UcAylik, TutarDegisken: true);
        var l = TekrarlayanTakvim.Bekleyenler([s], KararYok, new DateOnly(2026, 9, 24));
        var b = Assert.Single(l);   // Temmuz (Nisan + 3); Ağustos ve Eylül dönem ayı değil
        Assert.Equal(new DateOnly(2026, 7, 1), b.Ay);
        Assert.True(b.TutarDegisken);
        Assert.Equal(TekrarSikligi.UcAylik, b.Siklik);
    }

    [Fact]
    public void Aylik_sablon_varsayilan_sikligiyla_eskisiyle_ayni_bekleyenleri_uretir()
    {
        var eski = new TekrarlayanSablon(1, "Kira", "Ortak", 100m, 5, true, new DateOnly(2026, 1, 1));
        var acik = eski with { Siklik = TekrarSikligi.Aylik };
        var bugun = new DateOnly(2026, 9, 24);
        var a = TekrarlayanTakvim.Bekleyenler([eski], KararYok, bugun);
        Assert.Equal(a, TekrarlayanTakvim.Bekleyenler([acik], KararYok, bugun));
        Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)], a.Select(b => b.Ay));
        Assert.All(a, b => { Assert.False(b.TutarDegisken); Assert.Null(b.KrediKartiId); });
    }

    [Theory]
    [InlineData("kdv", "2026-09-24", new[] { "2026-09-01" })]
    [InlineData("kdv", "2026-09-29", new[] { "2026-10-01" })]
    [InlineData("sgk", "2026-09-24", new[] { "2026-09-01" })]
    [InlineData("gecici-vergi", "2026-09-24", new[] { "2027-05-01", "2027-08-01", "2026-11-01" })]
    [InlineData("gecici-vergi", "2026-05-17", new[] { "2026-05-01", "2026-08-01", "2026-11-01" })]
    [InlineData("mtv", "2026-09-24", new[] { "2027-01-01" })]
    [InlineData("emlak-vergisi", "2026-09-24", new[] { "2026-11-01" })]
    [InlineData("emlak-vergisi", "2026-05-31", new[] { "2026-05-01" })]
    public void Hazir_sablonun_ilk_ayi_vadesi_gecmemis_ilk_donem_ayidir(string kod, string bugun, string[] beklenen)
    {
        var h = TekrarlayanHazirlar.Liste.Single(x => x.Kod == kod);
        Assert.Equal(beklenen.Select(DateOnly.Parse), h.Kalemler.Select(k => TekrarlayanHazirlar.IlkAy(k, DateOnly.Parse(bugun))));
    }

    [Fact]
    public void Emlak_vergisi_kasimda_30una_duser()
    {
        var k = TekrarlayanHazirlar.Liste.Single(x => x.Kod == "emlak-vergisi").Kalemler.Single();
        Assert.Equal(new DateOnly(2026, 11, 30), TekrarlayanTakvim.Vade(new DateOnly(2026, 11, 1), k.Gun));
        Assert.Equal(new DateOnly(2027, 5, 31), TekrarlayanTakvim.Vade(new DateOnly(2027, 5, 1), k.Gun));
        Assert.True(TekrarlayanTakvim.AyDahil(k.Siklik, new DateOnly(2026, 11, 1), new DateOnly(2027, 5, 1)));
    }
}

/// <summary>Tekrarlayan gider ikinci adım API'si (Paket D, özellik 33).</summary>
public class TekrarlayanIkinciAdimTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public TekrarlayanIkinciAdimTests(PaketDFactory factory) => _factory = factory;

    private static string Ad(string kok) => kok + " " + Guid.NewGuid().ToString("N")[..6];

    private static async Task<string> KalemEkle(HttpClient c, string ad)
    {
        await Basarili(await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true }));
        return ad;
    }

    private static async Task<JsonElement> SablonEkle(HttpClient c, object govde)
        => await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler", govde));

    private static async Task<List<JsonElement>> Bekleyenler(HttpClient c, int id)
        => (await GetJson(c, "/api/tekrarlayangiderler/bekleyen")).EnumerateArray()
            .Where(b => b.GetProperty("tekrarlayanGiderId").GetInt32() == id).ToList();

    [Fact]
    public async Task Onay_tarih_kanal_not_ve_tutari_gecersiz_kilar_normal_islem_dogrulamasiyla()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = await KalemEkle(c, Ad("Kira"));
        var s = await SablonEkle(c, new { kalem, kanal = "MEZAT", tutar = 1_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-09-01" });

        var bozuk = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01", kanal = "YOK" });
        Assert.Equal(HttpStatusCode.BadRequest, bozuk.StatusCode);
        Assert.Equal("'YOK' adında bir kanal yok.", await Hata(bozuk));
        var sifir = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01", tutar = 0m });
        Assert.Equal("Tutar sıfırdan büyük olmalı.", await Hata(sifir));

        var islem = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla",
            new { ay = "2026-09-01", tarih = "2026-09-07", tutar = 1_234.56m, kanal = " TOPTAN ", not = "  Eylül kirası  " }));
        Assert.Equal("2026-09-07", islem.Str("tarih"));
        Assert.Equal(1_234.56m, islem.Dec("tutarTl"));
        Assert.Equal("TOPTAN", islem.Str("kanal"));
        Assert.Equal("Eylül kirası", islem.Str("not"));
        Assert.Equal("SabitGider", islem.Str("tip"));
        Assert.Equal(kalem, islem.Str("cari"));
        Assert.Empty(await Bekleyenler(c, s.Id()));

        // Not verilmezse eskisi gibi "Tekrarlayan gider"; boş metin notu boşaltır.
        var s2 = await SablonEkle(c, new { kalem, kanal = "Ortak", tutar = 10m, ayinGunu = 1, aktif = true, baslangicAyi = "2026-08-01" });
        var i1 = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s2.Id()}/onayla", new { ay = "2026-08-01" }));
        Assert.Equal("Tekrarlayan gider", i1.Str("not"));
        Assert.Equal("Ortak", i1.Str("kanal"));
        var i2 = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s2.Id()}/onayla", new { ay = "2026-09-01", not = "" }));
        Assert.True(i2.Null("not"));
    }

    [Fact]
    public async Task Degisken_tutarli_sablon_sifir_tutarla_kaydedilir_onayda_tutar_ister()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = await KalemEkle(c, Ad("Vergi"));
        var s = await SablonEkle(c, new { kalem, kanal = "Ortak", tutar = 0m, ayinGunu = 20, aktif = true, baslangicAyi = "2026-09-01", tutarDegisken = true });
        Assert.True(s.Bool("tutarDegisken"));
        var b = Assert.Single(await Bekleyenler(c, s.Id()));
        Assert.True(b.Bool("tutarDegisken"));
        Assert.Equal(0m, b.Dec("tutar"));

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01" });
        Assert.Equal("Bu giderin tutarı her seferinde girilir; tutarı yazın.", await Hata(r));
        var islem = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01", tutar = 4_321m }));
        Assert.Equal(4_321m, islem.Dec("tutarTl"));

        // Değişken değilse 0 tutar eskisi gibi reddedilir; değişkende negatif reddedilir.
        var sifir = await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem, kanal = "Ortak", tutar = 0m, ayinGunu = 20, aktif = true });
        Assert.Equal("Tutar sıfırdan büyük olmalı.", await Hata(sifir));
        var negatif = await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem, kanal = "Ortak", tutar = -1m, ayinGunu = 20, aktif = true, tutarDegisken = true });
        Assert.Equal("Tutar negatif olamaz.", await Hata(negatif));
    }

    [Fact]
    public async Task Karta_bagli_sablon_onayda_karta_bagli_KK_islemi_olusturur()
    {
        var c = await _factory.EditorClientAsync();
        var kart = await Basarili(await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = Ad("Bonus"), kesimTarihi = "2026-01-15", sonOdemeTarihi = "2026-01-25", limit = 10_000m, borc = 0m }));
        var kalemYok = await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = "Olmayan Cari", kanal = "MEZAT", tutar = 99m, ayinGunu = 3, aktif = true, krediKartiId = kart.Id() });
        Assert.Contains("kayıtlı bir cari olmalı", await Hata(kalemYok));
        var kartYok = await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = "Market", kanal = "MEZAT", tutar = 99m, ayinGunu = 3, aktif = true, krediKartiId = 987_654 });
        Assert.Equal("Kredi kartı bulunamadı.", await Hata(kartYok));

        var s = await SablonEkle(c, new { kalem = "market", kanal = "MEZAT", tutar = 99.90m, ayinGunu = 3, aktif = true, baslangicAyi = "2026-09-01", krediKartiId = kart.Id() });
        Assert.Equal("Market", s.Str("kalem"));   // kayıtlı cari yazımı
        var b = Assert.Single(await Bekleyenler(c, s.Id()));
        Assert.Equal(kart.Id(), b.GetProperty("krediKartiId").GetInt32());

        var borc0 = (await GetJson(c, "/api/kredikartlari")).EnumerateArray().Single(k => k.Id() == kart.Id()).Dec("guncelBorc");
        var islem = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01" }));
        Assert.Equal("KrediKarti", islem.Str("tip"));
        Assert.Equal(kart.Id(), islem.GetProperty("krediKartiId").GetInt32());
        Assert.Equal("Market", islem.Str("cari"));
        var borc1 = (await GetJson(c, "/api/kredikartlari")).EnumerateArray().Single(k => k.Id() == kart.Id()).Dec("guncelBorc");
        Assert.Equal(99.90m, borc1 - borc0);
    }

    [Fact]
    public async Task Siklik_disindaki_ay_icin_karar_verilemez()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = await KalemEkle(c, Ad("Üç aylık"));
        var s = await SablonEkle(c, new { kalem, kanal = "Ortak", tutar = 50m, ayinGunu = 1, aktif = true, baslangicAyi = "2026-07-01", siklik = "UcAylik" });
        Assert.Equal("UcAylik", s.Str("siklik"));
        Assert.Equal(["2026-07-01"], (await Bekleyenler(c, s.Id())).Select(b => b.Str("ay")));
        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atla", new { ay = "2026-08-01" });
        Assert.Equal("Bu gider bu ay tekrarlanmıyor (sıklığına uymuyor).", await Hata(r));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem, kanal = "Ortak", tutar = 5m, ayinGunu = 1, aktif = true, siklik = 9 })).StatusCode);
    }

    [Fact]
    public async Task Bu_ay_atla_geri_alinir_ay_bekleyene_doner_ve_gecmise_yazilir()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = await KalemEkle(c, Ad("Aidat"));
        var s = await SablonEkle(c, new { kalem, kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-08-01" });
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atla", new { ay = "2026-08-01" }));
        Assert.Equal(["2026-09-01"], (await Bekleyenler(c, s.Id())).Select(b => b.Str("ay")));

        var atlanan = (await GetJson(c, "/api/tekrarlayangiderler/atlananlar")).EnumerateArray()
            .Single(a => a.GetProperty("tekrarlayanGiderId").GetInt32() == s.Id());
        Assert.Equal("2026-08-01", atlanan.Str("ay"));
        Assert.Equal("2026-08-10", atlanan.Str("vade"));

        var izleyici = await _factory.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-08-01" })).StatusCode);

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-08-15" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(["2026-08-01", "2026-09-01"], (await Bekleyenler(c, s.Id())).Select(b => b.Str("ay")));
        Assert.DoesNotContain((await GetJson(c, "/api/tekrarlayangiderler/atlananlar")).EnumerateArray(),
            a => a.GetProperty("tekrarlayanGiderId").GetInt32() == s.Id());
        Assert.Contains(await Gecmis(c, GecmisTurleri.TekrarlayanKarar),
            g => g.Str("eylem") == "Silindi" && g.Str("ozet").Contains("Atlandı"));

        // Karar yoksa 404; girilen ay geri alınamaz (işlem silinmeli).
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-08-01" })).StatusCode);
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01" }));
        var girildi = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-09-01" });
        Assert.Equal(HttpStatusCode.Conflict, girildi.StatusCode);
        Assert.Contains("girildi", await Hata(girildi));
    }

    [Fact]
    public async Task Pencere_disindaki_eski_atlama_geri_alinamaz()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = await KalemEkle(c, Ad("Eski"));
        var s = await SablonEkle(c, new { kalem, kanal = "Ortak", tutar = 1m, ayinGunu = 1, aktif = true, baslangicAyi = "2026-01-01" });
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atla", new { ay = "2026-03-01" }));
        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-03-01" });
        Assert.Equal("Yalnız bu ay ve önceki 2 ayın atlama kararları geri alınabilir.", await Hata(r));
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PostAsJsonAsync("/api/tekrarlayangiderler/987654/atlamayi-geri-al", new { ay = "2026-09-01" })).StatusCode);
    }

    [Fact]
    public async Task Hazir_sablonlar_kalem_ve_degisken_tutarli_sablon_ekler_ikinci_kez_409()
    {
        using var f = new PaketDFactory();
        var c = await f.EditorClientAsync();
        var liste = (await GetJson(c, "/api/tekrarlayangiderler/hazir")).EnumerateArray().ToList();
        Assert.Equal(["kdv", "muhtasar", "sgk", "gecici-vergi", "mtv", "emlak-vergisi"], liste.Select(h => h.Str("kod")));
        Assert.All(liste, h => Assert.False(h.Bool("eklendi")));

        var r = await c.PostAsJsonAsync("/api/tekrarlayangiderler/hazir", new { kod = "gecici-vergi" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var eklenen = (await Json(r)).EnumerateArray().ToList();
        Assert.Equal(3, eklenen.Count);
        Assert.All(eklenen, e =>
        {
            Assert.Equal("Geçici vergi", e.Str("kalem"));
            Assert.Equal("Ortak", e.Str("kanal"));
            Assert.Equal(0m, e.Dec("tutar"));
            Assert.True(e.Bool("tutarDegisken"));
            Assert.Equal("Yillik", e.Str("siklik"));
            Assert.Equal(17, e.GetProperty("ayinGunu").GetInt32());
        });
        Assert.Equal(["2027-05-01", "2027-08-01", "2026-11-01"], eklenen.Select(e => e.Str("baslangicAyi")));
        Assert.Contains((await GetJson(c, "/api/giderkalemleri")).EnumerateArray(), k => k.Str("ad") == "Geçici vergi");

        var tekrar = await c.PostAsJsonAsync("/api/tekrarlayangiderler/hazir", new { kod = "GECICI-VERGI" });
        Assert.Equal(HttpStatusCode.Conflict, tekrar.StatusCode);
        Assert.True((await GetJson(c, "/api/tekrarlayangiderler/hazir")).EnumerateArray().Single(h => h.Str("kod") == "gecici-vergi").Bool("eklendi"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/tekrarlayangiderler/hazir", new { kod = "yok" })).StatusCode);

        // Var olan kalem yeniden eklenmez; KDV bu ay 28'inde bekleyene düşer.
        await Basarili(await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "kdv", aktif = true }));
        var kdv = (await Json(await c.PostAsJsonAsync("/api/tekrarlayangiderler/hazir", new { kod = "kdv" }))).EnumerateArray().Single();
        Assert.Equal("kdv", kdv.Str("kalem"));
        Assert.Equal("2026-09-01", kdv.Str("baslangicAyi"));
        Assert.Equal("Aylik", kdv.Str("siklik"));
        Assert.Single((await GetJson(c, "/api/giderkalemleri")).EnumerateArray(), k => k.Str("ad").Equals("kdv", StringComparison.OrdinalIgnoreCase));

        var izleyici = await f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/tekrarlayangiderler/hazir", new { kod = "mtv" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/tekrarlayangiderler/hazir")).StatusCode);
    }

    [Fact]
    public async Task Cari_adi_degisince_karta_bagli_sablon_da_tasinir_cari_silinemez()
    {
        var c = await _factory.EditorClientAsync();
        var cariAd = Ad("Abonelik");
        var cari = await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = cariAd, aktif = true }));
        var kart = await Basarili(await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = Ad("Kart"), kesimTarihi = "2026-01-15", sonOdemeTarihi = "2026-01-25", limit = 1m, borc = 0m }));
        var s = await SablonEkle(c, new { kalem = cariAd, kanal = "MEZAT", tutar = 9m, ayinGunu = 3, aktif = true, krediKartiId = kart.Id() });

        var sil = await c.DeleteAsync($"/api/cariler/{cari.Id()}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
        var yeniAd = cariAd + " Yeni";
        await Basarili(await c.PutAsJsonAsync($"/api/cariler/{cari.Id()}", new { ad = yeniAd, aktif = true }));
        var sablon = (await GetJson(c, "/api/tekrarlayangiderler")).EnumerateArray().Single(x => x.Id() == s.Id());
        Assert.Equal(yeniAd, sablon.Str("kalem"));
    }
}
