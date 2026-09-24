using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket C sunucu uçları: kaydetmeden önce uyarılar, toplu yükleme (ya hep ya hiç), cariye göre
/// öneri, gelişmiş işlem araması ve son silme araması. Bugün = 24 Eylül 2026 (sabit saat).
/// </summary>
public class HizliGirisApiTests : IClassFixture<HizliGirisApiTests.SabitSaatFactory>
{
    public class SabitSaatFactory : KasaWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitSaat())));
        }
    }

    private sealed class SabitSaat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    }

    private record UyariYanit(string Kod, string Mesaj);
    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, string Tip, string? Not, int? KrediKartiId);
    private record TopluYanit(int Eklenen, decimal Toplam, List<string> YeniCariler, List<IslemYanit> Islemler);
    private record SatirHatasi(int Sira, string Hata);
    private record TopluHataYanit(string Hata, List<SatirHatasi> Satirlar);
    private record OneriYanit(string Cari, DateOnly Tarih, decimal TutarTl, string Kanal, string Tip, int? KrediKartiId);
    private record SilmeYanit(int Id, string Tur, int? KayitId, string Eylem, bool GeriAlindi, bool GeriAlinabilir);
    private record HataYanit(string Hata);
    private record PanelYanit(decimal GuncelKasa, decimal BuHaftaSonucu, decimal BuAySonucu);

    private readonly SabitSaatFactory _factory;
    public HizliGirisApiTests(SabitSaatFactory factory) => _factory = factory;

    private static async Task CariEkle(HttpClient c, string ad)
        => Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/cariler", new { ad })).StatusCode);

    private static async Task<IslemYanit> IslemEkle(HttpClient c, string tarih, string cari, decimal tutar,
        string kanal = "MEZAT", string tip = "Cari", string? not = null, int? krediKartiId = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih, cari, tutarTl = tutar, kanal, tip, not, krediKartiId });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<IslemYanit>())!;
    }

    private static async Task<List<UyariYanit>> Uyarilar(HttpClient c, string tarih, string? cari, decimal tutar,
        string tip = "Cari", int? haricId = null, int? krediKartiId = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler/uyarilar",
            new { tarih, cari, tutarTl = tutar, kanal = "MEZAT", tip, krediKartiId, haricId });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<List<UyariYanit>>())!;
    }

    private async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    private int Say<T>(Func<KasaDbContext, IQueryable<T>> q)
    {
        using var scope = _factory.Services.CreateScope();
        return q(scope.ServiceProvider.GetRequiredService<KasaDbContext>()).Count();
    }

    // ---------- 28: Kaydetmeden önce uyarılar ----------

    [Fact]
    public async Task Ayni_cariye_ayni_tutar_3_gun_icinde_uyarir_disinda_uyarmaz()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Uyarı Çift Ltd");
        var ilk = await IslemEkle(c, "2026-09-20", "Uyarı Çift Ltd", 1_500m);

        var u = await Uyarilar(c, "2026-09-23", "uyarı çift ltd", 1_500m); // Türkçe harf duyarsız
        var ayni = Assert.Single(u, x => x.Kod == "AyniTutar");
        Assert.Contains("20.09.2026", ayni.Mesaj);
        Assert.Contains("1.500,00 ₺", ayni.Mesaj);

        Assert.Contains(await Uyarilar(c, "2026-09-17", "Uyarı Çift Ltd", 1_500m), x => x.Kod == "AyniTutar"); // tam 3 gün önce
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Uyarı Çift Ltd", 1_500m), x => x.Kod == "AyniTutar"); // 4 gün
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-21", "Uyarı Çift Ltd", 1_500.01m), x => x.Kod == "AyniTutar");
        // Düzenlenen kayıt kendisiyle çift sayılmaz.
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-20", "Uyarı Çift Ltd", 1_500m, haricId: ilk.Id), x => x.Kod == "AyniTutar");
        // Kayıtlı olmayan cari: uyarı yok, hata da yok.
        Assert.Empty(await Uyarilar(c, "2026-09-20", "Hiç Olmayan Cari", 1_500m));
    }

    [Fact]
    public async Task Olagan_disi_tutar_ortancanin_10_kati_ve_en_az_3_islem_ister()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Ortanca Market");
        await IslemEkle(c, "2026-06-01", "Ortanca Market", 100m);
        await IslemEkle(c, "2026-06-02", "Ortanca Market", 120m);
        // 2 işlem: örnek yetersiz.
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Ortanca Market", 50_000m), x => x.Kod == "OlaganDisiTutar");

        await IslemEkle(c, "2026-06-03", "Ortanca Market", 110m); // ortanca 110
        var u = Assert.Single(await Uyarilar(c, "2026-09-24", "Ortanca Market", 1_100m), x => x.Kod == "OlaganDisiTutar");
        Assert.Contains("10 katı", u.Mesaj);
        Assert.Contains("110,00 ₺", u.Mesaj);
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Ortanca Market", 1_099.99m), x => x.Kod == "OlaganDisiTutar");
    }

    [Fact]
    public void Ortanca_cift_sayida_ortadaki_ikisinin_ortalamasidir()
    {
        Assert.Equal(15m, Kasa.Api.Servisler.IslemUyarilari.Ortanca([10m, 20m]));
        Assert.Equal(20m, Kasa.Api.Servisler.IslemUyarilari.Ortanca([30m, 10m, 20m]));
        Assert.Equal(0m, Kasa.Api.Servisler.IslemUyarilari.Ortanca([]));
        Assert.Equal(2.5m, Kasa.Api.Servisler.IslemUyarilari.Ortanca([4m, 1m, 3m, 2m]));
    }

    [Fact]
    public async Task Olagan_disi_tutar_yalniz_son_20_islemi_sayar()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Son Yirmi AŞ");
        // Eski 5 büyük işlem + yeni 20 küçük işlem: ortanca küçüklerden hesaplanır.
        for (var i = 0; i < 5; i++) await IslemEkle(c, $"2026-01-0{i + 1}", "Son Yirmi AŞ", 100_000m);
        for (var i = 0; i < 20; i++) await IslemEkle(c, $"2026-07-{i + 1:D2}", "Son Yirmi AŞ", 10m);
        Assert.Contains(await Uyarilar(c, "2026-09-24", "Son Yirmi AŞ", 100m), x => x.Kod == "OlaganDisiTutar");
    }

    [Fact]
    public async Task Eski_tarih_45_gunden_eskiyse_uyarir()
    {
        var c = await _factory.EditorClientAsync();
        // Bugün 24.09.2026; 45 gün önce 10.08.2026.
        var u = Assert.Single(await Uyarilar(c, "2026-08-09", null, 10m), x => x.Kod == "EskiTarih");
        Assert.Contains("09.08.2026", u.Mesaj);
        Assert.DoesNotContain(await Uyarilar(c, "2026-08-10", null, 10m), x => x.Kod == "EskiTarih");

        // Eski tarihli bir kaydı bugüne taşımak da o eski raporu değiştirir.
        await CariEkle(c, "Eski Kayıt Cari");
        var eski = await IslemEkle(c, "2026-07-01", "Eski Kayıt Cari", 75m);
        var d = Assert.Single(await Uyarilar(c, "2026-09-24", "Eski Kayıt Cari", 75m, haricId: eski.Id), x => x.Kod == "EskiTarih");
        Assert.Contains("01.07.2026", d.Mesaj);
    }

    [Fact]
    public async Task Ayni_kisiye_ayni_tutarda_verilen_cek_cift_dusme_uyarisi_verir()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Çek Kişi İnşaat");
        async Task Cek(string yon, string durum, string kisi, decimal tutar, string vade, string? islemTarihi)
            => Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/cekler", new
            {
                yon, cekNo = "A-1", banka = "Ziraat", kisi, tutar, duzenlemeTarihi = "2026-09-01", vadeTarihi = vade,
                kanal = "MEZAT", durum, islemTarihi, not = (string?)null,
            })).StatusCode);

        await Cek("Verilen", "Odendi", "ÇEK KİŞİ İNŞAAT", 7_000m, "2026-09-18", "2026-09-19");
        var u = Assert.Single(await Uyarilar(c, "2026-09-24", "çek kişi inşaat", 7_000m), x => x.Kod == "CekCiftDusme");
        Assert.Contains("ödenmiş", u.Mesaj);
        Assert.Contains("19.09.2026", u.Mesaj);
        // ±7 gün dışında ya da farklı tutarda uyarı yok.
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-27", "Çek Kişi İnşaat", 7_000m), x => x.Kod == "CekCiftDusme");
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Çek Kişi İnşaat", 7_000.5m), x => x.Kod == "CekCiftDusme");

        // Ödenecek (portföydeki) verilen çek: vadesi ±7 gün.
        await Cek("Verilen", "Portfoyde", "Çek Kişi İnşaat", 3_000m, "2026-09-30", null);
        var p = Assert.Single(await Uyarilar(c, "2026-09-24", "Çek Kişi İnşaat", 3_000m), x => x.Kod == "CekCiftDusme");
        Assert.Contains("vade 30.09.2026", p.Mesaj);
        // Alınan çek sayılmaz; kişi eşleşmezse sayılmaz.
        await Cek("Alinan", "Portfoyde", "Çek Kişi İnşaat", 4_000m, "2026-09-25", null);
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Çek Kişi İnşaat", 4_000m), x => x.Kod == "CekCiftDusme");
        Assert.DoesNotContain(await Uyarilar(c, "2026-09-24", "Başka Firma", 3_000m), x => x.Kod == "CekCiftDusme");
    }

    [Fact]
    public async Task Uyari_kaydi_engellemez_ve_yetki_ister()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Engelsiz Cari");
        await IslemEkle(c, "2026-09-24", "Engelsiz Cari", 500m);
        Assert.Contains(await Uyarilar(c, "2026-09-24", "Engelsiz Cari", 500m), x => x.Kod == "AyniTutar");
        await IslemEkle(c, "2026-09-24", "Engelsiz Cari", 500m); // uyarıya rağmen kaydedilir

        var govde = new { tarih = "2026-09-24", cari = "Engelsiz Cari", tutarTl = 500m, kanal = "MEZAT", tip = "Cari" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/islemler/uyarilar", govde)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await IzleyiciAsync()).PostAsJsonAsync("/api/islemler/uyarilar", govde)).StatusCode);
    }

    // ---------- 16: Toplu yükleme ----------

    private static object Satir(string tarih, string? cari, decimal tutar, string kanal = "MEZAT", string tip = "Cari",
        string? not = null, int? krediKartiId = null)
        => new { tarih, cari, tutarTl = tutar, kanal, tip, not, krediKartiId };

    [Fact]
    public async Task Toplu_yukleme_hepsini_tek_seferde_ekler_ve_gecmise_ozet_yazar()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Toplu Bir");
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Toplu Kira" })).StatusCode);
        var kart = await (await c.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Toplu Kart", kesimTarihi = "2026-09-10", sonOdemeTarihi = "2026-09-20", limit = 10_000m, borc = 0m,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var kartId = kart.GetProperty("id").GetInt32();

        var r = await c.PostAsJsonAsync("/api/islemler/toplu", new
        {
            satirlar = new[]
            {
                Satir("2026-09-01", "toplu bir", 100.10m, not: "  ilk  "),
                Satir("2026-09-02", "toplu kira", 2_000m, tip: "SabitGider"),
                Satir("2026-09-03", "Toplu Bir", 50m, tip: "Cari", krediKartiId: kartId),
            },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var s = (await r.Content.ReadFromJsonAsync<TopluYanit>())!;
        Assert.Equal(3, s.Eklenen);
        Assert.Equal(2_150.10m, s.Toplam);
        Assert.Empty(s.YeniCariler);
        Assert.Equal(["Toplu Bir", "Toplu Kira", "Toplu Bir"], s.Islemler.Select(i => i.Cari)); // kayıtlı yazım
        Assert.Equal("ilk", s.Islemler[0].Not);
        Assert.Equal("KrediKarti", s.Islemler[2].Tip); // karta bağlı satır K.K olur
        Assert.All(s.Islemler, i => Assert.True(i.Id > 0));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var ozet = db.Degisiklikler.AsNoTracking().Where(d => d.Tur == GecmisTurleri.Islem && d.Ozet.StartsWith("Toplu yükleme"))
            .OrderByDescending(d => d.Id).First();
        Assert.Equal("Toplu yükleme: 3 işlem eklendi (toplam 2.150,10 ₺, 01.09.2026 – 03.09.2026)", ozet.Ozet);
        var ids = s.Islemler.Select(i => (int?)i.Id).ToList();
        Assert.Equal(3, db.Degisiklikler.Count(d => d.Tur == GecmisTurleri.Islem && d.Eylem == Eylemler.Eklendi && ids.Contains(d.KayitId)));
    }

    [Fact]
    public async Task Toplu_yuklemede_tek_hatali_satir_varsa_hicbiri_kaydedilmez()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Toplu İki");
        var islemOnce = Say(db => db.Islemler);
        var gecmisOnce = Say(db => db.Degisiklikler);
        var cariOnce = Say(db => db.Cariler);

        var r = await c.PostAsJsonAsync("/api/islemler/toplu", new
        {
            satirlar = new[]
            {
                Satir("2026-09-01", "Toplu İki", 10m),
                Satir("2026-09-01", "Kayıtsız Firma", 10m),
                Satir("2026-09-01", "Toplu İki", 10m, kanal: "YOK"),
                Satir("2026-09-01", "Toplu İki", 0m),
                Satir("2026-09-01", "Toplu İki", 10.001m),
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var h = (await r.Content.ReadFromJsonAsync<TopluHataYanit>())!;
        Assert.Equal([2, 3, 4, 5], h.Satirlar.Select(x => x.Sira));
        Assert.StartsWith("4 satırda hata var; hiçbir satır kaydedilmedi. 2. satır:", h.Hata);
        Assert.Contains("adında bir cari yok", h.Satirlar[0].Hata);
        Assert.Contains("kanal yok", h.Satirlar[1].Hata);

        // Yeni cari eklenecekti ama başka bir satır hatalı: cari de eklenmez (tek transaction).
        var r2 = await c.PostAsJsonAsync("/api/islemler/toplu", new
        {
            yeniCarileriEkle = true,
            satirlar = new[] { Satir("2026-09-01", "Geri Dönen Cari", 10m), Satir("2026-09-01", "Toplu İki", -5m) },
        });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal([2], (await r2.Content.ReadFromJsonAsync<TopluHataYanit>())!.Satirlar.Select(x => x.Sira));

        Assert.Equal(islemOnce, Say(db => db.Islemler));
        Assert.Equal(gecmisOnce, Say(db => db.Degisiklikler));
        Assert.Equal(cariOnce, Say(db => db.Cariler));
    }

    [Fact]
    public async Task Toplu_yukleme_istenirse_yeni_carileri_bir_kez_ekler()
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Toplu Elektrik" })).StatusCode);
        var r = await c.PostAsJsonAsync("/api/islemler/toplu", new
        {
            yeniCarileriEkle = true,
            satirlar = new[]
            {
                Satir("2026-09-05", "Yepyeni Ltd. Şti.", 10m),
                Satir("2026-09-06", "YEPYENİ LTD. ŞTİ.", 20m),
                Satir("2026-09-06", "Toplu Elektrik", 30m, tip: "SabitGider"), // kalem: cari eklenmez
            },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var s = (await r.Content.ReadFromJsonAsync<TopluYanit>())!;
        Assert.Equal(["Yepyeni Ltd. Şti."], s.YeniCariler);
        Assert.Equal(["Yepyeni Ltd. Şti.", "Yepyeni Ltd. Şti.", "Toplu Elektrik"], s.Islemler.Select(i => i.Cari));
        var cariler = (await c.GetFromJsonAsync<List<JsonElement>>("/api/cariler?ara=yepyeni"))!;
        Assert.Single(cariler);
        Assert.Equal(0, Say(db => db.Cariler.Where(x => x.Ad == "Toplu Elektrik")));

        // Olmayan kalem cari olarak eklenmez: hata verir.
        var r2 = await c.PostAsJsonAsync("/api/islemler/toplu", new
        {
            yeniCarileriEkle = true,
            satirlar = new[] { Satir("2026-09-06", "Olmayan Kalem", 30m, tip: "SabitGider") },
        });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Contains("sabit gider kalemi yok", (await r2.Content.ReadFromJsonAsync<TopluHataYanit>())!.Satirlar[0].Hata);
    }

    [Fact]
    public async Task Toplu_yukleme_bos_cok_buyuk_ve_yetkisiz_istekleri_reddeder()
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/islemler/toplu", new { satirlar = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/islemler/toplu", new { satirlar = (object[]?)null })).StatusCode);
        var cok = Enumerable.Range(0, 1001).Select(_ => Satir("2026-09-01", "X", 1m)).ToArray();
        var r = await c.PostAsJsonAsync("/api/islemler/toplu", new { satirlar = cok });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("1000", (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata);

        var govde = new { satirlar = new[] { Satir("2026-09-01", "X", 1m) } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/islemler/toplu", govde)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await IzleyiciAsync()).PostAsJsonAsync("/api/islemler/toplu", govde)).StatusCode);
    }

    [Fact]
    public async Task Toplu_yukleme_tek_tek_eklemeyle_ayni_rakamlari_verir()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Rakam Aynı");
        var satirlar = new[]
        {
            Satir("2026-09-22", "Rakam Aynı", 123.45m, kanal: "MEZAT"),
            Satir("2026-09-23", "Rakam Aynı", 10m, kanal: "PERAKENDE"),
            Satir("2026-09-24", "Rakam Aynı", 0.55m, kanal: "Ortak"),
        };
        var once = (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!;
        var r = await c.PostAsJsonAsync("/api/islemler/toplu", new { satirlar });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var toplu = (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!;
        foreach (var i in (await r.Content.ReadFromJsonAsync<TopluYanit>())!.Islemler)
            Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{i.Id}")).StatusCode);
        Assert.Equal(once, (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!);

        await IslemEkle(c, "2026-09-22", "Rakam Aynı", 123.45m, kanal: "MEZAT");
        await IslemEkle(c, "2026-09-23", "Rakam Aynı", 10m, kanal: "PERAKENDE");
        await IslemEkle(c, "2026-09-24", "Rakam Aynı", 0.55m, kanal: "Ortak");
        Assert.Equal(toplu, (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!);
    }

    // ---------- 15: Cariye göre öneri ----------

    [Fact]
    public async Task Son_islem_onerisi_carinin_en_son_kanalini_ve_tipini_verir()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Öneri Ticaret");
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync("/api/islemler/son?cari=" + Uri.EscapeDataString("Öneri Ticaret"))).StatusCode);

        await IslemEkle(c, "2026-09-01", "Öneri Ticaret", 10m, kanal: "MEZAT");
        await IslemEkle(c, "2026-09-10", "Öneri Ticaret", 20m, kanal: "TOPTAN");
        await IslemEkle(c, "2026-09-05", "Öneri Ticaret", 30m, kanal: "PERAKENDE");
        var o = (await c.GetFromJsonAsync<OneriYanit>("/api/islemler/son?cari=" + Uri.EscapeDataString("öneri ticaret")))!;
        Assert.Equal("Öneri Ticaret", o.Cari);
        Assert.Equal("TOPTAN", o.Kanal);
        Assert.Equal("Cari", o.Tip);
        Assert.Equal(20m, o.TutarTl);

        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Öneri SGK" })).StatusCode);
        await IslemEkle(c, "2026-09-02", "Öneri SGK", 900m, kanal: "Ortak", tip: "SabitGider");
        var k = (await c.GetFromJsonAsync<OneriYanit>("/api/islemler/son?cari=" + Uri.EscapeDataString("ÖNERİ SGK")))!;
        Assert.Equal(("Ortak", "SabitGider"), (k.Kanal, k.Tip));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler/son?cari=%20")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await IzleyiciAsync()).GetAsync("/api/islemler/son?cari=" + Uri.EscapeDataString("Öneri Ticaret"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/islemler/son?cari=X")).StatusCode);
    }

    // ---------- 06: Gelişmiş işlem arama ----------

    [Fact]
    public async Task Gelismis_arama_not_tip_kart_ve_tutar_suzer_sayfalama_ayni_kalir()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Arama Özel Ltd");
        var kart = await (await c.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Arama Kart", kesimTarihi = "2026-09-10", sonOdemeTarihi = "2026-09-20", limit = 10_000m, borc = 0m,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var kartId = kart.GetProperty("id").GetInt32();
        var a = await IslemEkle(c, "2026-03-01", "Arama Özel Ltd", 100m, not: "İADE faturası");
        var b = await IslemEkle(c, "2026-03-02", "Arama Özel Ltd", 250m, not: "nakliye");
        var k1 = await IslemEkle(c, "2026-03-03", "Arama Özel Ltd", 250m, krediKartiId: kartId, not: "iade kart");
        var d = await IslemEkle(c, "2026-03-04", "Arama Özel Ltd", 1_000m);
        const string Taban = "/api/islemler?cari=Arama%20%C3%96zel&baslangic=2026-03-01&bitis=2026-03-31";

        async Task<List<int>> Idler(string ek) => (await c.GetFromJsonAsync<List<IslemYanit>>(Taban + ek))!.Select(i => i.Id).ToList();

        Assert.Equal([a.Id, b.Id, k1.Id, d.Id], await Idler(""));                         // eski davranış
        Assert.Equal([a.Id, k1.Id], await Idler("&notAra=iade"));                          // Türkçe harf duyarsız (İADE)
        Assert.Equal([k1.Id], await Idler("&tip=KrediKarti"));
        Assert.Equal([a.Id, b.Id, d.Id], await Idler("&tip=cari"));
        Assert.Equal([k1.Id], await Idler($"&kartId={kartId}"));
        Assert.Equal([b.Id, k1.Id], await Idler("&minTutar=250&maxTutar=250"));           // sınırlar dahil
        Assert.Equal([b.Id, k1.Id, d.Id], await Idler("&minTutar=200.5"));
        Assert.Equal([a.Id], await Idler("&maxTutar=100"));

        // Sayfalama ve toplam başlığı süzgeçten sonra.
        var r = await c.GetAsync(Taban + "&minTutar=200&limit=1&offset=1");
        Assert.Equal("3", r.Headers.GetValues("X-Toplam-Kayit").Single());
        Assert.Equal([k1.Id], (await r.Content.ReadFromJsonAsync<List<IslemYanit>>())!.Select(i => i.Id));
        // Cari süzgeci olmadan (SQL yolu) tip + tarih.
        var r2 = await c.GetAsync("/api/islemler?baslangic=2026-03-03&bitis=2026-03-03&tip=KrediKarti&limit=10");
        Assert.Equal("1", r2.Headers.GetValues("X-Toplam-Kayit").Single());

        // CSV aynı süzgeci uygular.
        var csv = await c.GetStringAsync(Taban.Replace("/api/islemler?", "/api/disaaktar/islemler.csv?") + "&notAra=nakliye");
        Assert.Contains("nakliye", csv);
        Assert.DoesNotContain("İADE", csv);
    }

    [Theory]
    [InlineData("tip=Havale", "Geçersiz gider tipi")]
    [InlineData("minTutar=-1", "negatif")]
    [InlineData("minTutar=10&maxTutar=5", "En az tutar")]
    public async Task Gelismis_arama_gecersiz_suzgeci_reddeder(string sorgu, string beklenen)
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.GetAsync("/api/islemler?" + sorgu);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(beklenen, (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/islemler.csv?" + sorgu)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler?notAra=" + new string('a', 201))).StatusCode);
    }

    // ---------- 32: Son silme (Geri al şeridi) ----------

    [Fact]
    public async Task Son_silme_geri_alinacak_gecmis_satirini_bulur()
    {
        var c = await _factory.EditorClientAsync();
        await CariEkle(c, "Silinecek Cari");
        var i = await IslemEkle(c, "2026-09-24", "Silinecek Cari", 42m);
        var yol = $"/api/gecmis/son-silme?tur={Uri.EscapeDataString(GecmisTurleri.Islem)}&kayitId={i.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(yol)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{i.Id}")).StatusCode);
        var s = (await c.GetFromJsonAsync<SilmeYanit>(yol))!;
        Assert.Equal(("Silindi", GecmisTurleri.Islem, i.Id, true, false), (s.Eylem, s.Tur, s.KayitId!.Value, s.GeriAlinabilir, s.GeriAlindi));

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/gecmis/{s.Id}/geri-al", null)).StatusCode);
        var sonra = (await c.GetFromJsonAsync<SilmeYanit>(yol))!;
        Assert.True(sonra.GeriAlindi);
        Assert.False(sonra.GeriAlinabilir);

        // Kanal silmesi geri alınamaz: satır bulunur ama GeriAlinabilir=false.
        var k = await (await c.PostAsJsonAsync("/api/kanallar", new { ad = "SİLİNECEK KANAL", aktif = true, sira = 9 })).Content.ReadFromJsonAsync<JsonElement>();
        var kid = k.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kanallar/{kid}")).StatusCode);
        var ks = (await c.GetFromJsonAsync<SilmeYanit>($"/api/gecmis/son-silme?tur={Uri.EscapeDataString(GecmisTurleri.Kanal)}&kayitId={kid}"))!;
        Assert.False(ks.GeriAlinabilir);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gecmis/son-silme?kayitId=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gecmis/son-silme?tur=Cari")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(yol)).StatusCode);
    }
}
