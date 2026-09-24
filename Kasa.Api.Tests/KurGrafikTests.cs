using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Servisler;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>04 · Kur tablosu, "TCMB'den doldur" (sahte HttpMessageHandler) ve grafik verisi.</summary>
public class KurGrafikTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public KurGrafikTests(PaketBFactory f) => _f = f;

    private async Task<HttpClient> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        lock (_f.Tcmb) _f.Tcmb.Istekler.Clear();
        _f.Tcmb.Yanit = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        return c;
    }

    [Fact]
    public async Task Kur_satiri_yazilir_okunur_bos_satir_silinir()
    {
        var c = await HazirlaAsync();
        var r = await c.PutAsJsonAsync("/api/kurlar", new { ay = "2026-08-17", tufeEndeksi = 3_567.1234m, usdTry = 41.25m, eurTry = (decimal?)null, altinGramTry = 4_300.5m });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var k = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-08-01", k.GetProperty("ay").GetString());
        var liste = await Oku(c, "/api/kurlar");
        var satir = liste.EnumerateArray().Single();
        Assert.Equal(3_567.1234m, D(satir, "tufeEndeksi"));
        Assert.Equal(JsonValueKind.Null, satir.GetProperty("eurTry").ValueKind);

        (await c.PutAsJsonAsync("/api/kurlar", new { ay = "2026-08-01" })).EnsureSuccessStatusCode();
        Assert.Empty((await Oku(c, "/api/kurlar")).EnumerateArray());

        foreach (var (govde, mesaj) in new (object, string)[]
                 {
                     (new { ay = "2026-08-01", usdTry = 0m }, "USD/TRY sıfırdan büyük olmalı."),
                     (new { ay = "2026-08-01", tufeEndeksi = 1.12345m }, "TÜFE endeksi en fazla 4 ondalık basamak içerebilir."),
                     (new { ay = "2026-08-01", altinGramTry = 20_000_000m }, "Gram altın çok büyük."),
                 })
        {
            var h = await c.PutAsJsonAsync("/api/kurlar", govde);
            Assert.Equal(HttpStatusCode.BadRequest, h.StatusCode);
            Assert.Equal(mesaj, await HataMetni(h));
        }
        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/kurlar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync("/api/kurlar", new { ay = "2026-08-01", usdTry = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-01" })).StatusCode);
    }

    [Fact]
    public async Task TCMB_is_gunlerinin_ortalamasini_doldurur_tatili_atlar_TUFE_ve_altina_dokunmaz()
    {
        var c = await HazirlaAsync();
        (await c.PutAsJsonAsync("/api/kurlar", new { ay = "2026-08-01", tufeEndeksi = 3_500m, altinGramTry = 4_000m, usdTry = 1m })).EnsureSuccessStatusCode();
        // Ağustos 2026: 21 iş günü; 3 Ağustos tatil (404), diğer günler gün numarasına göre kur.
        _f.Tcmb.Yanit = istek =>
        {
            var ad = istek.RequestUri!.Segments[^1];                 // 03082026.xml
            var gun = int.Parse(ad[..2]);
            if (!istek.RequestUri.AbsolutePath.StartsWith("/kurlar/202608/")) return new HttpResponseMessage(HttpStatusCode.BadRequest);
            if (gun == 3) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SahteTcmb.Xml(40m + gun / 100m, 45m + gun / 100m)) };
        };
        var r = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-20" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var s = await r.Content.ReadFromJsonAsync<JsonElement>();

        var isGunleri = Enumerable.Range(1, 31).Select(g => new DateOnly(2026, 8, g))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        lock (_f.Tcmb) Assert.Equal(isGunleri.Count, _f.Tcmb.Istekler.Count);
        Assert.All(_f.Tcmb.Istekler, u => Assert.StartsWith("https://www.tcmb.gov.tr/kurlar/202608/", u.ToString()));
        var alinan = isGunleri.Where(d => d.Day != 3).ToList();
        Assert.Equal(alinan.Count, s.GetProperty("gunSayisi").GetInt32());
        var usd = decimal.Round(alinan.Sum(d => 40m + d.Day / 100m) / alinan.Count, 4, MidpointRounding.AwayFromZero);
        var kur = s.GetProperty("kur");
        Assert.Equal(usd, D(kur, "usdTry"));
        Assert.Equal(usd + 5m, D(kur, "eurTry"));
        Assert.Equal(3_500m, D(kur, "tufeEndeksi"));
        Assert.Equal(4_000m, D(kur, "altinGramTry"));
        Assert.Equal("2026-08-04", s.GetProperty("ilkGun").GetString());
    }

    [Fact]
    public async Task TCMB_hatalari_anlasilir_turkce_mesaj_doner_ve_hicbir_sey_yazilmaz()
    {
        var c = await HazirlaAsync();
        // Bir gün sunucu hatası: eksik veriyle ortalama alınmaz (502).
        _f.Tcmb.Yanit = istek => istek.RequestUri!.AbsolutePath.EndsWith("05082026.xml")
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SahteTcmb.Xml(40m, 45m)) };
        var r = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Contains("TCMB hata döndü (500)", await HataMetni(r));

        _f.Tcmb.Yanit = _ => throw new HttpRequestException("ağ yok");
        var r2 = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadGateway, r2.StatusCode);
        Assert.Contains("TCMB'ye ulaşılamadı", await HataMetni(r2));

        _f.Tcmb.Yanit = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>bakım</html>") };
        var r3 = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadGateway, r3.StatusCode);
        Assert.Contains("beklenen biçimde değil", await HataMetni(r3));

        _f.Tcmb.Yanit = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var r4 = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r4.StatusCode);
        Assert.Equal("Ağustos 2026 için TCMB'de yayımlanmış kur bulunamadı.", await HataMetni(r4));

        var r5 = await c.PostAsJsonAsync("/api/kurlar/tcmb", new { ay = "2026-10-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r5.StatusCode);
        Assert.Equal("Henüz başlamamış bir ay için kur alınamaz.", await HataMetni(r5));

        Assert.Empty((await Oku(c, "/api/kurlar")).EnumerateArray());
    }

    [Fact]
    public void Is_gunleri_bugunde_biter_ve_birim_bolunur()
    {
        var g = TcmbKurServisi.IsGunleri(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7));
        Assert.Equal(new[] { 1, 2, 3, 4, 7 }, g.Select(d => d.Day));
        Assert.Equal(new Uri("https://www.tcmb.gov.tr/kurlar/202609/07092026.xml"), TcmbKurServisi.GunAdresi(new DateOnly(2026, 9, 7)));
        var k = TcmbKurServisi.Ayristir(SahteTcmb.Xml(4125m, 4800m, birim: 100));
        Assert.Equal(41.25m, k.Usd);
        Assert.Equal(48m, k.Eur);
    }

    [Fact]
    public async Task Grafik_24_ay_aylik_raporla_ayni_takip_oncesi_isaretli_kur_bos_kalir()
    {
        var c = await HazirlaAsync();
        await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Kira", aktif = true });   // varsa 409, önemsiz
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        await IslemEkle(c, new DateOnly(2026, 8, 4), 300m, "MEZAT");
        await IslemEkle(c, new DateOnly(2026, 7, 4), 50m, "Ortak", "SabitGider", "Kira");
        (await c.PutAsJsonAsync("/api/kurlar", new { ay = "2026-08-01", usdTry = 41m })).EnsureSuccessStatusCode();

        var g = await Oku(c, "/api/rapor/grafik?yil=2026&ay=9");
        var aylar = g.GetProperty("aylar").EnumerateArray().ToList();
        Assert.Equal(24, aylar.Count);
        Assert.Equal((2024, 10), (aylar[0].GetProperty("yil").GetInt32(), aylar[0].GetProperty("ay").GetInt32()));
        Assert.Equal((2026, 9), (aylar[^1].GetProperty("yil").GetInt32(), aylar[^1].GetProperty("ay").GetInt32()));
        Assert.True(aylar.Single(a => a.GetProperty("yil").GetInt32() == 2026 && a.GetProperty("ay").GetInt32() == 5).GetProperty("takipOncesi").GetBoolean());

        foreach (var ay in new[] { 7, 8, 9 })
        {
            var satir = aylar.Single(a => a.GetProperty("yil").GetInt32() == 2026 && a.GetProperty("ay").GetInt32() == ay);
            Assert.False(satir.GetProperty("takipOncesi").GetBoolean());
            var aylik = await Oku(c, $"/api/rapor/aylik?yil=2026&ay={ay}");
            foreach (var k in aylik.GetProperty("kanallar").EnumerateArray())
            {
                var gk = satir.GetProperty("kanallar").EnumerateArray().Single(x => x.GetProperty("kanal").GetString() == k.GetProperty("kanal").GetString());
                Assert.Equal(D(k, "aySonucu"), D(gk, "aySonucu"));
                Assert.Equal(D(k, "gelen") + D(k, "cekGelen"), D(gk, "gelir"));
            }
        }
        var agustos = aylar.Single(a => a.GetProperty("yil").GetInt32() == 2026 && a.GetProperty("ay").GetInt32() == 8);
        Assert.Equal(41m, D(agustos, "usdTry"));
        Assert.Equal(JsonValueKind.Null, agustos.GetProperty("tufeEndeksi").ValueKind);
        Assert.Equal(JsonValueKind.Null, aylar[^1].GetProperty("usdTry").ValueKind);
        Assert.Equal(new[] { "MEZAT", "PERAKENDE", "TOPTAN" }, g.GetProperty("kanallar").EnumerateArray().Select(k => k.GetString()));
    }
}
