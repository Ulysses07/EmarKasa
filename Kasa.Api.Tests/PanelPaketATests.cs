using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket A uçları: nakit tahmini (/api/rapor/tahmin), eksik gelen (/api/gelenler/eksik), geçmiş özeti
/// (/api/gecmis/ozet) ve geçmişe dönük işaret. Her test kendi uygulamasını (boş veritabanı) açar.
/// Bugün = 24 Eylül 2026 Perşembe (İstanbul); saat testte ileri alınabilir.
/// </summary>
public class PanelPaketATests
{
    public sealed class AyarliSaat : TimeProvider
    {
        private DateTimeOffset _simdi = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        public void Ayarla(DateOnly gun) => _simdi = new DateTimeOffset(gun.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _simdi;
    }

    public sealed class Fabrika : KasaWebFactory
    {
        public AyarliSaat Saat { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Saat)));
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateOnly Bugun = new(2026, 9, 24);

    private record PanelYanit(decimal GuncelKasa);
    private record IdYanit(int Id);
    private record GecmisSatiri(int Id, string Tur, int? KayitId, string Eylem, string Ozet, bool GecmiseDonuk);

    private static async Task<HttpClient> EditorAsync(Fabrika f, DateOnly? takip = null, decimal kasaAcilis = 50_000m)
    {
        var c = await f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, takip ?? new DateOnly(2026, 8, 1), kasaAcilis);
        return c;
    }

    private static async Task<HttpClient> IzleyiciAsync(Fabrika f, HttpClient editor)
    {
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle-paket-a" })).EnsureSuccessStatusCode();
        var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle-paket-a" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    private static async Task<int> Post(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"{yol}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<IdYanit>(Json))!.Id;
    }

    private static Task<int> IslemEkle(HttpClient c, string tarih, decimal tutar, string tip = "Cari", int? kart = null, string cari = "Market")
        => Post(c, "/api/islemler", new { tarih, cari, tutarTl = tutar, kanal = "MEZAT", tip, krediKartiId = kart });

    private static Task<int> CekEkle(HttpClient c, string yon, decimal tutar, string vade, string durum = "Portfoyde",
        string? islemTarihi = null, string kisi = "Ahmet")
        => Post(c, "/api/cekler", new
        {
            yon, cekNo = (string?)null, banka = (string?)null, kisi, tutar, duzenlemeTarihi = "2026-08-01",
            vadeTarihi = vade, kanal = "MEZAT", durum, islemTarihi, not = (string?)null,
        });

    private static async Task GelenYaz(HttpClient c, string donem, string kanal, decimal tutar)
        => (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = donem, kanal, tutarTl = tutar })).EnsureSuccessStatusCode();

    private static async Task<NakitTahminSonucu> Tahmin(HttpClient c, string sorgu = "")
    {
        var r = await c.GetAsync("/api/rapor/tahmin" + sorgu);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<NakitTahminSonucu>(Json))!;
    }

    private static async Task<decimal> GuncelKasa(HttpClient c)
        => (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel", Json))!.GuncelKasa;

    private static TahminGunu Gun(NakitTahminSonucu t, DateOnly tarih) => t.Gunler.Single(g => g.Tarih == tarih);

    private static DateOnly T(int ay, int gun) => new(2026, ay, gun);

    // ─────────────────────────── yetki ───────────────────────────

    [Fact]
    public async Task Yeni_uclar_oturumsuz_401_izleyici_okuyabilir()
    {
        using var f = new Fabrika();
        var anonim = f.CreateClient();
        foreach (var yol in new[] { "/api/rapor/tahmin", "/api/gelenler/eksik", "/api/gecmis/ozet" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync(yol)).StatusCode);

        var editor = await EditorAsync(f);
        var izleyici = await IzleyiciAsync(f, editor);
        foreach (var yol in new[] { "/api/rapor/tahmin?gun=90", "/api/gelenler/eksik", "/api/gecmis/ozet?sonId=0" })
            Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync(yol)).StatusCode);
    }

    // ─────────────────────────── nakit tahmini ───────────────────────────

    [Fact]
    public async Task Tahmin_bos_defterde_tek_duz_cizgi_ve_varsayilan_30_gun()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f, kasaAcilis: 12_345.67m);
        var t = await Tahmin(c);
        Assert.Equal(Bugun, t.Bugun);
        Assert.Equal(30, t.Gun);
        Assert.Equal(31, t.Gunler.Count);
        Assert.All(t.Gunler, g => Assert.Equal(12_345.67m, g.Kasa));
        Assert.Equal(Bugun, t.EnDusukTarih);
        Assert.Equal(12_345.67m, t.SonKasa);
        Assert.Empty(t.HaricKalemler);
        Assert.Equal(367, (await Tahmin(c, "?gun=366")).Gunler.Count);
    }

    [Fact]
    public async Task Tahmin_sifirinci_gun_panel_kasasi_kalemler_dogru_gunde_ve_isaretle()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);

        // Bugünkü kasayı oluşturan geçmiş kayıtlar (tahmine kalem olarak girmez).
        await GelenYaz(c, "2026-09-21", "MEZAT", 10_000m);
        await IslemEkle(c, "2026-09-10", 2_000m);
        await CekEkle(c, "Alinan", 900m, "2026-09-15", "TahsilEdildi", "2026-09-15");

        // Çekler: portföydeki alınan (+), ödenecek verilen (−), vadesi geçmiş (yarına), ileri tarihli ödendi.
        var alinan = await CekEkle(c, "Alinan", 7_500m, "2026-10-05", kisi: "Veli");
        await CekEkle(c, "Verilen", 3_000m, "2026-10-10", kisi: "Toptancı");
        var gecikmis = await CekEkle(c, "Alinan", 1_000m, "2026-09-20", kisi: "Geciken");
        await CekEkle(c, "Verilen", 2_500m, "2026-09-30", "Odendi", "2026-09-30", kisi: "Nakliye");
        await CekEkle(c, "Alinan", 99_999m, "2027-02-01", kisi: "Çok ileride");   // ufuk dışı

        // İleri tarihli gider, karta bağlı olmayan eski K.K (Ağustos → 28 Eylül, Eylül → 26 Ekim).
        await IslemEkle(c, "2026-09-28", 1_200m);
        await IslemEkle(c, "2026-08-15", 400m, tip: "KrediKarti");
        await IslemEkle(c, "2026-09-05", 600m, tip: "KrediKarti");

        // Kart: kesim 20 / son ödeme 30. 10 Eylül harcaması 20 Eylül ekstresinde (son ödeme 30 Eylül);
        // 27 Eylül'e girilmiş 300 ₺ ödeme o gün çıkar ve ekstreyi 700'e indirir. 22 Eylül harcaması
        // sonraki ekstrede (son ödeme 30 Ekim) — 30 günlük ufkun dışında.
        var kart = await Post(c, "/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-07-20", sonOdemeTarihi = "2026-07-30", limit = 50_000m, borc = 0m });
        await IslemEkle(c, "2026-09-10", 1_000m, "Cari", kart);
        await IslemEkle(c, "2026-09-22", 500m, "Cari", kart);
        await Post(c, "/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-09-27", tutar = 300m });

        // Tekrarlayan: Eylül'ün 5'i onay bekliyor (yarına), Ekim'in 5'i vadede; 26'sı bu ay ileride.
        await Post(c, "/api/giderkalemleri", new { ad = "Kira", aktif = true });
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "Kira", kanal = "Ortak", tutar = 15_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-09-01" });
        await Post(c, "/api/giderkalemleri", new { ad = "SGK", aktif = true });
        await Post(c, "/api/tekrarlayangiderler", new { kalem = "SGK", kanal = "Ortak", tutar = 4_000m, ayinGunu = 26, aktif = true, baslangicAyi = "2026-09-01" });

        var kasa = await GuncelKasa(c);
        Assert.Equal(50_000m + 10_000m - 2_000m + 900m, kasa);

        var t = await Tahmin(c);
        Assert.Equal(kasa, t.BaslangicKasa);
        Assert.Equal(kasa, t.Gunler[0].Kasa);
        Assert.Empty(t.Gunler[0].Kalemler);

        (TahminKalemTuru, decimal)[] Kalemler(DateOnly d) => Gun(t, d).Kalemler.Select(k => (k.Tur, k.Tutar)).OrderBy(x => x.Tur).ThenBy(x => x.Tutar).ToArray();

        // Yarın: vadesi geçmiş alınan çek (+1.000) ve onay bekleyen Eylül kirası (−15.000).
        Assert.Equal(new[] { (TahminKalemTuru.AlinanCek, 1_000m), (TahminKalemTuru.TekrarlayanGider, -15_000m) }, Kalemler(T(9, 25)));
        var gec = Gun(t, T(9, 25)).Kalemler.Single(k => k.Tur == TahminKalemTuru.AlinanCek);
        Assert.True(gec.Gecikmis);
        Assert.Equal(gecikmis, gec.CekId);
        Assert.Equal(T(9, 20), gec.Tarih);
        Assert.Contains("onay bekliyor", Gun(t, T(9, 25)).Kalemler.Single(k => k.Tur == TahminKalemTuru.TekrarlayanGider).Aciklama);

        Assert.Equal(new[] { (TahminKalemTuru.TekrarlayanGider, -4_000m) }, Kalemler(T(9, 26)));
        Assert.Equal(new[] { (TahminKalemTuru.KartOdemesi, -300m) }, Kalemler(T(9, 27)));
        Assert.Equal(new[] { (TahminKalemTuru.IleriTarihliIslem, -1_200m), (TahminKalemTuru.KartsizKrediKarti, -400m) }, Kalemler(T(9, 28)));
        Assert.Equal(new[] { (TahminKalemTuru.VerilenCek, -2_500m), (TahminKalemTuru.KartOdemesi, -700m) }, Kalemler(T(9, 30)));
        Assert.Equal(new[] { (TahminKalemTuru.AlinanCek, 7_500m), (TahminKalemTuru.TekrarlayanGider, -15_000m) }, Kalemler(T(10, 5)));
        Assert.Equal(alinan, Gun(t, T(10, 5)).Kalemler.Single(k => k.Tur == TahminKalemTuru.AlinanCek).CekId);
        Assert.Equal(new[] { (TahminKalemTuru.VerilenCek, -3_000m) }, Kalemler(T(10, 10)));
        Assert.Empty(Kalemler(T(10, 24)));   // Eylül K.K'sı 26 Ekim'de: 30 günlük ufkun (24 Ekim) dışında
        Assert.DoesNotContain(t.Gunler.SelectMany(g => g.Kalemler), k => k.Tur == TahminKalemTuru.KartsizKrediKarti && k.Tutar == -600m);
        Assert.DoesNotContain(t.Gunler.SelectMany(g => g.Kalemler), k => k.Tutar == 99_999m);

        // Kasa zinciri ve toplamlar.
        var degisim = 1_000m - 15_000m - 4_000m - 300m - 1_200m - 400m - 2_500m - 700m + 7_500m - 15_000m - 3_000m;
        Assert.Equal(kasa + degisim, t.SonKasa);
        Assert.Equal(8_500m, t.ToplamGiris);
        Assert.Equal(42_100m, t.ToplamCikis);
        for (int i = 1; i < t.Gunler.Count; i++)
            Assert.Equal(t.Gunler[i - 1].Kasa + t.Gunler[i].Giris - t.Gunler[i].Cikis, t.Gunler[i].Kasa);
        Assert.Equal(t.Gunler.Min(g => g.Kasa), t.EnDusukKasa);
        Assert.Equal(T(10, 10), t.EnDusukTarih);

        // 60 gün: 22 Eylül harcaması (30 Ekim) ve Eylül K.K'sı (26 Ekim) da girer; ilk 30 gün aynı kalır.
        var t60 = await Tahmin(c, "?gun=60");
        Assert.Equal(61, t60.Gunler.Count);
        Assert.Equal(new[] { (TahminKalemTuru.TekrarlayanGider, -4_000m), (TahminKalemTuru.KartsizKrediKarti, -600m) },
            Gun(t60, T(10, 26)).Kalemler.Select(k => (k.Tur, k.Tutar)));
        Assert.Equal(new[] { (TahminKalemTuru.KartOdemesi, -500m) }, Gun(t60, T(10, 30)).Kalemler.Select(k => (k.Tur, k.Tutar)));
        Assert.Equal(t.Gunler.Select(g => g.Kasa), t60.Gunler.Take(31).Select(g => g.Kasa));
    }

    [Fact]
    public async Task Haric_tutulan_cek_hesaba_girmez_ayri_listede_doner()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        var riskli = await CekEkle(c, "Alinan", 7_500m, "2026-10-05", kisi: "Riskli");
        var diger = await CekEkle(c, "Verilen", 2_000m, "2026-10-01");

        var tam = await Tahmin(c);
        Assert.Equal(55_500m, tam.SonKasa);

        var haric = await Tahmin(c, $"?haric={riskli}, 99999");
        Assert.Equal(48_000m, haric.SonKasa);
        Assert.DoesNotContain(haric.Gunler.SelectMany(g => g.Kalemler), k => k.CekId == riskli);
        var h = Assert.Single(haric.HaricKalemler);
        Assert.Equal((riskli, 7_500m, T(10, 5)), (h.CekId!.Value, h.Tutar, h.Tarih));
        Assert.Equal(T(10, 1), haric.EnDusukTarih);
        Assert.Equal(48_000m, haric.EnDusukKasa);

        // Ufuk dışındaki hariç çek listelenmez.
        Assert.Empty((await Tahmin(c, $"?gun=5&haric={riskli},{diger}")).HaricKalemler);
    }

    [Theory]
    [InlineData("?gun=0")]
    [InlineData("?gun=367")]
    [InlineData("?gun=-5")]
    [InlineData("?haric=abc")]
    [InlineData("?haric=1,x")]
    [InlineData("?haric=-3")]
    [InlineData("?haric=1.5")]
    public async Task Tahmin_gecersiz_parametre_400(string sorgu)
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/rapor/tahmin" + sorgu)).StatusCode);
    }

    [Fact]
    public async Task Tahmin_raporlari_ve_gecmisi_degistirmez()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        await GelenYaz(c, "2026-09-14", "MEZAT", 8_000m);
        await IslemEkle(c, "2026-08-20", 700m, tip: "KrediKarti");
        await IslemEkle(c, "2026-09-29", 300m);
        var cek = await CekEkle(c, "Alinan", 5_000m, "2026-10-02");
        var kart = await Post(c, "/api/kredikartlari", new { ad = "Axess", kesimTarihi = "2026-07-12", sonOdemeTarihi = "2026-07-22", limit = 10_000m, borc = 2_000m });
        await IslemEkle(c, "2026-09-11", 400m, "Cari", kart);

        async Task<string[]> Anlik()
        {
            var r = await c.GetAsync("/api/gecmis?limit=1");
            return
            [
                await c.GetStringAsync("/api/rapor/panel"),
                await c.GetStringAsync("/api/rapor/haftalik"),
                await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9"),
                await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=10"),
                await c.GetStringAsync("/api/kredikartlari"),
                await c.GetStringAsync("/api/cekler/ozet"),
                r.Headers.GetValues("X-Toplam-Kayit").Single(),
            ];
        }

        var once = await Anlik();
        foreach (var s in new[] { "", "?gun=60", "?gun=90", $"?gun=90&haric={cek}", "?gun=366" })
            await Tahmin(c, s);
        Assert.Equal(once, await Anlik());
    }

    [Fact]
    public async Task Tahmin_edilen_kasa_zaman_gelince_gercek_kasaya_esit()
    {
        // Kendiliğinden gerçekleşen kalemler (ileri tarihli işlem/ödeme/çek, ertelenen K.K) için tahmin,
        // o gün geldiğinde hesap motorunun ürettiği kasayla birebir aynı olmalı.
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        await GelenYaz(c, "2026-09-21", "PERAKENDE", 3_250.50m);
        await IslemEkle(c, "2026-09-28", 1_200m);
        await IslemEkle(c, "2026-10-03", 800.25m, tip: "Cari");
        await IslemEkle(c, "2026-08-15", 400m, tip: "KrediKarti");
        await IslemEkle(c, "2026-09-05", 600m, tip: "KrediKarti");
        await IslemEkle(c, "2026-09-29", 111m, tip: "KrediKarti");   // ileri tarihli eski K.K → Ekim sonunda
        await CekEkle(c, "Verilen", 2_500m, "2026-09-30", "Odendi", "2026-09-30");
        await CekEkle(c, "Alinan", 4_000m, "2026-10-01", "TahsilEdildi", "2026-10-02");
        var kart = await Post(c, "/api/kredikartlari", new { ad = "World", kesimTarihi = "2026-07-20", sonOdemeTarihi = "2026-07-30", limit = 0m, borc = 0m });
        await Post(c, "/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-09-27", tutar = 300m });

        var t = await Tahmin(c, "?gun=45");
        foreach (var gun in new[] { T(9, 26), T(9, 27), T(9, 28), T(9, 30), T(10, 1), T(10, 2), T(10, 3), T(10, 25), T(10, 26), T(11, 8) })
        {
            f.Saat.Ayarla(gun);
            Assert.Equal(Gun(t, gun).Kasa, await GuncelKasa(c));
        }
    }

    // ─────────────────────────── eksik gelen ───────────────────────────

    private record EksikYanit(DateOnly DonemStart, DateOnly DonemEnd, List<string> Kanallar);

    [Fact]
    public async Task Eksik_gelen_son_iki_haftada_biten_donemlerde_aktif_kanallar_icin()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        // Kurulumla gelen kanalların geçmişte "Eklendi" satırı yok: başlangıçları ilk hareketleridir (KanalDonemleri,
        // Gelenler sayfasının eksik listesiyle aynı kural). Ağustos gelenleri üçünün de bu haftalardan önce var olduğunu gösterir.
        foreach (var k in new[] { "MEZAT", "PERAKENDE", "TOPTAN" }) await GelenYaz(c, "2026-08-03", k, 1m);
        await Post(c, "/api/kanallar", new { ad = "PASİF", aktif = false, sira = 9, acilisDevri = 0m });
        await GelenYaz(c, "2026-09-16", "MEZAT", 0m);        // sıfır da "girildi" sayılır; tarih dönem başına çekilir
        await GelenYaz(c, "2026-09-14", "PERAKENDE", 1_000m);
        await GelenYaz(c, "2026-09-21", "TOPTAN", 500m);      // içinde bulunulan dönem (henüz bitmedi)

        var l = (await c.GetFromJsonAsync<List<EksikYanit>>("/api/gelenler/eksik", Json))!;
        Assert.Equal(2, l.Count);
        Assert.Equal((T(9, 7), T(9, 13)), (l[0].DonemStart, l[0].DonemEnd));
        Assert.Equal(["MEZAT", "PERAKENDE", "TOPTAN"], l[0].Kanallar);
        Assert.Equal((T(9, 14), T(9, 20)), (l[1].DonemStart, l[1].DonemEnd));
        Assert.Equal(["TOPTAN"], l[1].Kanallar);

        // Pazartesi: 21–27 Eylül dönemi bitti; 7–13 Eylül iki haftadan eski kaldı.
        f.Saat.Ayarla(T(9, 28));
        l = (await c.GetFromJsonAsync<List<EksikYanit>>("/api/gelenler/eksik", Json))!;
        Assert.Equal([T(9, 14), T(9, 21)], l.Select(x => x.DonemStart));
        Assert.Equal(["MEZAT", "PERAKENDE"], l[1].Kanallar);

        // Hepsi girilince liste boş.
        foreach (var k in new[] { "MEZAT", "PERAKENDE" }) await GelenYaz(c, "2026-09-21", k, 1m);
        await GelenYaz(c, "2026-09-14", "TOPTAN", 1m);
        Assert.Empty((await c.GetFromJsonAsync<List<EksikYanit>>("/api/gelenler/eksik", Json))!);
    }

    [Fact]
    public async Task Eksik_gelen_takip_yeni_basladiysa_bos()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f, takip: Bugun);
        Assert.Empty((await c.GetFromJsonAsync<List<EksikYanit>>("/api/gelenler/eksik", Json))!);
        await KasaWebFactory.TakipBaslangiciAyarla(c, Bugun.AddDays(10));   // takip ileride
        Assert.Empty((await c.GetFromJsonAsync<List<EksikYanit>>("/api/gelenler/eksik", Json))!);
    }

    // ─────────────────────────── geçmiş özeti ───────────────────────────

    private record OzetSatiri(int Id, DateTime ZamanUtc, string Tur, string Ozet);
    private record OzetYanit(int SonId, DateTime? SonZamanUtc, int Toplam, int GecmiseDonuk, List<OzetSatiri> GecmiseDonukSatirlar);

    [Fact]
    public async Task Gecmis_ozeti_bos_gecmiste_sifir_negatif_sonId_400()
    {
        using var f = new Fabrika();
        var c = await f.EditorClientAsync();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Degisiklikler.RemoveRange(db.Degisiklikler);
            db.SaveChanges();
        }
        var o = (await c.GetFromJsonAsync<OzetYanit>("/api/gecmis/ozet?sonId=0", Json))!;
        Assert.Equal((0, (DateTime?)null, 0, 0), (o.SonId, o.SonZamanUtc, o.Toplam, o.GecmiseDonuk));
        Assert.Empty(o.GecmiseDonukSatirlar);
        Assert.Equal(0, (await c.GetFromJsonAsync<OzetYanit>("/api/gecmis/ozet", Json))!.SonId);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gecmis/ozet?sonId=-1")).StatusCode);
    }

    [Fact]
    public async Task Gecmis_ozeti_son_bakistan_beri_degisiklik_ve_gecmise_donukleri_sayar()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        var bas = (await c.GetFromJsonAsync<OzetYanit>("/api/gecmis/ozet", Json))!;
        Assert.True(bas.SonId > 0);   // takip başlangıcı değişikliği
        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), bas.SonZamanUtc);
        Assert.Equal(0, bas.Toplam);   // sonId verilmedi → sayım yok

        var agustos = await IslemEkle(c, "2026-08-15", 1_000m);   // geçmiş ay → geçmişe dönük
        var eylul = await IslemEkle(c, "2026-09-10", 2_000m);     // bu ay → değil
        (await c.PutAsJsonAsync($"/api/islemler/{eylul}", new { tarih = "2026-09-10", cari = "Market", tutarTl = 2_000m, kanal = "MEZAT", tip = "Cari", not = "yalnız not" })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/islemler/{eylul}", new { tarih = "2026-08-20", cari = "Market", tutarTl = 2_000m, kanal = "MEZAT", tip = "Cari" })).EnsureSuccessStatusCode();
        await GelenYaz(c, "2026-08-10", "MEZAT", 5_000m);          // Ağustos geleni → geçmişe dönük
        await Post(c, "/api/cariler", new { ad = "Yeni Cari", aktif = true });   // rakam değil
        await CekEkle(c, "Alinan", 900m, "2026-08-25", "TahsilEdildi", "2026-08-25");   // Ağustos tahsilatı
        await CekEkle(c, "Alinan", 900m, "2026-08-25");            // portföy: kasaya dokunmaz

        var o = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={bas.SonId}", Json))!;
        Assert.Equal(8, o.Toplam);
        Assert.Equal(4, o.GecmiseDonuk);
        Assert.Equal(o.SonId - 1, o.GecmiseDonukSatirlar[0].Id);   // en yeni satır (portföy çeki) değil, ondan önceki
        Assert.Equal(["Çek", "Gelen", "İşlem", "İşlem"], o.GecmiseDonukSatirlar.Select(x => x.Tur));
        Assert.True(o.GecmiseDonukSatirlar.Select(x => x.Id).SequenceEqual(o.GecmiseDonukSatirlar.Select(x => x.Id).OrderDescending()));

        // Geçmiş listesindeki işaret özetle aynı kuraldan gelir.
        var liste = (await c.GetFromJsonAsync<List<GecmisSatiri>>("/api/gecmis?limit=1000", Json))!
            .Where(x => x.Id > bas.SonId).ToList();
        Assert.Equal(o.GecmiseDonukSatirlar.Select(x => x.Id), liste.Where(x => x.GecmiseDonuk).Select(x => x.Id));
        Assert.Contains(liste, x => x.Tur == "İşlem" && x.Eylem == "Eklendi" && x.GecmiseDonuk && x.Ozet.Contains("15.08.2026"));

        // Güncel bakış: yeni değişiklik yok; ileri bir Id de (DB sıfırlandı) boş döner.
        var yeni = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={o.SonId}", Json))!;
        Assert.Equal((0, 0), (yeni.Toplam, yeni.GecmiseDonuk));
        Assert.Equal(0, (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={o.SonId + 50}", Json))!.Toplam);

        // En fazla 5 satır döner; sayı hepsini sayar.
        for (int i = 0; i < 4; i++) await IslemEkle(c, "2026-07-0" + (i + 1), 10m + i);
        var cok = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={bas.SonId}", Json))!;
        Assert.Equal(8, cok.GecmiseDonuk);
        Assert.Equal(5, cok.GecmiseDonukSatirlar.Count);
        Assert.True(liste.Single(x => x.Tur == "İşlem" && x.KayitId == agustos && x.Eylem == "Eklendi").GecmiseDonuk);
        Assert.False(liste.Single(x => x.Tur == "İşlem" && x.KayitId == eylul && x.Eylem == "Eklendi").GecmiseDonuk);
    }

    [Fact]
    public async Task Gecen_ayin_kk_harcamasi_gecen_ayi_degistirmez_gecmise_donuk_sayilmaz()
    {
        using var f = new Fabrika();
        var c = await EditorAsync(f);
        await GelenYaz(c, "2026-08-10", "MEZAT", 20_000m);
        await IslemEkle(c, "2026-08-12", 1_000m);
        var kart = await Post(c, "/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-08-20", sonOdemeTarihi = "2026-08-30", limit = 50_000m, borc = 0m });

        async Task<(string Aylik, string Haftalik)> Agustos()
        {
            var haftalik = JsonDocument.Parse(await c.GetStringAsync("/api/rapor/haftalik")).RootElement.EnumerateArray()
                .Where(h => h.GetProperty("donem").GetProperty("start").GetString()!.StartsWith("2026-08"))
                .Select(h => h.GetRawText());
            return (await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=8"), string.Join("\n", haftalik));
        }
        var once = await Agustos();
        Assert.NotEqual("", once.Haftalik);
        var eylulOnce = await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9");
        var bas = (await c.GetFromJsonAsync<OzetYanit>("/api/gecmis/ozet", Json))!;

        var bagli = await IslemEkle(c, "2026-08-20", 700m, kart: kart);
        var eskiUsul = await IslemEkle(c, "2026-08-21", 300m, tip: "KrediKarti");

        // Ağustos'un aylık ve haftalık rakamları aynı kalır; K.K Eylül'ün sonucundan düşer.
        Assert.Equal(once, await Agustos());
        Assert.NotEqual(eylulOnce, await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9"));
        var o = (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={bas.SonId}", Json))!;
        Assert.Equal((2, 0), (o.Toplam, o.GecmiseDonuk));
        var liste = (await c.GetFromJsonAsync<List<GecmisSatiri>>("/api/gecmis?limit=1000", Json))!;
        Assert.False(liste.Single(x => x.Tur == "İşlem" && x.KayitId == bagli).GecmiseDonuk);
        Assert.False(liste.Single(x => x.Tur == "İşlem" && x.KayitId == eskiUsul).GecmiseDonuk);

        // Temmuz'un K.K'sı ise Ağustos'un (kapanmış ayın) sonucunu değiştirir → geçmişe dönük.
        var temmuz = await IslemEkle(c, "2026-07-25", 400m, tip: "KrediKarti");
        Assert.NotEqual(once.Aylik, (await Agustos()).Aylik);
        liste = (await c.GetFromJsonAsync<List<GecmisSatiri>>("/api/gecmis?limit=1000", Json))!;
        Assert.True(liste.Single(x => x.Tur == "İşlem" && x.KayitId == temmuz).GecmiseDonuk);
        Assert.Equal(1, (await c.GetFromJsonAsync<OzetYanit>($"/api/gecmis/ozet?sonId={bas.SonId}", Json))!.GecmiseDonuk);
    }
}

/// <summary>Geçmişe dönük kuralı (saf): tür, eski/yeni JSON ve değişiklik zamanı.</summary>
public class GecmiseDonukKuraliTests
{
    private static readonly DateTime Eylul24 = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    private static string Islem(string tarih, decimal tutar = 100m, string? not = null, string tip = "Cari", int? kart = null)
        => $$"""{"id":1,"tarih":"{{tarih}}","cari":"Market","tutarTl":{{tutar.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"kanal":"MEZAT","tip":"{{tip}}","not":{{(not is null ? "null" : $"\"{not}\"")}},"krediKartiId":{{(kart is null ? "null" : kart.ToString())}}}""";

    [Theory]
    // Geçen ayın K.K'sı bu ay girildi: rakamı bu ay etkiler (ertesi ay) → geçmişe dönük değil.
    [InlineData("2026-08-20", "KrediKarti", null, false)]
    [InlineData("2026-08-31", "KrediKarti", null, false)]
    [InlineData("2026-08-20", "Cari", 3, false)]            // karta bağlı: kayıtlı tipi ne olursa olsun K.K
    [InlineData("2026-08-20", "SabitGider", 3, false)]
    // İki ay önceki K.K geçen ayın (kapanmış) rakamını değiştirir.
    [InlineData("2026-07-31", "KrediKarti", null, true)]
    [InlineData("2026-07-05", "Cari", 3, true)]
    [InlineData("2025-12-15", "KrediKarti", null, true)]
    // Geçen ayın Cari / Sabit gideri kendi ayında düşer.
    [InlineData("2026-08-20", "Cari", null, true)]
    [InlineData("2026-08-20", "SabitGider", null, true)]
    [InlineData("2026-09-10", "KrediKarti", null, false)]   // bu ayın K.K'sı
    public void Kk_harcamasi_rakamlari_ertesi_ay_etkiler(string tarih, string tip, int? kart, bool beklenen)
    {
        Assert.Equal(beklenen, GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem(tarih, tip: tip, kart: kart), Eylul24));
        Assert.Equal(beklenen, GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem(tarih, tip: tip, kart: kart), null, Eylul24));
    }

    [Fact]
    public void Kk_guncellemesinde_her_hal_kendi_etkiledigi_aya_gore()
    {
        // Geçen ayın Cari gideri K.K yapıldı: Ağustos'un Cari'si azaldı → geçmişe dönük.
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20"), Islem("2026-08-20", tip: "KrediKarti"), Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20", tip: "KrediKarti"), Islem("2026-08-20"), Eylul24));
        // Geçen ayın K.K'sı karta bağlandı / tutarı düzeltildi: ikisi de Eylül'ü etkiler.
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20", tip: "KrediKarti"), Islem("2026-08-20", tip: "KrediKarti", kart: 3), Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20", 100m, tip: "KrediKarti"), Islem("2026-08-20", 150m, tip: "KrediKarti"), Eylul24));
        // Geçen ayın K.K'sı Temmuz'a taşındı: yeni hali Ağustos'u etkiler.
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20", tip: "KrediKarti"), Islem("2026-07-20", tip: "KrediKarti"), Eylul24));
        // Ay sınırı Türkiye saatine göre: 1 Ekim 01:00 İstanbul'da 31 Ağustos K.K'sı (Eylül'ü etkiler) geçmiştedir.
        var ekimBasi = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem("2026-08-31", tip: "KrediKarti"), ekimBasi));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem("2026-08-31", tip: "KrediKarti"), ekimBasi.AddHours(-2)));
        // Sayısal tip (eski biçim) de tanınır.
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null,
            Islem("2026-08-20").Replace("\"tip\":\"Cari\"", $"\"tip\":{(int)GiderTipi.KrediKarti}"), Eylul24));
    }

    [Theory]
    [InlineData("2026-08-31", true)]
    [InlineData("2025-12-01", true)]
    [InlineData("2026-09-01", false)]
    [InlineData("2026-10-15", false)]   // ileri tarih
    public void Eklenen_ve_silinen_islem_kaydin_ayina_gore(string tarih, bool beklenen)
    {
        Assert.Equal(beklenen, GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem(tarih), Eylul24));
        Assert.Equal(beklenen, GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem(tarih), null, Eylul24));
    }

    [Fact]
    public void Guncellemede_para_alani_degismezse_geçmise_donuk_degil()
    {
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-01"), Islem("2026-08-01", not: "düzeltme"), Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-01", 100m), Islem("2026-08-01", 100.00m), Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-01", 100m), Islem("2026-08-01", 101m), Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-01"), Islem("2026-08-01", tip: "SabitGider"), Eylul24));
        // Bu aydaki kayıt geçmiş aya taşındı (eski hali bu ay, yeni hali geçmiş ay) → geçmişe dönük.
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-09-10"), Islem("2026-08-20"), Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-08-20"), Islem("2026-09-10"), Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, Islem("2026-09-10"), Islem("2026-09-11"), Eylul24));
    }

    [Fact]
    public void Ay_siniri_Turkiye_saatine_gore()
    {
        // 30 Eylül 22:00 UTC = 1 Ekim 01:00 İstanbul: Eylül kaydı artık geçmiş ayda.
        var ekimBasi = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem("2026-09-30"), ekimBasi));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem("2026-09-30"), ekimBasi.AddHours(-2)));   // 23:00 İstanbul
        // Türü belirtilmemiş zaman UTC kabul edilir (SQLite'tan okunan değer).
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, Islem("2026-09-30"), DateTime.SpecifyKind(ekimBasi, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Cek_yalniz_tahsil_edildi_ya_da_odendi_ise_islem_tarihine_bakar()
    {
        static string Cek(string durum, string? islem) =>
            $$"""{"yon":"Alinan","kisi":"A","tutar":900,"vadeTarihi":"2026-08-25","kanal":"MEZAT","durum":"{{durum}}","islemTarihi":{{(islem is null ? "null" : $"\"{islem}\"")}}}""";
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Cek, null, Cek("Portfoyde", null), Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Cek, null, Cek("CiroEdildi", "2026-08-25"), Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Cek, null, Cek("TahsilEdildi", "2026-08-25"), Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Cek, null, Cek("TahsilEdildi", "2026-09-02"), Eylul24));
        // Geçmiş aydaki tahsilat geri alındı (portföye döndü) → geçmişe dönük.
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Cek, Cek("TahsilEdildi", "2026-08-25"), Cek("Portfoyde", null), Eylul24));
    }

    [Fact]
    public void Gelen_ve_kart_odemesi_tarihine_gore()
    {
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Gelen, null, """{"donemStart":"2026-08-31","kanal":"MEZAT","tutarTl":5}""", Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Gelen, null, """{"donemStart":"2026-09-01","kanal":"MEZAT","tutarTl":5}""", Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.KartOdemesi, """{"tarih":"2026-08-02","tutar":5,"not":null}""", null, Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.KartOdemesi, """{"tarih":"2026-08-02","tutar":5,"not":null}""", """{"tarih":"2026-08-02","tutar":5,"not":"x"}""", Eylul24));
    }

    [Fact]
    public void Acilis_devri_degisimi_her_zaman_gecmise_donuk()
    {
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Ayar, """{"takipBaslangic":"2026-08-01","kasaAcilisDevri":0}""", """{"takipBaslangic":"2026-08-01","kasaAcilisDevri":10}""", Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Ayar, """{"takipBaslangic":"2026-08-01","kasaAcilisDevri":0}""", """{"takipBaslangic":"2026-07-01","kasaAcilisDevri":0}""", Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Ayar, """{"takipBaslangic":"2026-08-01","kasaAcilisDevri":0}""", """{"takipBaslangic":"2026-08-01","kasaAcilisDevri":0.0}""", Eylul24));
        Assert.True(GecmiseDonukKurali.Mi(GecmisTurleri.Kanal, """{"ad":"MEZAT","acilisDevri":0}""", """{"ad":"MEZAT","acilisDevri":-50}""", Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Kanal, """{"ad":"MEZAT","acilisDevri":0}""", """{"ad":"MEZAT 2","acilisDevri":0}""", Eylul24));
        Assert.False(GecmiseDonukKurali.Mi(GecmisTurleri.Kanal, null, """{"ad":"YENİ","acilisDevri":100}""", Eylul24));   // yeni kanal
    }

    [Theory]
    [InlineData("Cari", null, """{"ad":"X"}""")]
    [InlineData("Kasa sayımı", null, """{"tarih":"2026-08-01","sayilanTutar":5}""")]
    [InlineData("Kredi kartı", null, """{"ad":"K","kesimTarihi":"2026-01-05"}""")]
    [InlineData("İşlem", """[{"id":1}]""", """[{"id":2}]""")]   // toplu özet (dizi)
    [InlineData("İşlem", "bozuk", """{"tarih":"2026-08-01"}""")]
    [InlineData("İşlem", null, null)]
    [InlineData("İşlem", null, """{"tarih":null}""")]
    public void Ilgisiz_tur_ve_bicim_gecmise_donuk_degil(string tur, string? eski, string? yeni)
        => Assert.False(GecmiseDonukKurali.Mi(tur, eski, yeni, Eylul24));
}
