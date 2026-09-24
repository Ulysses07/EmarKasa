using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Servisler;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>07 · Cari özeti; 24 · yazdırılabilir aylık rapor; 40 · ay paketi ve yeni CSV'ler.</summary>
public class CariOzetiVeCiktiTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public CariOzetiVeCiktiTests(PaketBFactory f) => _f = f;

    private async Task<HttpClient> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1), 1_000m);
        await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Kira", aktif = true });
        return c;
    }

    private static object Cek(string yon, string kisi, decimal tutar, string durum, string? islem, string vade = "2026-08-20", string kanal = "MEZAT", string duzenleme = "2026-07-01")
        => new { yon, kisi, tutar, duzenlemeTarihi = duzenleme, vadeTarihi = vade, kanal, durum, islemTarihi = islem };

    // ------------------------------------------------------------ 07

    [Fact]
    public async Task Cari_ozeti_islem_kart_ve_odenen_verilen_cek_ay_ay()
    {
        var c = await HazirlaAsync();
        await IslemEkle(c, new DateOnly(2026, 7, 2), 1_000m, "MEZAT", "Cari", "PORT KARGO");
        await IslemEkle(c, new DateOnly(2026, 7, 9), 500m, "TOPTAN", "Cari", "port kargo");
        await IslemEkle(c, new DateOnly(2026, 8, 1), 200m, "MEZAT", "KrediKarti", "PORT KARGO");
        await IslemEkle(c, new DateOnly(2026, 8, 1), 777m, "MEZAT", "Cari", "X");
        (await c.PostAsJsonAsync("/api/cekler", Cek("Verilen", " Port Kargo ", 3_000m, "Odendi", "2026-08-15"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", Cek("Verilen", "PORT KARGO", 9_000m, "Portfoyde", null))).EnsureSuccessStatusCode();   // ödenmedi
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "PORT KARGO", 8_000m, "TahsilEdildi", "2026-08-15"))).EnsureSuccessStatusCode(); // alınan
        (await c.PostAsJsonAsync("/api/cekler", Cek("Verilen", "PORT KARGO LTD", 4_000m, "Odendi", "2026-08-15"))).EnsureSuccessStatusCode(); // ad farklı

        var d = await Oku(c, "/api/rapor/cari-ozeti?ad=Port%20Kargo&yil=2026");
        Assert.Equal("cari", d.GetProperty("tur").GetString());
        var aylar = d.GetProperty("aylar").EnumerateArray().ToList();
        Assert.Equal(12, aylar.Count);
        Assert.Equal(1_500m, D(aylar[6], "nakit"));
        Assert.Equal(2, aylar[6].GetProperty("adet").GetInt32());
        Assert.Equal(200m, D(aylar[7], "krediKarti"));
        Assert.Equal(3_000m, D(aylar[7], "cek"));
        Assert.Equal(3_200m, D(aylar[7], "toplam"));
        Assert.Equal(4_700m, D(d, "toplam"));
        Assert.Equal(0m, D(aylar[0], "toplam"));
    }

    [Fact]
    public async Task Kalem_ozeti_sablon_ve_girilen_yan_yana()
    {
        var c = await HazirlaAsync();
        var t = (await (await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = "Kira", kanal = "Ortak", tutar = 40_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-07-01" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{t}/onayla", new { ay = "2026-07-01", tutar = 45_000m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{t}/atla", new { ay = "2026-08-01" })).EnsureSuccessStatusCode();

        var d = await Oku(c, "/api/rapor/cari-ozeti?ad=kira&yil=2026&tur=kalem");
        var aylar = d.GetProperty("aylar").EnumerateArray().ToList();
        Assert.Equal(40_000m, D(aylar[6], "sablon"));
        Assert.Equal(45_000m, D(aylar[6], "nakit"));
        Assert.Equal("Girildi", aylar[6].GetProperty("karar").GetString());
        Assert.Equal("Atlandı", aylar[7].GetProperty("karar").GetString());
        Assert.Equal(JsonValueKind.Null, aylar[5].GetProperty("sablon").ValueKind);   // başlangıç ayından önce
        Assert.Equal(40_000m * 6, D(d, "sablonToplam"));
        Assert.Equal(45_000m, D(d, "toplam"));

        foreach (var (url, mesaj) in new[]
                 {
                     ("/api/rapor/cari-ozeti?yil=2026", "Cari ya da kalem adı gerekli."),
                     ("/api/rapor/cari-ozeti?ad=X&yil=1999", "Yıl 2000 ile 2100 arasında olmalı."),
                     ("/api/rapor/cari-ozeti?ad=X&yil=2026&tur=kanal", "Tür 'cari' ya da 'kalem' olmalı."),
                 })
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(mesaj, await HataMetni(r));
        }
    }

    // ------------------------------------------------------------ 24

    [Fact]
    public async Task Yazdirilabilir_rapor_tek_sayfa_html_kacisli_turkce_ve_siki_CSP()
    {
        var c = await HazirlaAsync();
        (await c.PostAsJsonAsync("/api/kanallar", new { ad = "<script>alert(1)</script>", aktif = true, sira = 9, acilisDevri = 0m })).EnsureSuccessStatusCode();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_500.50m);
        await IslemEkle(c, new DateOnly(2026, 8, 4), 1_234_567.89m, "PERAKENDE");
        (await c.PostAsJsonAsync("/api/kredikartlari", new { ad = "Kart <b>&</b>", kesimTarihi = "2026-08-05", sonOdemeTarihi = "2026-08-15", limit = 50_000m, borc = 2_000m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "\"><img src=x onerror=alert(1)>", 7_000m, "Portfoyde", null))).EnsureSuccessStatusCode();
        // Ay sonundan sonra tahsil edilen çek, ay sonunda portföydeydi.
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "Sonra Tahsil", 3_000m, "TahsilEdildi", "2026-09-05"))).EnsureSuccessStatusCode();
        // Ay sonundan önce tahsil edilen ve ay sonundan sonra düzenlenen çekler portföyde değildi.
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "Önce Tahsil", 100m, "TahsilEdildi", "2026-08-10"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "Eylül Çeki", 200m, "Portfoyde", null, "2026-10-01", "MEZAT", "2026-09-02"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-08-31", sayilanTutar = 500m, not = "Sayım <notu>" })).EnsureSuccessStatusCode();

        var r = await c.GetAsync("/api/rapor/aylik-yazdir?yil=2026&ay=8");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/html", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("kasa-aylik-rapor-2026-08.html", r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName);
        var csp = string.Join(";", r.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("style-src 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("script-src 'unsafe-inline'", csp);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());

        var html = await r.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Ağustos 2026 · Aylık rapor", html);
        Assert.Contains("kasa.emarglobal.com", html);
        Assert.Contains("1.500,50 ₺", html);
        Assert.Contains("-1.234.567,89 ₺", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&quot;&gt;&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.Contains("Kart &lt;b&gt;&amp;&lt;/b&gt;", html);
        Assert.Contains("Sayım &lt;notu&gt;", html);
        Assert.Contains("Alınan (portföyde): 2 adet · 10.000,00 ₺", html);
        Assert.Contains("Sonra Tahsil", html);
        Assert.DoesNotContain("Önce Tahsil", html);
        Assert.DoesNotContain("Eylül Çeki", html);
        Assert.Contains("Kart borçları (31.08.2026)", html);
        Assert.Contains("@page { size: A4", html);

        // Tek betik, CSP'deki özetle birebir; satır içi olay işleyicisi yok.
        var betikBas = html.IndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
        var betik = html[betikBas..html.IndexOf("</script>", betikBas, StringComparison.Ordinal)];
        Assert.Equal(1, html.Split("<script>").Length - 1);
        var ozet = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(betik)));
        Assert.Contains($"script-src '{ozet}'", csp);
        Assert.Contains($"script-src '{ozet}'", html);   // meta CSP (dosyadan açılınca)
        Assert.DoesNotContain("onclick", html);
        Assert.Contains("window.print()", betik);

        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/rapor/aylik-yazdir?yil=2026&ay=8")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/rapor/aylik-yazdir?yil=2026&ay=13")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/rapor/aylik-yazdir?yil=2026&ay=8")).StatusCode);
        // Diğer uç noktalar genel sıkı CSP'yi korur.
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            (await c.GetAsync("/api/rapor/aylik?yil=2026&ay=8")).Headers.GetValues("Content-Security-Policy").Single());
    }

    // ------------------------------------------------------------ 40

    private static List<List<string>> Csv(byte[] b)
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, b[..3]);
        return Encoding.UTF8.GetString(b[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split(';').ToList()).ToList();
    }

    [Fact]
    public async Task Ay_paketi_tum_dosyalari_mevcut_ciktilarla_ayni_icerir()
    {
        var c = await HazirlaAsync();
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        await IslemEkle(c, new DateOnly(2026, 8, 4), 300m, "MEZAT", "Cari", "X");
        await IslemEkle(c, new DateOnly(2026, 9, 4), 55m);
        (await c.PostAsJsonAsync("/api/cekler", Cek("Verilen", "=HYPERLINK(\"http://kotu\")", 700m, "Odendi", "2026-08-12", kanal: "Ortak"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-08-31", sayilanTutar = 700m, not = "+ekstra" })).EnsureSuccessStatusCode();

        var r = await c.GetAsync("/api/disaaktar/ay-paketi.zip?yil=2026&ay=8");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/zip", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("kasa-ay-paketi-2026-08.zip", r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName);
        using var zip = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync()));
        var dosyalar = zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
        Assert.Equal(AyPaketi.DosyaKokleri.Select(k => k + "-2026-08.csv").Append("kasa-aylik-rapor-2026-08.html").Order(),
            dosyalar.Keys.Order());

        // Var olan yazıcılarla birebir aynı.
        Assert.Equal(await c.GetByteArrayAsync("/api/disaaktar/islemler.csv?baslangic=2026-08-01&bitis=2026-08-31"), dosyalar["islemler-2026-08.csv"]);
        Assert.Equal(await c.GetByteArrayAsync("/api/disaaktar/aylik.csv?yil=2026&ay=8"), dosyalar["aylik-2026-08.csv"]);
        Assert.Equal(await c.GetByteArrayAsync("/api/disaaktar/kasa-dokumu.csv?baslangic=2026-08-01&bitis=2026-08-31"), dosyalar["kasa-dokumu-2026-08.csv"]);
        var islemler = Csv(dosyalar["islemler-2026-08.csv"]);
        Assert.Equal(3, islemler.Count);   // başlık + 1 işlem + toplam (Eylül yok)

        var haftalik = Csv(dosyalar["haftalik-2026-08.csv"]);
        Assert.All(haftalik.Skip(1), s => Assert.EndsWith(".08.2026", s[0]));

        var cekler = Csv(dosyalar["cekler-2026-08.csv"]);
        Assert.Equal("\"'=HYPERLINK(\"\"http://kotu\"\")\"", cekler[1][3]);
        Assert.Equal("Ödendi", cekler[1][8]);

        var sayim = Csv(dosyalar["kasa-sayimlari-2026-08.csv"]);
        Assert.Equal("'+ekstra", sayim[1][5]);
        var gecmis = Csv(dosyalar["gecmis-2026-08.csv"]);
        Assert.Equal(new[] { "Zaman", "Rol", "Tür", "Eylem", "Özet", "Geri alındı" }, gecmis[0]);
        Assert.Empty(gecmis.Skip(1));   // test saati Eylül: Ağustos'ta yazılmış geçmiş yok

        var dokum = Csv(dosyalar["kasa-dokumu-2026-08.csv"]);
        Assert.StartsWith("Açılış (01.08.2026)", dokum[1][0]);
        Assert.StartsWith("Kapanış (31.08.2026)", dokum[^1][0]);
        Assert.Contains("Ağustos 2026 · Aylık rapor", Encoding.UTF8.GetString(dosyalar["kasa-aylik-rapor-2026-08.html"]));

        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/disaaktar/ay-paketi.zip?yil=2026&ay=8")).StatusCode);
    }

    [Fact]
    public async Task Cek_sayim_ve_gecmis_csvleri_filtreli_ve_korumali()
    {
        var c = await HazirlaAsync();
        (await c.PostAsJsonAsync("/api/cekler", Cek("Alinan", "@SUM(1)", 100m, "Portfoyde", null, "2026-08-01"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", Cek("Verilen", "Tedarikçi; A.Ş.", 200m, "Portfoyde", null, "2026-09-01"))).EnsureSuccessStatusCode();

        var r = await c.GetAsync("/api/disaaktar/cekler.csv?yon=Verilen");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("kasa-cekler-2026-09-24.csv", r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName);
        var satirlar = Encoding.UTF8.GetString((await r.Content.ReadAsByteArrayAsync())[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, satirlar.Length);   // başlık + 1 çek + 2 toplam
        Assert.Contains("\"Tedarikçi; A.Ş.\"", satirlar[1]);
        Assert.Contains("Ödenecek", satirlar[1]);
        Assert.StartsWith("Toplam alınan (0 çek)", satirlar[2]);
        Assert.StartsWith("Toplam verilen (1 çek);;;;200,00", satirlar[3]);

        var tum = Encoding.UTF8.GetString(await c.GetByteArrayAsync("/api/disaaktar/cekler.csv"));
        Assert.Contains("'@SUM(1)", tum);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/cekler.csv?yon=Yan")).StatusCode);

        (await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-09-01", sayilanTutar = 50m })).EnsureSuccessStatusCode();
        var sayim = Encoding.UTF8.GetString(await c.GetByteArrayAsync("/api/disaaktar/kasasayimlari.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("﻿Tarih;Sayılan;Defterdeki (kayıt anında);Fark;Bugünkü defter;Not;Kayıt zamanı", sayim[0]);
        Assert.StartsWith("01.09.2026;50,00;1000,00;-950,00;1000,00;;24.09.2026", sayim[1]);

        var gr = await c.GetAsync("/api/disaaktar/gecmis.csv?tur=%C3%87ek");
        Assert.Equal("kasa-gecmis-2026-09-24-cek.csv", gr.Content.Headers.ContentDisposition!.FileNameStar ?? gr.Content.Headers.ContentDisposition.FileName);
        var gecmis = Encoding.UTF8.GetString(await gr.Content.ReadAsByteArrayAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(gecmis.Skip(1), s => Assert.Contains(";Çek;", s));
        Assert.Contains(";Editör;Çek;Eklendi;", gecmis[1]);
        Assert.Contains("Tedarikçi", gecmis[1]);   // en yeni önce
        Assert.Contains("@SUM(1)", gecmis[2]);

        var izleyici = await IzleyiciAsync(_f);
        foreach (var url in new[] { "/api/disaaktar/cekler.csv", "/api/disaaktar/kasasayimlari.csv", "/api/disaaktar/gecmis.csv" })
        {
            Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync(url)).StatusCode);
        }
    }
}
