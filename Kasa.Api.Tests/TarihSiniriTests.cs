using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Girdi tarih sınırları. host-auth-4: kayıt tarihi 01.01.2000 ile kasa gününün bir yıl sonrası arasında olmalı, rapor
/// yılı da aynı pencerenin yıllarıyla sınırlı; daha geniş tarih raporların dönem ufkunu yüz binlerce döneme uzatır.
/// gap-tarihsel-spec-ve-emekli-web-4 / gap-veri-degismezleri-patlama-yaricapi-2: takip başlangıcından önceki tarihe
/// yeni genel gider girilemez (haftalık kasada yok, aylık raporda var olurdu); mevcut eski kayıt ve raporları değişmez.
/// Bütün testler sabit kasa günüyle koşar.
/// </summary>
public class TarihSiniriTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun; // 25.09.2026

    [Fact]
    public void Kayit_tarihi_penceresi_2000_basindan_bugunun_bir_yil_sonrasina_kadardir()
    {
        Assert.Equal(new DateOnly(2000, 1, 1), GirdiDogrulama.EnErkenTarih);
        Assert.Equal(new DateOnly(2027, 9, 25), GirdiDogrulama.EnGecTarih(Bugun));
        Assert.True(GirdiDogrulama.GecerliKayitTarihi(new DateOnly(2000, 1, 1), Bugun));
        Assert.True(GirdiDogrulama.GecerliKayitTarihi(new DateOnly(2027, 9, 25), Bugun));
        Assert.False(GirdiDogrulama.GecerliKayitTarihi(new DateOnly(1999, 12, 31), Bugun));
        Assert.False(GirdiDogrulama.GecerliKayitTarihi(new DateOnly(2027, 9, 26), Bugun));
        Assert.False(GirdiDogrulama.GecerliKayitTarihi(default, Bugun));
        // Artık gün: 29.02'den bir yıl sonrası 28.02'dir.
        Assert.Equal(new DateOnly(2029, 2, 28), GirdiDogrulama.EnGecTarih(new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public async Task Gider_tarihi_bugunden_bir_yil_sonrasini_asamaz_sinir_dahildir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        // host-auth-4 (b): yazım hatalı gelecek tarih (9026) haftalık raporun ufkunu kalıcı olarak uzatırdı.
        foreach (var tarih in new[] { Bugun.AddYears(1).AddDays(1), new DateOnly(9026, 9, 25), new DateOnly(9998, 12, 31) })
        {
            var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, "Yazım hatası", 10m, "MEZAT", GiderTipi.Cari));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("Tarih 01.01.2000 ile 25.09.2027 arasında olmalıdır.", await AlanHatasi(r, "tarih"));
        }
        var varsayilan = await c.PostAsJsonAsync("/api/islemler", new { tarih = "0001-01-01", cari = "Tarihsiz", tutarTl = 10m, kanal = "MEZAT", tip = "Cari" });
        Assert.Equal("Geçerli bir tarih seçin.", await AlanHatasi(varsayilan, "tarih"));
        using (var scope = f.Services.CreateScope())
            Assert.Empty(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Islemler);

        // Sınır dahil: planlı ileri tarihli gider bir yıl içinde girilebilir; dönemler de bu tarihte biter.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun.AddYears(1), "Yıllık sigorta", 10m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        var donemler = (await c.GetFromJsonAsync<JsonElement>("/api/donemler")).EnumerateArray().ToList();
        Assert.Equal(Bugun.AddYears(1), donemler.Max(d => DateOnly.Parse(d.GetProperty("end").GetString()!)));
    }

    [Fact]
    public async Task Ayni_pencere_takip_baslangici_gelir_donemi_ve_alis_tarihinde_uygulanir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        // host-auth-4 (c): kurulumda 0026 yazılan takip başlangıcı ilk hareketten sonra düzeltilemezdi.
        foreach (var tarih in new[] { "0026-09-01", "1999-12-31", "2027-09-26" })
        {
            var r = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = tarih, kasaAcilisDevri = 0m });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("Tarih 01.01.2000 ile 25.09.2027 arasında olmalıdır.", await AlanHatasi(r, "takipBaslangic"));
        }
        Assert.Equal(Bugun, DateOnly.Parse((await c.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("takipBaslangic").GetString()!));

        var gelir = await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(new DateOnly(2030, 1, 1), "MEZAT", 100m));
        Assert.Equal(HttpStatusCode.BadRequest, gelir.StatusCode);
        Assert.Equal("Tarih 01.01.2000 ile 25.09.2027 arasında olmalıdır.", await AlanHatasi(gelir, "donemStart"));

        var alis = await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, new DateOnly(2062, 9, 25), "Satıcı", null, [new("Mal", 100m, [new(1, 100m)])]));
        Assert.Equal(HttpStatusCode.BadRequest, alis.StatusCode);
        Assert.Equal("Tarih 01.01.2000 ile 25.09.2027 arasında olmalıdır.", await AlanHatasi(alis, "tarih"));
    }

    [Fact]
    public async Task Aylik_rapor_ve_aylik_gider_yili_makul_aralikla_alan_bazli_sinirlanir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        // host-auth-4 (a): 9998 yılı yaklaşık 500 bin dönem ürettirirdi.
        foreach (var (yil, ay, alan, ileti) in new[]
        {
            (9998, 12, "yil", "Yıl 2000 ile 2027 arasında olmalıdır."),
            (1999, 12, "yil", "Yıl 2000 ile 2027 arasında olmalıdır."),
            (2028, 1, "yil", "Yıl 2000 ile 2027 arasında olmalıdır."),
            (2026, 13, "ay", "Ay 1 ile 12 arasında olmalıdır."),
            (2026, 0, "ay", "Ay 1 ile 12 arasında olmalıdır."),
        })
        {
            foreach (var yol in new[] { "/api/rapor/aylik", "/api/aylik-giderler" })
            {
                var r = await c.GetAsync($"{yol}?yil={yil}&ay={ay}");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Equal(ileti, await AlanHatasi(r, alan));
            }
        }
        foreach (var (yil, ay) in new[] { (2000, 1), (2027, 12), (Bugun.Year, Bugun.Month) })
        {
            (await c.GetAsync($"/api/rapor/aylik?yil={yil}&ay={ay}")).EnsureSuccessStatusCode();
            (await c.GetAsync($"/api/aylik-giderler?yil={yil}&ay={ay}")).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Aylik_gider_sablonunun_gecerlilik_ayi_ileri_pencereyi_asamaz()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var ileri = await c.PostAsJsonAsync("/api/aylik-giderler/sablonlar", Sablon(new DateOnly(2027, 10, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, ileri.StatusCode);
        Assert.Equal("Geçerlilik cari ay ile 09.2027 arasında bir ayın ilk günü olmalı.", (await ileri.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
        (await c.PostAsJsonAsync("/api/aylik-giderler/sablonlar", Sablon(new DateOnly(2027, 9, 1)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Takip_baslangicindan_once_yeni_gider_girilemez_mevcut_eski_kayit_ve_raporlari_degismez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var baslangic = new DateOnly(2026, 6, 15);
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        // Kural öncesinden kalan kayıt (canlı veride olabilir): takip başlangıcından önce tarihli nakit gider.
        int eskiId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var mezat = db.Kanallar.Single(k => k.Ad == "MEZAT");
            var eski = new IslemEntity { Tarih = new DateOnly(2026, 6, 10), Cari = "Eski fatura", TutarTl = 5000m, Kanal = mezat.Ad, KanalId = mezat.Id, Tip = GiderTipi.Cari };
            db.Islemler.Add(eski);
            db.SaveChanges();
            eskiId = eski.Id;
        }
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new DateOnly(2026, 7, 1), "Temmuz gideri", 300m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        var raporlar = new[] { "/api/rapor/aylik?yil=2026&ay=6", "/api/rapor/aylik?yil=2026&ay=7", "/api/rapor/haftalik", "/api/rapor/panel", "/api/donemler" };
        var once = await Oku(c, raporlar);

        const string ileti = "Gider tarihi takip başlangıcından (15.06.2026) önce olamaz.";
        foreach (var yeni in new[]
        {
            new IslemYazDto(new DateOnly(2026, 6, 10), "MEZAT faturası", 5000m, "MEZAT", GiderTipi.Cari),
            new IslemYazDto(new DateOnly(2026, 6, 14), "Kira", 100m, Kanallar.Ortak, GiderTipi.SabitGider),
            new IslemYazDto(new DateOnly(2026, 5, 20), "Eski kart", 100m, "MEZAT", GiderTipi.KrediKarti),
            new IslemYazDto(new DateOnly(2025, 6, 15), "Yıl hatası", 100m, "MEZAT", GiderTipi.Cari),
        })
        {
            var r = await c.PostAsJsonAsync("/api/islemler", yeni);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(ileti, await AlanHatasi(r, "tarih"));
        }
        // Mevcut eski kayıt tarihi değişmeden düzenlenebilir; tarihi başlangıç öncesinde başka bir güne taşınamaz.
        var eskiYaz = new IslemYazDto(new DateOnly(2026, 6, 10), "Eski fatura", 5000m, "MEZAT", GiderTipi.Cari, Not: "Fatura no 12");
        (await c.PutAsJsonAsync($"/api/islemler/{eskiId}", eskiYaz)).EnsureSuccessStatusCode();
        var tasima = await c.PutAsJsonAsync($"/api/islemler/{eskiId}", eskiYaz with { Tarih = new DateOnly(2026, 6, 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tasima.StatusCode);
        Assert.Equal(ileti, await AlanHatasi(tasima, "tarih"));
        // Takip içindeki kayıt da başlangıç öncesine çekilemez.
        var temmuz = (await c.GetFromJsonAsync<JsonElement>("/api/islemler")).EnumerateArray().Single(i => i.GetProperty("cari").GetString() == "Temmuz gideri").GetProperty("id").GetInt32();
        var geriCekme = await c.PutAsJsonAsync($"/api/islemler/{temmuz}", new IslemYazDto(new DateOnly(2026, 6, 14), "Temmuz gideri", 300m, "MEZAT", GiderTipi.Cari));
        Assert.Equal(ileti, await AlanHatasi(geriCekme, "tarih"));

        // Reddedilen girişler ve eski kaydın not düzeltmesi hiçbir raporu değiştirmez; eski kayıt listede kalır.
        Assert.Equal(once, await Oku(c, raporlar));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var eski = db.Islemler.AsNoTracking().Single(i => i.Id == eskiId);
            Assert.Equal((new DateOnly(2026, 6, 10), 5000m, "Fatura no 12"), (eski.Tarih, eski.TutarTl, eski.Not));
            Assert.Equal(2, db.Islemler.Count());
        }
        // Takip başlangıcı günü kabul edilir.
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(baslangic, "İlk gün", 10m, "MEZAT", GiderTipi.Cari))).StatusCode);
    }

    [Fact]
    public async Task Ilk_acilis_takip_baslangici_kasa_saatinin_Istanbul_gunudur()
    {
        // 09.03.2026 22:30 UTC İstanbul'da 10.03.2026 01:30'dur; makinenin yerel günü ve UTC günü değil, kasa günü tohumlanır.
        var an = new DateTimeOffset(2026, 3, 9, 22, 30, 0, TimeSpan.Zero);
        await using var f = new TohumsuzSaatliFabrika(new SabitSaat(an));
        using var c = await f.EditorClientAsync();
        Assert.Equal(new DateOnly(2026, 3, 10), DateOnly.Parse((await c.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("takipBaslangic").GetString()!));
    }

    /// <summary>
    /// Takip başlangıcını veritabanında doğrudan ayarlar. Sabit geçmiş tarihli eski model (eski kart) senaryoları genel
    /// gideri takip başlangıcından sonraya yazmalıdır; paylaşılan fikstürde veri varken PUT /api/ayarlar 409 döner.
    /// </summary>
    internal static void TakipBaslangiciAyarla(KasaWebFactory f, DateOnly gun)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Ayarlar.First().TakipBaslangic = gun;
        db.SaveChanges();
    }

    private static AylikGiderSablonYaz Sablon(DateOnly gecerliAy) => new(Guid.NewGuid(), 0, "Kira", "Kira", 100m, 1, "Genel", [], gecerliAy);

    private static async Task<List<string>> Oku(HttpClient c, IEnumerable<string> yollar)
    {
        var sonuc = new List<string>();
        foreach (var yol in yollar)
            sonuc.Add(await c.GetStringAsync(yol));
        return sonuc;
    }

    private static async Task<string> AlanHatasi(HttpResponseMessage r, string alan)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty(alan)[0].GetString()!;
    }

    /// <summary>Ayar satırını önceden yazmayan sabit saatli sunucu: ilk açılış tohumunu Program.cs yapar.</summary>
    private sealed class TohumsuzSaatliFabrika(TimeProvider saat) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton(saat); });
        }
    }
}
