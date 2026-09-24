using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket D inceleme bulguları: karta bağlı tekrarlayan gideri olan kartın silinmesi, eski istemcinin
/// PUT gövdeleri, kesim günü değişen kartın mutabakatları, boş küpür satırı ve atlanan ay listesi.
/// </summary>
public class PaketDIncelemeTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public PaketDIncelemeTests(PaketDFactory factory) => _factory = factory;

    private static string Ad(string kok) => kok + " " + Guid.NewGuid().ToString("N")[..6];

    private static async Task<JsonElement> KartEkle(HttpClient c, string kesim = "2026-01-15", decimal borc = 0m)
        => await Basarili(await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = Ad("Kart"), kesimTarihi = kesim, sonOdemeTarihi = "2026-01-25", limit = 10_000m, borc }));

    private static async Task<JsonElement> Sablon(HttpClient c, int id)
        => (await GetJson(c, "/api/tekrarlayangiderler")).EnumerateArray().Single(x => x.Id() == id);

    // ------------------------------------------------------------------ kart silme (özellik 33)

    [Fact]
    public async Task Karta_bagli_tekrarlayan_gideri_olan_kart_silinemez_sablon_bozulmaz()
    {
        var c = await _factory.EditorClientAsync();
        var cari = Ad("Yazılım");
        await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = cari, aktif = true }));
        var kart = await KartEkle(c);
        var s = await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = cari, kanal = "MEZAT", tutar = 49m, ayinGunu = 3, aktif = true, baslangicAyi = "2026-09-01", krediKartiId = kart.Id() }));

        // Kartın henüz hiç hareketi yok; yine de şablon kullandığı için silinemez.
        var sil = await c.DeleteAsync($"/api/kredikartlari/{kart.Id()}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
        Assert.Contains("tekrarlayan giderde kullanılıyor", await Hata(sil));
        Assert.Equal(kart.Id(), (await Sablon(c, s.Id())).GetProperty("krediKartiId").GetInt32());
        Assert.DoesNotContain(await Gecmis(c, GecmisTurleri.TekrarlayanGider),
            g => g.GetProperty("kayitId").GetInt32() == s.Id() && g.Str("eylem") == "Güncellendi");

        // Şablon hâlâ karta bağlı K.K işlemi üretir (sabit gider değil).
        var islem = await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/onayla", new { ay = "2026-09-01" }));
        Assert.Equal("KrediKarti", islem.Str("tip"));
        Assert.Equal(kart.Id(), islem.GetProperty("krediKartiId").GetInt32());
    }

    [Fact]
    public async Task Sablonu_silinen_kart_silinir_mutabakatlari_gecmise_yazilir()
    {
        var c = await _factory.EditorClientAsync();
        var cari = Ad("Abonelik");
        await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = cari, aktif = true }));
        var kart = await KartEkle(c, borc: 250m);
        var s = await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = cari, kanal = "MEZAT", tutar = 9m, ayinGunu = 3, aktif = true, krediKartiId = kart.Id() }));
        // Hareketsiz kartta (yalnız açılış borcu) mutabakat yapılabilir.
        var m = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat",
            new { krediKartiId = kart.Id(), kesim = "2026-09-15", ekstreTutari = 250m, farkKabul = false }));
        Assert.Equal("Mutabik", m.Str("durum"));

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kredikartlari/{kart.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/tekrarlayangiderler/{s.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kredikartlari/{kart.Id()}")).StatusCode);

        var mid = m.GetProperty("mutabakatId").GetInt32();
        Assert.Contains(await Gecmis(c, GecmisTurleri.KartMutabakati),
            g => g.Str("eylem") == "Silindi" && g.GetProperty("kayitId").GetInt32() == mid);
    }

    [Fact]
    public async Task Kartsiz_sablon_kart_silmeyi_engellemez()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = Ad("Kira");
        await Basarili(await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = kalem, aktif = true }));
        await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem, kanal = "Ortak", tutar = 100m, ayinGunu = 5, aktif = true }));
        var kart = await KartEkle(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kredikartlari/{kart.Id()}")).StatusCode);
    }

    // ------------------------------------------------------------------ eski istemci PUT gövdeleri (33, 43)

    /// <summary>Paket D'den önceki uygulamanın gönderdiği çek gövdesi: tur/konum/ciroEdilenCari yok.</summary>
    private static object EskiCekGovdesi(string durum = "Portfoyde", string? islemTarihi = null, decimal tutar = 1_000m, string not = "eski")
        => new
        {
            yon = "Alinan", cekNo = "77", banka = "Ziraat", kisi = "Eski İstemci", tutar,
            duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-10-15", kanal = "MEZAT", durum, islemTarihi, not,
        };

    [Fact]
    public async Task Eski_istemci_cek_PUTu_tur_konum_ve_ciroyu_korur()
    {
        var c = await _factory.EditorClientAsync();
        var senet = await Basarili(await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", cekNo = "77", banka = "Ziraat", kisi = "Eski İstemci", tutar = 1_000m,
            duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-10-15", kanal = "MEZAT", durum = "Portfoyde",
            tur = "Senet", konum = "BankadaTahsilde",
        }));

        var p = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{senet.Id()}", EskiCekGovdesi(tutar: 1_250m)));
        Assert.Equal(1_250m, p.Dec("tutar"));
        Assert.Equal("Senet", p.Str("tur"));
        Assert.Equal("BankadaTahsilde", p.Str("konum"));
        var kayitli = (await GetJson(c, "/api/cekler")).EnumerateArray().Single(x => x.Id() == senet.Id());
        Assert.Equal(("Senet", "BankadaTahsilde"), (kayitli.Str("tur"), kayitli.Str("konum")));

        // Ciro edilen evrakta cari de korunur; yeni istemci alanı gönderince yine yazılır.
        var ciro = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{senet.Id()}", new
        {
            yon = "Alinan", cekNo = "77", banka = "Ziraat", kisi = "Eski İstemci", tutar = 1_250m,
            duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-10-15", kanal = "MEZAT", durum = "CiroEdildi",
            islemTarihi = "2026-09-20", tur = "Senet", konum = "Elde", ciroEdilenCari = "Tedarikçi A",
        }));
        Assert.Equal("Tedarikçi A", ciro.Str("ciroEdilenCari"));
        var eski = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{senet.Id()}",
            EskiCekGovdesi("CiroEdildi", "2026-09-21", 1_250m, "not değişti")));
        Assert.Equal(("Senet", "Elde", "Tedarikçi A", "not değişti"), (eski.Str("tur"), eski.Str("konum"), eski.Str("ciroEdilenCari"), eski.Str("not")));

        var yeni = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{senet.Id()}", new
        {
            yon = "Alinan", cekNo = "77", banka = "Ziraat", kisi = "Eski İstemci", tutar = 1_250m,
            duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-10-15", kanal = "MEZAT", durum = "Portfoyde",
            tur = "Cek", konum = "Icrada", ciroEdilenCari = (string?)null,
        }));
        Assert.Equal(("Cek", "Icrada"), (yeni.Str("tur"), yeni.Str("konum")));
        Assert.True(yeni.Null("ciroEdilenCari"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PutAsJsonAsync($"/api/cekler/{senet.Id()}", EskiCekGovdesi(tutar: 0m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/cekler/987654", EskiCekGovdesi())).StatusCode);
    }

    [Fact]
    public async Task Eski_istemci_cek_PUTu_kasayi_eskisi_gibi_etkiler()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var a = await Basarili(await c.PostAsJsonAsync("/api/cekler", EskiCekGovdesi()));
        var k0 = await GuncelKasa(c);
        await Basarili(await c.PutAsJsonAsync($"/api/cekler/{a.Id()}", EskiCekGovdesi("TahsilEdildi", "2026-09-20")));
        Assert.Equal(1_000m, await GuncelKasa(c) - k0);   // vadede kasaya: tahsil günü girer
    }

    [Fact]
    public async Task Eski_istemci_tekrarlayan_PUTu_siklik_kart_ve_degiskeni_korur()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = Ad("Sigorta");
        await Basarili(await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = kalem, aktif = true }));
        var s = await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler", new
        {
            kalem, kanal = "Ortak", tutar = 0m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-07-01",
            siklik = "Yillik", tutarDegisken = true,
        }));

        // Eski uygulama: yalnız kalem/kanal/tutar/gün/aktif (başlangıç ayı da yok). Tutar 0 → değişken korunduğu için geçerli.
        var p = await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}",
            new { kalem, kanal = "Ortak", tutar = 0m, ayinGunu = 12, aktif = true }));
        Assert.Equal(("Yillik", true, "2026-07-01", 12), (p.Str("siklik"), p.Bool("tutarDegisken"), p.Str("baslangicAyi"), p.GetProperty("ayinGunu").GetInt32()));
        Assert.True(p.Null("krediKartiId"));
        // Yıllık şablon her ay bekleyene düşmez: yalnız Temmuz (aylığa dönseydi Ağustos ve Eylül de).
        Assert.Equal(["2026-07-01"], (await GetJson(c, "/api/tekrarlayangiderler/bekleyen")).EnumerateArray()
            .Where(b => b.GetProperty("tekrarlayanGiderId").GetInt32() == s.Id()).Select(b => b.Str("ay")));

        // Karta bağlı şablonun kartı da korunur (kalem cari adıdır; kartsız sayılsaydı reddedilirdi).
        var cari = Ad("Bulut");
        await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = cari, aktif = true }));
        var kart = await KartEkle(c);
        var k = await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = cari, kanal = "MEZAT", tutar = 30m, ayinGunu = 3, aktif = true, krediKartiId = kart.Id() }));
        var kp = await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{k.Id()}",
            new { kalem = cari, kanal = "MEZAT", tutar = 35m, ayinGunu = 3, aktif = true }));
        Assert.Equal((kart.Id(), 35m, "Aylik"), (kp.GetProperty("krediKartiId").GetInt32(), kp.Dec("tutar"), kp.Str("siklik")));

        // Yeni istemci alanları gönderince yazılır: krediKartiId = null kartsıza çevirir (kalem de gider kalemi olmalı).
        var kartsiz = await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{k.Id()}",
            new { kalem = cari, kanal = "MEZAT", tutar = 35m, ayinGunu = 3, aktif = true, krediKartiId = (int?)null, siklik = "Aylik", tutarDegisken = false });
        Assert.Equal(HttpStatusCode.BadRequest, kartsiz.StatusCode);
        Assert.Contains("sabit gider kalemi yok", await Hata(kartsiz));
        var aylik = await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}",
            new { kalem, kanal = "Ortak", tutar = 500m, ayinGunu = 12, aktif = true, siklik = "Aylik", tutarDegisken = false }));
        Assert.Equal(("Aylik", false), (aylik.Str("siklik"), aylik.Bool("tutarDegisken")));
    }

    // ------------------------------------------------------------------ kesim günü değişen kart (34)

    [Fact]
    public async Task Kesim_gunu_degisince_eski_mutabakatlar_listede_kalir_acilir_ve_silinir()
    {
        var c = await _factory.EditorClientAsync();
        var kart = await KartEkle(c, borc: 100m);
        var id = kart.Id();
        await PaketD.IslemEkle(c, "Market", 40m, "2026-08-20", krediKartiId: id);
        var eylul = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat",
            new { krediKartiId = id, kesim = "2026-09-15", ekstreTutari = 140m, not = "eylül", farkKabul = false }));
        await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat",
            new { krediKartiId = id, kesim = "2026-08-15", ekstreTutari = 100m, farkKabul = false }));

        // Kesim günü 15 → 20.
        await Basarili(await c.PutAsJsonAsync($"/api/kredikartlari/{id}",
            new { ad = kart.Str("ad"), kesimTarihi = "2026-01-20", sonOdemeTarihi = "2026-01-25", limit = 10_000m, borc = 100m }));

        var donemler = (await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={id}&adet=3")).EnumerateArray().ToList();
        // 20 Eyl, 20 Ağu, 20 Tem (yeni kesimler) + 15 Eyl ve 15 Ağu (eski kayıtlar), kesime göre en yeni önce.
        Assert.Equal(["2026-09-20", "2026-09-15", "2026-08-20", "2026-08-15", "2026-07-20"], donemler.Select(d => d.Str("kesim")));
        var eski = donemler[1];
        Assert.Equal("2026-08-16", eski.Str("baslangic"));   // kayıttaki dönem
        Assert.Equal(eylul.GetProperty("mutabakatId").GetInt32(), eski.GetProperty("mutabakatId").GetInt32());
        Assert.Equal("Mutabik", eski.Str("durum"));
        Assert.All(donemler.Where(d => d.Str("kesim").EndsWith("20")), d => Assert.True(d.Null("mutabakatId")));
        // Listenin aralığından eski kayıt (15 Ağu) adet=1'de görünmez; hizalı eski dönemler gibi.
        Assert.Equal(["2026-09-20", "2026-09-15"],
            (await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={id}&adet=1")).EnumerateArray().Select(d => d.Str("kesim")));

        // Eski kesimle ayrıntı açılır, güncellenir, silinir; kayıt olmayan eski kesim yine reddedilir.
        var d0 = await GetJson(c, $"/api/kartmutabakat?krediKartiId={id}&kesim=2026-09-15");
        Assert.Equal(("2026-08-16", 140m, "eylül"), (d0.Str("baslangic"), d0.Dec("hesaplananBorc"), d0.Str("not")));
        Assert.Single(d0.GetProperty("islemler").EnumerateArray());
        var g = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat",
            new { krediKartiId = id, kesim = "2026-09-15", ekstreTutari = 150m, not = "faiz", farkKabul = true }));
        Assert.Equal(("2026-08-16", "FarkKabul", 10m), (g.Str("baslangic"), g.Str("durum"), g.Dec("fark")));
        var kayitsiz = await c.GetAsync($"/api/kartmutabakat?krediKartiId={id}&kesim=2026-07-15");
        Assert.Equal(HttpStatusCode.BadRequest, kayitsiz.StatusCode);
        Assert.Equal("Kesim tarihi kartın kesim gününe (20) denk gelmiyor.", await Hata(kayitsiz));

        var mid = g.GetProperty("mutabakatId").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kartmutabakat/{mid}")).StatusCode);
        Assert.DoesNotContain((await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={id}&adet=3")).EnumerateArray(),
            d => d.Str("kesim") == "2026-09-15");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/kartmutabakat?krediKartiId={id}&kesim=2026-09-15")).StatusCode);
    }

    // ------------------------------------------------------------------ sayım satırları (35)

    [Fact]
    public async Task Bos_kupur_satiri_400_doner()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var r = await c.PostAsync("/api/kasasayimlari", JsonContent.Create(JsonDocument.Parse(
            """{"tarih":"2026-09-20","sayilanTutar":0,"satirlar":[{"tur":"Nakit","ad":"Nakit","tutar":0,"kupurler":[null]}]}""").RootElement));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Boş küpür satırı olamaz.", await Hata(r));

        var karisik = await c.PostAsync("/api/kasasayimlari", JsonContent.Create(JsonDocument.Parse(
            """{"tarih":"2026-09-20","sayilanTutar":200,"satirlar":[{"tur":"Nakit","ad":"Nakit","tutar":200,"kupurler":[{"kurus":20000,"adet":1},null]}]}""").RootElement));
        Assert.Equal("Boş küpür satırı olamaz.", await Hata(karisik));
    }

    // ------------------------------------------------------------------ atlanan aylar (33)

    [Fact]
    public async Task Atlananlar_yalniz_geri_alinabilenleri_listeler_pasif_sablon_geri_alinamaz()
    {
        var c = await _factory.EditorClientAsync();
        var kalem = Ad("Aidat");
        await Basarili(await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = kalem, aktif = true }));
        async Task<List<string>> Atlananlar(int id) => (await GetJson(c, "/api/tekrarlayangiderler/atlananlar")).EnumerateArray()
            .Where(a => a.GetProperty("tekrarlayanGiderId").GetInt32() == id).Select(a => a.Str("ay")).ToList();

        var s = await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem, kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-07-01" }));
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atla", new { ay = "2026-07-01" }));
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atla", new { ay = "2026-08-01" }));
        Assert.Equal(["2026-08-01", "2026-07-01"], await Atlananlar(s.Id()));

        // Sıklık 3 ayda bire çevrilince Ağustos artık tekrar ayı değil: listelenmez (geri alma zaten 400 verirdi).
        await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}",
            new { kalem, kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-07-01", siklik = "UcAylik" }));
        Assert.Equal(["2026-07-01"], await Atlananlar(s.Id()));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-08-01" })).StatusCode);

        // Pasif şablonun atlanan ayı listelenmez ve geri alınamaz (bekleyene dönmezdi).
        await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}",
            new { kalem, kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = false }));
        Assert.Empty(await Atlananlar(s.Id()));
        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-07-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("pasif", await Hata(r));

        // Yeniden aktif olunca ay listeye döner ve geri alınınca bekleyene düşer.
        await Basarili(await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}",
            new { kalem, kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = true }));
        Assert.Equal(["2026-07-01"], await Atlananlar(s.Id()));
        Assert.Equal(HttpStatusCode.NoContent,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id()}/atlamayi-geri-al", new { ay = "2026-07-01" })).StatusCode);
        Assert.Contains((await GetJson(c, "/api/tekrarlayangiderler/bekleyen")).EnumerateArray(),
            b => b.GetProperty("tekrarlayanGiderId").GetInt32() == s.Id() && b.Str("ay") == "2026-07-01");
    }
}
