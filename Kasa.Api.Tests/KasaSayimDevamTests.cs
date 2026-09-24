using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>Kasa sayımının devamı (Paket D, özellik 35): satırlar, küpür, fark durumu, neden değişti, son sayım.</summary>
public class KasaSayimDevamTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public KasaSayimDevamTests(PaketDFactory factory) => _factory = factory;

    private async Task<HttpClient> Editor()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        return c;
    }

    private static object Satir(string tur, string ad, decimal tutar, object[]? kupurler = null)
        => new { tur, ad, tutar, kupurler };

    private static Task<HttpResponseMessage> Kaydet(HttpClient c, string tarih, decimal sayilan, object[]? satirlar = null, string? not = null)
        => c.PostAsJsonAsync("/api/kasasayimlari", new { tarih, sayilanTutar = sayilan, not, satirlar });

    [Fact]
    public async Task Satirli_sayim_kupurlerle_kaydedilir_toplam_satirlarin_toplamidir()
    {
        var c = await Editor();
        var hesap = (await GetJson(c, "/api/kasasayimlari/hesapla?tarih=2026-09-20")).Dec("hesaplananTutar");
        var nakit = Satir("Nakit", " Kasa ", 405.40m, [new { kurus = 20000, adet = 2 }, new { kurus = 500, adet = 1 }, new { kurus = 25, adet = 0 }, new { kurus = 10, adet = 4 }]);
        var s = await Basarili(await Kaydet(c, "2026-09-20", 1_655.40m,
            [nakit, Satir("Banka", "İş Bankası", 1_000m), Satir("Pos", "POS'ta bekleyen", 250m), Satir("Diger", "Yemek kartı", 0m)]));
        Assert.Equal(1_655.40m, s.Dec("sayilanTutar"));
        // Defter değeri satırlardan etkilenmez.
        Assert.Equal(hesap, s.Dec("hesaplananTutar"));
        Assert.Equal("Acik", s.Str("farkDurumu"));
        var satirlar = s.GetProperty("satirlar").EnumerateArray().ToList();
        Assert.Equal(["Kasa", "İş Bankası", "POS'ta bekleyen", "Yemek kartı"], satirlar.Select(x => x.Str("ad")));
        // Sıfır adetli küpür atılır, küpürler büyükten küçüğe.
        Assert.Equal([20000, 500, 10], satirlar[0].GetProperty("kupurler").EnumerateArray().Select(k => k.GetProperty("kurus").GetInt32()));
        Assert.True(satirlar[1].Null("kupurler"));

        var liste = (await GetJson(c, "/api/kasasayimlari")).EnumerateArray().Single(x => x.Id() == s.Id());
        Assert.Equal(4, liste.GetProperty("satirlar").GetArrayLength());

        // Eski tip tek tutarlı sayım da sürer (satır yok).
        var eski = await Basarili(await Kaydet(c, "2026-09-21", 10m));
        Assert.True(eski.Null("satirlar"));
    }

    [Theory]
    [InlineData("toplam")]
    [InlineData("kupurToplami")]
    [InlineData("kupurBankada")]
    [InlineData("gecersizKupur")]
    [InlineData("negatif")]
    [InlineData("adsiz")]
    [InlineData("fazlaSatir")]
    [InlineData("ayniKupur")]
    public async Task Satir_dogrulamalari(string durum)
    {
        var c = await Editor();
        var (sayilan, satirlar, beklenen) = durum switch
        {
            "toplam" => (100m, new[] { Satir("Nakit", "Kasa", 60m), Satir("Banka", "Ziraat", 30m) }, "Sayılan tutar satırların toplamına (90,00 ₺) eşit olmalı."),
            "kupurToplami" => (100m, new[] { Satir("Nakit", "Kasa", 100m, [new { kurus = 5000, adet = 1 }]) }, "'Kasa' tutarı küpürlerin toplamına (50,00 ₺) eşit olmalı."),
            "kupurBankada" => (50m, new[] { Satir("Banka", "Ziraat", 50m, [new { kurus = 5000, adet = 1 }]) }, "Küpür sayımı yalnız nakit satırında yapılabilir."),
            "gecersizKupur" => (1m, new[] { Satir("Nakit", "Kasa", 1m, [new { kurus = 1, adet = 100 }]) }, "Geçersiz küpür: 1 kuruş."),
            "negatif" => (0m, new[] { Satir("Nakit", "Kasa", 10m), Satir("Banka", "Eksi", -10m) }, "'Eksi' tutarı negatif olamaz."),
            "adsiz" => (10m, new[] { Satir("Nakit", "  ", 10m) }, "Her sayım satırının bir adı olmalı."),
            "fazlaSatir" => (0m, Enumerable.Range(0, 21).Select(i => Satir("Diger", $"S{i}", 0m)).ToArray(), "Sayım en fazla 20 satır olabilir."),
            _ => (10m, new[] { Satir("Nakit", "Kasa", 10m, [new { kurus = 500, adet = 1 }, new { kurus = 500, adet = 1 }]) }, "Aynı küpür iki kez yazılamaz."),
        };
        var r = await Kaydet(c, "2026-09-20", sayilan, satirlar);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(beklenen, await Hata(r));
    }

    [Fact]
    public async Task Fark_durumu_ve_aciklamasi()
    {
        var c = await Editor();
        var defter = (await GetJson(c, "/api/kasasayimlari/hesapla?tarih=2026-09-19")).Dec("hesaplananTutar");
        var farkli = await Basarili(await Kaydet(c, "2026-09-19", defter + 12.5m));
        var farksiz = await Basarili(await Kaydet(c, "2026-09-19", defter));

        var r1 = await c.PutAsJsonAsync($"/api/kasasayimlari/{farkli.Id()}/fark", new { durum = "Aciklandi", aciklama = "  " });
        Assert.Equal("Farkı açıklamak için bir açıklama yazın.", await Hata(r1));
        var a = await Basarili(await c.PutAsJsonAsync($"/api/kasasayimlari/{farkli.Id()}/fark", new { durum = "Aciklandi", aciklama = " Bozuk para sayılmadı " }));
        Assert.Equal("Aciklandi", a.Str("farkDurumu"));
        Assert.Equal("Bozuk para sayılmadı", a.Str("farkAciklamasi"));
        Assert.Equal(12.5m, a.Dec("fark"));
        var k = await Basarili(await c.PutAsJsonAsync($"/api/kasasayimlari/{farkli.Id()}/fark", new { durum = "KabulEdildi", aciklama = (string?)null }));
        Assert.Equal("KabulEdildi", k.Str("farkDurumu"));
        Assert.True(k.Null("farkAciklamasi"));

        Assert.Equal("Bu sayımda fark yok.", await Hata(await c.PutAsJsonAsync($"/api/kasasayimlari/{farksiz.Id()}/fark", new { durum = "KabulEdildi" })));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/kasasayimlari/987654/fark", new { durum = "KabulEdildi" })).StatusCode);
        var izleyici = await _factory.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync($"/api/kasasayimlari/{farkli.Id()}/fark", new { durum = "Acik" })).StatusCode);
        Assert.Contains(await Gecmis(c, GecmisTurleri.KasaSayimi), g => g.Str("eylem") == "Güncellendi" && g.Str("ozet").Contains("Fark durumu"));
    }

    [Fact]
    public async Task Neden_degisti_sayimdan_sonra_sayim_gunune_dokunan_degisiklikleri_listeler()
    {
        using var f = new PaketDFactory();
        var c = await f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var onceki = await IslemEkle(c, "A", 11m, "2026-09-05");   // sayımdan önce yazıldı: listelenmez
        var s = await Basarili(await Kaydet(c, "2026-09-15", 0m));
        var etkiler = await IslemEkle(c, "A", 40m, "2026-09-10");
        await IslemEkle(c, "B", 99m, "2026-09-20");                 // sayım gününden sonra
        await Basarili(await c.PutAsJsonAsync($"/api/islemler/{onceki}", new { tarih = "2026-09-05", cari = "A", tutarTl = 21m, kanal = "MEZAT", tip = "Cari" }));
        await Basarili(await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "K", tutar = 500m, duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-09-12",
            kanal = "MEZAT", durum = "TahsilEdildi", islemTarihi = "2026-09-12",
        }));
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-09-01", kasaAcilisDevri = 1_000m })).EnsureSuccessStatusCode();

        var n = await GetJson(c, $"/api/kasasayimlari/{s.Id()}/nedendegisti");
        var satirlar = n.GetProperty("degisiklikler").EnumerateArray().ToList();
        Assert.Equal(["İşlem", "İşlem", "Çek", "Ayar"], satirlar.Select(x => x.Str("tur")));
        Assert.Equal(["Eklendi", "Güncellendi", "Eklendi", "Güncellendi"], satirlar.Select(x => x.Str("eylem")));
        Assert.Contains("40,00", satirlar[0].Str("ozet"));
        // Değişim = bugünkü defter − kayıttaki defter: −40 − 10 + 500 + 1000.
        Assert.Equal(1_450m, n.Dec("degisim"));
        Assert.Equal(n.Dec("hesaplananTutar") + 1_450m, n.Dec("guncelHesaplanan"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/kasasayimlari/987654/nedendegisti")).StatusCode);
        _ = etkiler;
    }

    [Fact]
    public async Task Son_sayim_tarihi_ve_gecen_gun()
    {
        using var f = new PaketDFactory();
        var c = await f.EditorClientAsync();
        var bos = await GetJson(c, "/api/kasasayimlari/son");
        Assert.True(bos.Null("tarih"));
        Assert.True(bos.Null("gecenGun"));
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        await Basarili(await Kaydet(c, "2026-09-10", 1m));
        var son = await Basarili(await Kaydet(c, "2026-09-17", 1m));
        await Basarili(await Kaydet(c, "2026-09-12", 1m));   // sonra girilen ama daha eski tarihli sayım
        var izleyici = await f.IzleyiciAsync();
        var r = await GetJson(izleyici, "/api/kasasayimlari/son");
        Assert.Equal("2026-09-17", r.Str("tarih"));
        Assert.Equal(7, r.GetProperty("gecenGun").GetInt32());
        Assert.Equal(son.Id(), r.GetProperty("sayimId").GetInt32());
    }

    [Fact]
    public async Task Silinen_satirli_sayim_satirlariyla_geri_gelir()
    {
        var c = await Editor();
        var s = await Basarili(await Kaydet(c, "2026-09-18", 70m, [Satir("Nakit", "Kasa", 50m, [new { kurus = 5000, adet = 1 }]), Satir("Banka", "Ziraat", 20m)]));
        await Basarili(await c.PutAsJsonAsync($"/api/kasasayimlari/{s.Id()}/fark", new { durum = "KabulEdildi" }));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kasasayimlari/{s.Id()}")).StatusCode);
        var satir = (await Gecmis(c, GecmisTurleri.KasaSayimi)).First(g => g.GetProperty("kayitId").GetInt32() == s.Id() && g.Str("eylem") == "Silindi");
        var geri = await Basarili(await GeriAl(c, satir.Id()));
        var yeni = (await GetJson(c, "/api/kasasayimlari")).EnumerateArray().Single(x => x.Id() == geri.Id());
        Assert.Equal(["Kasa", "Ziraat"], yeni.GetProperty("satirlar").EnumerateArray().Select(x => x.Str("ad")));
        Assert.Equal("KabulEdildi", yeni.Str("farkDurumu"));
    }
}
