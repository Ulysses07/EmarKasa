using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>
/// Paketlerin (A, C, D, E) birlikte çalışması: ayrı ayrı incelenmiş paketlerin birbirinin verisini doğru
/// okuduğunu kanıtlayan testler. Her test kendi uygulamasını (boş veritabanı) açar.
/// Bugün = 24 Eylül 2026 Perşembe (İstanbul); saat testte ileri/geri alınabilir.
/// </summary>
public class EntegrasyonBulgulariTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static DateOnly T(int ay, int gun) => new(2026, ay, gun);

    private static async Task<HttpClient> EditorAsync(PanelPaketATests.Fabrika f)
    {
        var c = await f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, T(8, 1), 50_000m);
        return c;
    }

    private static async Task<int> Post(HttpClient c, string yol, object govde)
        => (await Basarili(await c.PostAsJsonAsync(yol, govde))).Id();

    private static async Task<NakitTahminSonucu> Tahmin(HttpClient c, int gun)
    {
        var r = await c.GetAsync($"/api/rapor/tahmin?gun={gun}");
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<NakitTahminSonucu>(Json))!;
    }

    private static IEnumerable<(DateOnly Gun, TahminKalemi Kalem)> Kalemler(NakitTahminSonucu t)
        => t.Gunler.SelectMany(g => g.Kalemler.Select(k => (g.Tarih, k)));

    // ─────────────── A × D: nakit tahmini tekrarlayan giderin sıklığını, değişken tutarını ve kartını okur ───────────────

    [Fact]
    public async Task A_tahmini_D_sikligina_uymayan_ayda_tekrarlayan_gider_dusmez_bekleyen_listesiyle_ayni()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        foreach (var k in new[] { "Geçici vergi", "Muhasebe", "KDV", "SGK" })
            await Post(c, "/api/giderkalemleri", new { ad = k, aktif = true });
        // Yılda bir: Mayıs'ın 17'si (bu ay ve önceki iki ayda da, ufukta da vadesi yok).
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "Geçici vergi", kanal = "Ortak", tutar = 1_000m, ayinGunu = 17, aktif = true, baslangicAyi = "2026-05-01", siklik = "Yillik" });
        // 3 ayda bir: Ağustos (onay bekliyor), Kasım; Eylül/Ekim/Aralık yok.
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "Muhasebe", kanal = "Ortak", tutar = 300m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-08-01", siklik = "UcAylik" });
        // Tutarı her ay girilen, tutarı yazılmamış hazır şablon: ne çıkacağı bilinmez, tahmine girmez.
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "KDV", kanal = "Ortak", tutar = 0m, ayinGunu = 28, aktif = true, baslangicAyi = "2026-09-01", tutarDegisken = true });
        // Tutarı değişken ama tahmini yazılmış: o tutarla, "tahmini" diye girer.
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "SGK", kanal = "Ortak", tutar = 800m, ayinGunu = 26, aktif = true, baslangicAyi = "2026-09-01", tutarDegisken = true });

        var t = await Tahmin(c, 90);   // 24 Eylül → 23 Aralık
        var tekrarlayan = Kalemler(t).Where(x => x.Kalem.Tur == TahminKalemTuru.TekrarlayanGider).ToList();
        Assert.Equal(
            [(T(9, 25), T(8, 10), -300m), (T(9, 26), T(9, 26), -800m), (T(10, 26), T(10, 26), -800m), (T(11, 10), T(11, 10), -300m), (T(11, 26), T(11, 26), -800m)],
            tekrarlayan.Select(x => (x.Gun, x.Kalem.Tarih, x.Kalem.Tutar)).OrderBy(x => x.Gun).ThenBy(x => x.Tarih));
        Assert.DoesNotContain(tekrarlayan, x => x.Kalem.Aciklama.Contains("Geçici vergi") || x.Kalem.Aciklama.Contains("KDV"));
        Assert.All(tekrarlayan.Where(x => x.Kalem.Aciklama.StartsWith("SGK")), x => Assert.Contains("tahmini tutar", x.Kalem.Aciklama));

        // Onay bekleyen kalemler Panel'in bekleyen listesinin (D) tutarı olanlarıdır.
        var bekleyen = (await GetJson(c, "/api/tekrarlayangiderler/bekleyen")).EnumerateArray()
            .Where(b => b.Dec("tutar") > 0m).Select(b => (b.Str("kalem"), DateOnly.Parse(b.Str("vade")))).ToList();
        Assert.Equal(bekleyen, tekrarlayan.Where(x => x.Kalem.Aciklama.Contains("onay bekliyor"))
            .Select(x => (x.Kalem.Aciklama.Split(" · ")[0], x.Kalem.Tarih)));
    }

    [Fact]
    public async Task A_tahmini_D_karta_bagli_tekrarlayan_gideri_kasadan_kartin_odeme_gunu_duser()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        // Kesim 10 / son ödeme 20.
        var kart = await Post(c, "/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-07-10", sonOdemeTarihi = "2026-07-20", limit = 50_000m, borc = 0m });
        await Post(c, "/api/cariler", new { ad = "Yazılım AŞ", aktif = true });
        await Post(c, "/api/cariler", new { ad = "Bulut AŞ", aktif = true });
        // Her ayın 28'i 500 ₺ (Eylül'ünkü ileride) ve her ayın 5'i 200 ₺ (Eylül'ünkü onay bekliyor), ikisi de karta bağlı.
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "Yazılım AŞ", kanal = "Ortak", tutar = 500m, ayinGunu = 28, aktif = true, baslangicAyi = "2026-09-01", krediKartiId = kart });
        var bulut = await Post(c, "/api/tekrarlayangiderler", new { kalem = "Bulut AŞ", kanal = "Ortak", tutar = 200m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-09-01", krediKartiId = kart });

        var t = await Tahmin(c, 60);   // 24 Eylül → 23 Kasım
        var hepsi = Kalemler(t).ToList();
        // Kasadan vadede düşmez (karta bağlı harcama kasayı kartın ödendiği gün etkiler).
        Assert.DoesNotContain(hepsi, x => x.Kalem.Tur == TahminKalemTuru.TekrarlayanGider);
        Assert.Empty(t.Gunler.Single(g => g.Tarih == T(9, 28)).Kalemler);
        // Kartın ekstre ödemelerine girer: 5 Eylül (onay bekleyen) 10 Eylül ekstresinde, son ödemesi 20 Eylül geçti
        // (gecikmiş, yarına); 28 Eylül + 5 Ekim → 20 Ekim; 28 Ekim + 5 Kasım → 20 Kasım.
        Assert.Equal(
            [(T(9, 25), T(9, 20), -200m), (T(10, 20), T(10, 20), -700m), (T(11, 20), T(11, 20), -700m)],
            hepsi.Where(x => x.Kalem.Tur == TahminKalemTuru.KartOdemesi).Select(x => (x.Gun, x.Kalem.Tarih, x.Kalem.Tutar)));
        Assert.True(hepsi.Single(x => x.Gun == T(9, 25)).Kalem.Gecikmis);

        // Bekleyen ay vadesiyle onaylanınca (karta K.K işlemi olur) tahmin değişmez: tahmin onu zaten öyle saymıştı.
        await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{bulut}/onayla", new { ay = "2026-09-01" }));
        var sonra = await Tahmin(c, 60);
        Assert.Equal(t.Gunler.Select(g => (g.Tarih, g.Kasa)), sonra.Gunler.Select(g => (g.Tarih, g.Kasa)));
    }

    // ─────────────── A × C: iki eksik gelen ucu aynı kanal kuralını kullanır ───────────────

    private record EksikA(DateOnly DonemStart, DateOnly DonemEnd, List<string> Kanallar);
    private record EksikC(DateOnly DonemStart, DateOnly DonemEnd, string Kanal);

    private static async Task<(HashSet<(DateOnly, string)> A, HashSet<(DateOnly, string)> C)> EksikUclari(HttpClient c, DateOnly bugun)
    {
        var a = (await c.GetFromJsonAsync<List<EksikA>>("/api/gelenler/eksik", Json))!;
        var cc = (await c.GetFromJsonAsync<List<EksikC>>("/api/gelenler/eksik-liste", Json))!;
        var enErken = bugun.AddDays(-PanelEndpoints.EksikGelenGeriyeGun);
        return (a.SelectMany(e => e.Kanallar.Select(k => (e.DonemStart, k))).ToHashSet(),
                cc.Where(e => e.DonemEnd >= enErken).Select(e => (e.DonemStart, e.Kanal)).ToHashSet());
    }

    [Fact]
    public async Task A_ve_C_eksik_gelen_uclari_ayni_hafta_icin_ayni_kanallari_verir()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        // Bugün eklenen kanal geçmiş haftalar için eksik sayılmaz.
        await Post(c, "/api/kanallar", new { ad = "ONLINE", aktif = true, sira = 9, acilisDevri = 0m });
        foreach (var k in new[] { "MEZAT", "PERAKENDE", "TOPTAN" })
            (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-14", kanal = k, tutarTl = 100m })).EnsureSuccessStatusCode();

        var (a, cc) = await EksikUclari(c, T(9, 24));
        Assert.Empty(a);
        Assert.Equal(cc, a);

        // MEZAT'ın Eylül başından hareketi var: 7–13 Eylül haftası onun için iki uçta da eksik.
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "MEZAT", tutarTl = 50m })).EnsureSuccessStatusCode();
        (a, cc) = await EksikUclari(c, T(9, 24));
        Assert.Equal([(T(9, 7), "MEZAT")], a);
        Assert.Equal(cc, a);

        // Pazartesi (28 Eylül): 21–27 Eylül bitti; ONLINE artık o hafta için de eksik.
        f.Saat.Ayarla(T(9, 28));
        (a, cc) = await EksikUclari(c, T(9, 28));
        Assert.Contains((T(9, 21), "ONLINE"), a);
        Assert.DoesNotContain((T(9, 14), "ONLINE"), a);
        Assert.Equal(cc, a);
    }

    // ─────────────── C × D: eksik liste, D'nin "önceki haline döndür" satırını okur ───────────────

    [Fact]
    public async Task C_eksik_listesi_D_ile_geri_alinan_pasif_yapmayi_okur()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        f.Saat.Ayarla(T(9, 1));
        var sube = await Post(c, "/api/kanallar", new { ad = "SUBE", aktif = true, sira = 9, acilisDevri = 0m });
        f.Saat.Ayarla(T(9, 3));
        (await c.PutAsJsonAsync($"/api/kanallar/{sube}", new { ad = "SUBE", aktif = false, sira = 9, acilisDevri = 0m })).EnsureSuccessStatusCode();
        f.Saat.Ayarla(T(9, 4));
        var pasif = (await Gecmis(c, GecmisTurleri.Kanal)).First(s => s.GetProperty("kayitId").GetInt32() == sube && s.Str("eylem") == Eylemler.Guncellendi);
        await Basarili(await GeriAl(c, pasif.Id()));
        f.Saat.Ayarla(T(9, 24));

        var l = (await c.GetFromJsonAsync<List<EksikC>>("/api/gelenler/eksik-liste", Json))!;
        Assert.Equal([T(9, 14), T(9, 7), T(9, 1)], l.Where(e => e.Kanal == "SUBE").Select(e => e.DonemStart));
        var a = (await c.GetFromJsonAsync<List<EksikA>>("/api/gelenler/eksik", Json))!;
        Assert.Equal([T(9, 7), T(9, 14)], a.Where(e => e.Kanallar.Contains("SUBE")).Select(e => e.DonemStart));
    }

    // ─────────────── C × D × E: silmeyi geri almak Id bağlarını korur ───────────────

    [Fact]
    public async Task C_silme_geri_al_eski_Id_ile_doner_D_mutabakat_E_soru_ve_D_karar_baglari_kopmaz()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        var kart = await Post(c, "/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-07-10", sonOdemeTarihi = "2026-07-20", limit = 50_000m, borc = 0m });
        var islem = await IslemEkle(c, "Market", 300m, "2026-09-05", krediKartiId: kart);
        await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat", new
        {
            krediKartiId = kart, kesim = "2026-09-10", ekstreTutari = 300m, tikliIslemIdleri = new[] { islem }, farkKabul = false,
        }));
        var soru = await Post(c, "/api/sorular", new { hedefTur = "Islem", hedefId = islem, metin = "Bu harcama neyin?" });
        // Tekrarlayan gider onayıyla oluşan işlem (kararı işleme bağlı).
        await Post(c, "/api/giderkalemleri", new { ad = "Kira", aktif = true });
        var kira = await Post(c, "/api/tekrarlayangiderler", new { kalem = "Kira", kanal = "Ortak", tutar = 9_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-09-01" });
        var kiraIslem = (await Basarili(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{kira}/onayla", new { ay = "2026-09-01" }))).Id();

        async Task<int> SilVeGeriAl(int id)
        {
            (await c.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();
            var satir = await GetJson(c, $"/api/gecmis/son-silme?tur={Uri.EscapeDataString(GecmisTurleri.Islem)}&kayitId={id}");
            return (await Basarili(await GeriAl(c, satir.Id()))).Id();
        }
        Assert.Equal(islem, await SilVeGeriAl(islem));
        Assert.Equal(kiraIslem, await SilVeGeriAl(kiraIslem));

        var m = await GetJson(c, $"/api/kartmutabakat?krediKartiId={kart}&kesim=2026-09-10");
        Assert.True(m.GetProperty("islemler").EnumerateArray().Single(i => i.Id() == islem).Bool("tikli"));
        Assert.Equal(0m, m.Dec("tiksizToplam"));
        var sorular = (await GetJson(c, $"/api/sorular?hedefTur=Islem&hedefId={islem}")).EnumerateArray().ToList();
        Assert.Equal([soru], sorular.Select(s => s.Id()));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(kiraIslem, db.TekrarlayanGirisler.AsNoTracking().Single(g => g.TekrarlayanGiderId == kira).IslemId);

        // Geçmiş: kaydın bütün satırları aynı kayıt numarasında.
        var satirlar = (await Gecmis(c, GecmisTurleri.Islem)).Where(s => s.GetProperty("kayitId").ValueKind == JsonValueKind.Number
            && s.GetProperty("kayitId").GetInt32() == islem).Select(s => s.Str("eylem")).ToList();
        Assert.Equal([Eylemler.GeriAlindi, Eylemler.Silindi, Eylemler.Eklendi], satirlar);
    }

    // ─────────────── D (E ile açığa çıkan): ayar geri alma yanıtı gizli alan döndürmez ───────────────

    [Fact]
    public async Task D_ayar_geri_alma_yaniti_izleyici_sifre_ozetini_dondurmez()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle-gizli" })).EnsureSuccessStatusCode();
        await KasaWebFactory.TakipBaslangiciAyarla(c, T(8, 1), 60_000m);
        var satir = (await Gecmis(c, GecmisTurleri.Ayar)).First(s => s.Str("eylem") == Eylemler.Guncellendi);

        var r = await GeriAl(c, satir.Id());
        var govde = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, govde);
        Assert.DoesNotContain("hash", govde, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("surum", govde, StringComparison.OrdinalIgnoreCase);
        // GET /api/ayarlar ile aynı biçim.
        Assert.Equal(await c.GetStringAsync("/api/ayarlar"), govde);
        Assert.Equal(50_000m, (await GetJson(c, "/api/ayarlar")).Dec("kasaAcilisDevri"));
    }

    // ─────────────── A × E: "defter en son güncellendi" yalnız defter değişikliklerini sayar ───────────────

    private record OzetYanit(int SonId, DateTime? SonZamanUtc, int Toplam, int GecmiseDonuk);

    [Fact]
    public async Task A_gecmis_ozeti_E_soru_kullanici_ve_guvenlik_satirlarini_saymaz()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        f.Saat.Ayarla(T(9, 20));
        await IslemEkle(c, "Market", 100m, "2026-09-18");
        var once = (await c.GetFromJsonAsync<OzetYanit>("/api/gecmis/ozet", Json))!;
        Assert.Equal(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), once.SonZamanUtc);

        f.Saat.Ayarla(T(9, 24));
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle-ozet" })).EnsureSuccessStatusCode();
        var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle-ozet" })).EnsureSuccessStatusCode();
        await Post(izleyici, "/api/sorular", new { hedefTur = "Genel", metin = "Kasa neden eksi?" });
        await PaketEYardimci.KullaniciEkleAsync(c, "Ayşe Ortak", "ayse", Roller.Izleyici, "ayse-sifre-1");
        (await c.PutAsJsonAsync("/api/guvenlik/ayar", new { editorOturumGun = 14 })).EnsureSuccessStatusCode();
        Assert.True((await Gecmis(c)).Count(s => s.Id() > once.SonId) >= 4);   // satırlar geçmişte yine var

        var o = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={once.SonId}", Json))!;
        Assert.Equal((once.SonId, once.SonZamanUtc, 0), (o.SonId, o.SonZamanUtc, o.Toplam));

        // Defter değişikliği yine sayılır ve zamanı günceller.
        await IslemEkle(c, "Market", 50m, "2026-09-24");
        o = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={once.SonId}", Json))!;
        Assert.Equal((1, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc)), (o.Toplam, o.SonZamanUtc!.Value));
    }

    // ─────────────── D × E: sistem/risk kartı D'nin "İcrada" konumunu takip sayar ───────────────

    [Fact]
    public async Task E_risk_karti_D_icrada_konumundaki_karsiliksiz_ceki_takipte_sayar()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        var cek = await Basarili(await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", cekNo = "K-1", banka = "Ziraat", kisi = "Karşılıksız Ltd", tutar = 4_000m, duzenlemeTarihi = "2026-08-01",
            vadeTarihi = "2026-09-01", kanal = "MEZAT", durum = "Karsiliksiz", islemTarihi = (string?)null, not = (string?)null,
        }));

        async Task<JsonElement> CekMaddesi()
            => (await GetJson(c, "/api/sistem/risk")).GetProperty("maddeler").EnumerateArray().Single(m => m.Str("konu") == "Cek");
        Assert.Equal("Kirmizi", (await CekMaddesi()).Str("seviye"));

        var govde = JsonSerializer.Deserialize<Dictionary<string, object?>>(cek.GetRawText())!;
        govde["konum"] = "Icrada";
        await Basarili(await c.PutAsJsonAsync($"/api/cekler/{cek.Id()}", govde));
        var m = await CekMaddesi();
        Assert.Equal("Sari", m.Str("seviye"));
        Assert.Contains("takipte", m.Str("baslik"));
    }

    // ─────────────── A × C: kaydetmeden önceki "eski tarih" uyarısı ile geçmişe dönük işareti aynı kural ───────────────

    [Theory]
    [InlineData("2026-08-20", "Cari", false, true)]       // kapanmış Ağustos (35 gün): eski "45 gün" kuralı uyarmıyordu
    [InlineData("2026-09-01", "Cari", false, false)]
    [InlineData("2026-08-10", "KrediKarti", false, false)] // geçen ayın K.K'sı bu ayı etkiler (45 günden eski olsa da)
    [InlineData("2026-07-31", "KrediKarti", false, true)]  // iki ay önceki K.K kapanmış Ağustos'u değiştirir
    [InlineData("2026-08-05", "Cari", true, false)]        // karta bağlı: K.K kuralı
    [InlineData("2026-07-20", "Cari", true, true)]
    public async Task C_eski_tarih_uyarisi_A_gecmise_donuk_isaretiyle_ayni(string tarih, string tip, bool kartli, bool beklenen)
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        int? kart = kartli
            ? await Post(c, "/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-07-10", sonOdemeTarihi = "2026-07-20", limit = 0m, borc = 0m })
            : null;
        var govde = new { tarih, cari = "Market", tutarTl = 120m, kanal = "MEZAT", tip, krediKartiId = kart };

        var uyarilar = (await PostJson(c, "/api/islemler/uyarilar", govde)).EnumerateArray().Select(u => u.Str("kod")).ToList();
        await Post(c, "/api/islemler", govde);
        var son = (await GetJson(c, "/api/gecmis?limit=1")).EnumerateArray().Single();

        Assert.Equal(beklenen, son.Bool("gecmiseDonuk"));
        Assert.Equal(beklenen, uyarilar.Contains(IslemUyariKodlari.EskiTarih));
    }

    [Fact]
    public async Task C_eski_tarih_uyarisi_duzenlemede_de_A_kuraliyla_yalniz_not_degisirse_uyarmaz()
    {
        using var f = new PanelPaketATests.Fabrika();
        var c = await EditorAsync(f);
        var id = await IslemEkle(c, "Market", 75m, "2026-07-01");
        object Duzenleme(string tarih, decimal tutar, string? not)
            => new { tarih, cari = "Market", tutarTl = tutar, kanal = "MEZAT", tip = "Cari", not, haricId = id };
        async Task<bool> Uyarir(object g)
            => (await PostJson(c, "/api/islemler/uyarilar", g)).EnumerateArray().Any(u => u.Str("kod") == IslemUyariKodlari.EskiTarih);

        Assert.False(await Uyarir(Duzenleme("2026-07-01", 75m, "yalnız not")));   // rakam değişmez, işaretlenmez
        Assert.True(await Uyarir(Duzenleme("2026-07-01", 80m, null)));
        Assert.True(await Uyarir(Duzenleme("2026-09-24", 75m, null)));            // eski hali Temmuz'u değiştirir
    }

    private static async Task<JsonElement> PostJson(HttpClient c, string yol, object govde)
        => await Basarili(await c.PostAsJsonAsync(yol, govde));
}

/// <summary>D × E: gece yedek doğrulaması D ve E'nin tablolarını da sayar; eski sürümden yedeği bozuk saymaz.</summary>
public class EntegrasyonYedekDogrulamaTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-entegrasyon-yedek-" + Guid.NewGuid().ToString("N"));

    public EntegrasyonYedekDogrulamaTests() => Directory.CreateDirectory(_klasor);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_klasor, recursive: true); } catch (IOException) { }
    }

    private KasaDbContext CanliDb()
    {
        var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_klasor, "canli.db")};Pooling=False").Options);
        db.Database.EnsureCreated();
        var kart = new KrediKartiEntity { Ad = "Bonus", KesimTarihi = new DateOnly(2026, 7, 10), SonOdemeTarihi = new DateOnly(2026, 7, 20) };
        db.KrediKartlari.Add(kart);
        db.SaveChanges();
        db.KartMutabakatlari.Add(new KartMutabakatEntity { KrediKartiId = kart.Id, DonemBaslangic = new DateOnly(2026, 8, 11), DonemBitis = new DateOnly(2026, 9, 10), EkstreTutari = 100m });
        db.Kullanicilar.Add(new KullaniciEntity { AdSoyad = "Ayşe", KullaniciAdi = "ayse", Rol = Roller.Izleyici });
        db.Sorular.Add(new SoruEntity { Metin = "?" });
        db.SaveChanges();
        return db;
    }

    private static void YedekteCalistir(string dosya, string sql)
    {
        using var b = new SqliteConnection($"Data Source={dosya};Pooling=False");
        b.Open();
        using var k = b.CreateCommand();
        k.CommandText = sql;
        k.ExecuteNonQuery();
    }

    [Fact]
    public void Yedek_dogrulamasi_D_mutabakat_ve_E_kullanici_kaybini_gorur()
    {
        using var db = CanliDb();
        Assert.Contains("KartMutabakatlari", YedekDogrulayici.Tablolar(db));
        Assert.Contains("Kullanicilar", YedekDogrulayici.Tablolar(db));
        Assert.Contains("Sorular", YedekDogrulayici.Tablolar(db));
        Assert.DoesNotContain("Degisiklikler", YedekDogrulayici.Tablolar(db));
        Assert.DoesNotContain("GirisKayitlari", YedekDogrulayici.Tablolar(db));

        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);
        Assert.True(YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow).Basarili);

        YedekteCalistir(dosya, "DELETE FROM KartMutabakatlari; DELETE FROM Kullanicilar;");
        var sonuc = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.False(sonuc.Basarili);
        Assert.Contains("KartMutabakatlari: yedekte 0, canlıda 1", sonuc.Mesaj);
        Assert.Contains("Kullanicilar: yedekte 0, canlıda 1", sonuc.Mesaj);
    }

    [Fact]
    public void Surum_yukseltmeden_onceki_yedekte_yeni_tablo_yoksa_basarili_temel_tablo_yoksa_basarisiz()
    {
        using var db = CanliDb();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);
        YedekteCalistir(dosya, "DROP TABLE Sorular; DROP TABLE KartMutabakatlari;");
        var eski = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.True(eski.Basarili, eski.Mesaj);
        Assert.Contains("KartMutabakatlari", eski.Mesaj);

        YedekteCalistir(dosya, "DROP TABLE Cariler;");
        var eksik = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.False(eksik.Basarili);
        Assert.Contains("Cariler tablosu yedekte yok", eksik.Mesaj);
    }
}
