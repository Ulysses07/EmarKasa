using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>Tekrarlayan gider takvimi (saf): vade, başlangıç ayı, iki ay geriye bakış, kararlar.</summary>
public class TekrarlayanTakvimTests
{
    private static TekrarlayanSablon Sablon(int id, int gun, DateOnly baslangic, bool aktif = true, string kalem = "Kira")
        => new(id, kalem, "Ortak", 100m, gun, aktif, baslangic);

    private static readonly HashSet<(int, DateOnly)> KararYok = new();

    [Theory]
    [InlineData(2026, 2, 31, 28)]   // Şubat (artık yıl değil) → ayın son günü
    [InlineData(2028, 2, 31, 29)]   // artık yıl
    [InlineData(2028, 2, 30, 29)]
    [InlineData(2026, 4, 31, 30)]   // 30 çeken ay
    [InlineData(2026, 1, 31, 31)]
    [InlineData(2026, 2, 5, 5)]
    public void Vade_kisa_ayda_ayin_son_gunune_duser(int yil, int ay, int gun, int beklenen)
        => Assert.Equal(new DateOnly(yil, ay, beklenen), TekrarlayanTakvim.Vade(new DateOnly(yil, ay, 1), gun));

    [Fact]
    public void Ayin_31i_subatta_28inde_vadesi_gelir()
    {
        var s = new[] { Sablon(1, 31, new DateOnly(2026, 2, 1)) };
        Assert.Empty(TekrarlayanTakvim.Bekleyenler(s, KararYok, new DateOnly(2026, 2, 27)));
        var b = Assert.Single(TekrarlayanTakvim.Bekleyenler(s, KararYok, new DateOnly(2026, 2, 28)));
        Assert.Equal(new DateOnly(2026, 2, 1), b.Ay);
        Assert.Equal(new DateOnly(2026, 2, 28), b.Vade);
    }

    [Fact]
    public void Bu_ay_ve_onceki_iki_ay_listelenir_daha_eskisi_listelenmez()
    {
        var s = new[] { Sablon(1, 5, new DateOnly(2026, 1, 1)) };
        var l = TekrarlayanTakvim.Bekleyenler(s, KararYok, new DateOnly(2026, 9, 24));
        Assert.Equal(new[] { new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1) }, l.Select(b => b.Ay));
        Assert.Equal(new DateOnly(2026, 7, 5), l[0].Vade);
    }

    [Fact]
    public void Baslangic_ayindan_once_ve_vadesi_gelmemis_ay_listelenmez()
    {
        var bugun = new DateOnly(2026, 9, 24);
        Assert.Equal(new[] { new DateOnly(2026, 9, 1) },
            TekrarlayanTakvim.Bekleyenler(new[] { Sablon(1, 5, new DateOnly(2026, 9, 1)) }, KararYok, bugun).Select(b => b.Ay));
        // Günü 25: bu ayın vadesi yarın → yalnız önceki iki ay.
        Assert.Equal(new[] { new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1) },
            TekrarlayanTakvim.Bekleyenler(new[] { Sablon(1, 25, new DateOnly(2025, 1, 1)) }, KararYok, bugun).Select(b => b.Ay));
        // Başlangıç ayı gelecekte → hiçbir şey.
        Assert.Empty(TekrarlayanTakvim.Bekleyenler(new[] { Sablon(1, 1, new DateOnly(2026, 10, 1)) }, KararYok, bugun));
        // Başlangıç ayı ayın 1'i değilse de ay olarak ele alınır.
        Assert.Single(TekrarlayanTakvim.Bekleyenler(new[] { Sablon(1, 5, new DateOnly(2026, 9, 20)) }, KararYok, bugun));
    }

    [Fact]
    public void Karar_verilmis_ay_ve_pasif_sablon_listelenmez_vadeye_gore_sirali()
    {
        var bugun = new DateOnly(2026, 9, 24);
        var s = new[]
        {
            Sablon(1, 20, new DateOnly(2026, 8, 1), kalem: "SGK"),
            Sablon(2, 3, new DateOnly(2026, 9, 1), kalem: "Kira"),
            Sablon(3, 1, new DateOnly(2026, 1, 1), aktif: false),
        };
        var kararlar = new HashSet<(int, DateOnly)> { (1, new DateOnly(2026, 8, 1)) };
        var l = TekrarlayanTakvim.Bekleyenler(s, kararlar, bugun);
        Assert.Equal(new[] { (2, new DateOnly(2026, 9, 3)), (1, new DateOnly(2026, 9, 20)) },
            l.Select(b => (b.TekrarlayanGiderId, b.Vade)));
    }
}

/// <summary>Tekrarlayan gider API'si: CRUD, bekleyen, onayla/atla, kalem/kanal ad değişimi, yetki.</summary>
public class TekrarlayanGiderTests : IClassFixture<TekrarlayanGiderTests.AyarliSaatFactory>
{
    /// <summary>Testin ayarladığı gün (Türkiye saatiyle öğlen).</summary>
    public sealed class AyarliSaat : TimeProvider
    {
        private DateTimeOffset _simdi = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        public void Ayarla(DateOnly gun) => _simdi = new DateTimeOffset(gun.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _simdi;
    }

    public class AyarliSaatFactory : KasaWebFactory
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

    private record Sablon(int Id, string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif, DateOnly BaslangicAyi);
    private record Bekleyen(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade);
    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);
    private record Karar(int Id, int TekrarlayanGiderId, DateOnly Ay, string Durum, int? IslemId);
    private record Kalem(int Id, string Ad, bool Aktif);
    private record HataYanit(string Hata);

    private readonly AyarliSaatFactory _factory;
    public TekrarlayanGiderTests(AyarliSaatFactory factory) => _factory = factory;

    private static string Ad(string kok) => kok + " " + Guid.NewGuid().ToString("N")[..6];

    private static async Task<Kalem> KalemEkle(HttpClient c, string ad)
    {
        var r = await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Kalem>(Json))!;
    }

    private static Task<HttpResponseMessage> SablonYaz(HttpClient c, string kalem, int gun, string baslangic,
        string kanal = "MEZAT", decimal tutar = 1_000m, bool aktif = true)
        => c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem, kanal, tutar, ayinGunu = gun, aktif, baslangicAyi = baslangic });

    private static async Task<Sablon> SablonEkle(HttpClient c, string kalem, int gun, string baslangic,
        string kanal = "MEZAT", decimal tutar = 1_000m)
    {
        var r = await SablonYaz(c, kalem, gun, baslangic, kanal, tutar);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Sablon>(Json))!;
    }

    private static async Task<List<Bekleyen>> Bekleyenler(HttpClient c, int sablonId)
        => (await c.GetFromJsonAsync<List<Bekleyen>>("/api/tekrarlayangiderler/bekleyen", Json))!
            .Where(b => b.TekrarlayanGiderId == sablonId).ToList();

    private async Task<HttpClient> EditorAsync(DateOnly bugun)
    {
        _factory.Saat.Ayarla(bugun);
        return await _factory.EditorClientAsync();
    }

    [Fact]
    public async Task Onayla_sabit_gider_islemi_olusturur_bekleyenden_duser_ikinci_karar_409()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Kira"));
        // Kalem büyük/küçük harf farkıyla yazılsa da kayıtlı yazım saklanır.
        var s = await SablonEkle(c, kalem.Ad.ToUpper(Metin.Tr), 5, "2026-09-01", tutar: 15_000m);
        Assert.Equal(kalem.Ad, s.Kalem);

        var b = Assert.Single(await Bekleyenler(c, s.Id));
        Assert.Equal(new DateOnly(2026, 9, 1), b.Ay);
        Assert.Equal(new DateOnly(2026, 9, 5), b.Vade);
        Assert.Equal(15_000m, b.Tutar);
        Assert.Equal("MEZAT", b.Kanal);

        var onay = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla",
            new { ay = "2026-09-01", tarih = "2026-09-05", tutar = 15_250.50m });
        Assert.Equal(HttpStatusCode.Created, onay.StatusCode);
        var islem = (await onay.Content.ReadFromJsonAsync<IslemYanit>(Json))!;
        Assert.Equal(new IslemYanit(islem.Id, new DateOnly(2026, 9, 5), kalem.Ad, 15_250.50m, "MEZAT", GiderTipi.SabitGider, "Tekrarlayan gider"), islem);

        var islemler = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json);
        Assert.Contains(islemler!, i => i.Id == islem.Id && i.Cari == kalem.Ad);
        Assert.Empty(await Bekleyenler(c, s.Id));

        var ikinci = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-09-01" });
        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);
        Assert.Contains("girildi", (await ikinci.Content.ReadFromJsonAsync<HataYanit>(Json))!.Hata);
        Assert.Equal(HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/atla", new { ay = "2026-09-01" })).StatusCode);
        // İkinci onay işlem üretmedi.
        Assert.Single((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json))!, i => i.Cari == kalem.Ad);

        // Karar, işlemle bağlı kaydedildi; işlem silinirse bağ kopar ama karar kalır (ay yeniden açılmaz).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var g = db.TekrarlayanGirisler.AsNoTracking().Single(x => x.TekrarlayanGiderId == s.Id);
            Assert.Equal(TekrarlayanDurum.Girildi, g.Durum);
            Assert.Equal(islem.Id, g.IslemId);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{islem.Id}")).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Null(db.TekrarlayanGirisler.AsNoTracking().Single(x => x.TekrarlayanGiderId == s.Id).IslemId);
        }
        Assert.Empty(await Bekleyenler(c, s.Id));
    }

    [Fact]
    public async Task Onayla_tarih_ve_tutar_verilmezse_vade_ve_sablon_tutari_kullanilir()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("SGK"));
        var s = await SablonEkle(c, kalem.Ad, 15, "2026-08-01", kanal: "Ortak", tutar: 7_500m);

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-08-01" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var i = (await r.Content.ReadFromJsonAsync<IslemYanit>(Json))!;
        Assert.Equal((new DateOnly(2026, 8, 15), 7_500m, "Ortak"), (i.Tarih, i.TutarTl, i.Kanal));
        // Ağustos girildi; Eylül hâlâ bekliyor.
        Assert.Equal(new[] { new DateOnly(2026, 9, 1) }, (await Bekleyenler(c, s.Id)).Select(b => b.Ay));
    }

    [Fact]
    public async Task Onay_dogrulamadan_gecmezse_hicbir_sey_kaydedilmez()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Vergi"));
        var s = await SablonEkle(c, kalem.Ad, 1, "2026-09-01");

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-09-01", tutar = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Single(await Bekleyenler(c, s.Id));
        Assert.DoesNotContain((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json))!, i => i.Cari == kalem.Ad);
    }

    [Fact]
    public async Task Atla_karar_kaydeder_islem_olusturmaz_ikinci_kez_409()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Aidat"));
        var s = await SablonEkle(c, kalem.Ad, 10, "2026-09-01");

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/atla", new { ay = "2026-09-01" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var k = (await r.Content.ReadFromJsonAsync<Karar>(Json))!;
        Assert.Equal(("Atlandi", (int?)null, new DateOnly(2026, 9, 1)), (k.Durum, k.IslemId, k.Ay));
        Assert.Empty(await Bekleyenler(c, s.Id));
        Assert.DoesNotContain((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json))!, i => i.Cari == kalem.Ad);

        var ikinci = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/atla", new { ay = "2026-09-15" });   // aynı ay
        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);
        Assert.Contains("atlandı", (await ikinci.Content.ReadFromJsonAsync<HataYanit>(Json))!.Hata);
        Assert.Equal(HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-09-01" })).StatusCode);
    }

    [Fact]
    public async Task Ayin_31i_subatta_28inde_bekleyen_olur()
    {
        var c = await EditorAsync(new DateOnly(2026, 2, 27));
        var kalem = await KalemEkle(c, Ad("Maaş"));
        var s = await SablonEkle(c, kalem.Ad, 31, "2026-02-01");
        Assert.Empty(await Bekleyenler(c, s.Id));

        _factory.Saat.Ayarla(new DateOnly(2026, 2, 28));
        var b = Assert.Single(await Bekleyenler(c, s.Id));
        Assert.Equal(new DateOnly(2026, 2, 28), b.Vade);

        var r = await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-02-01" });
        Assert.Equal(new DateOnly(2026, 2, 28), (await r.Content.ReadFromJsonAsync<IslemYanit>(Json))!.Tarih);
    }

    [Fact]
    public async Task Iki_aydan_eski_aylar_ve_baslangic_oncesi_listelenmez_karar_verilemez()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Elektrik"));
        var eski = await SablonEkle(c, kalem.Ad, 1, "2026-03-01");
        var yeni = await SablonEkle(c, kalem.Ad, 1, "2026-09-01", kanal: "Ortak");

        Assert.Equal(new[] { new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1) },
            (await Bekleyenler(c, eski.Id)).Select(b => b.Ay));
        Assert.Equal(new[] { new DateOnly(2026, 9, 1) }, (await Bekleyenler(c, yeni.Id)).Select(b => b.Ay));

        // Başlangıç ayından önce ya da ileri bir ay için karar yok.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{yeni.Id}/atla", new { ay = "2026-08-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{yeni.Id}/onayla", new { ay = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PostAsJsonAsync("/api/tekrarlayangiderler/999999/atla", new { ay = "2026-09-01" })).StatusCode);

        // Pasif şablon beklemez.
        (await c.PutAsJsonAsync($"/api/tekrarlayangiderler/{eski.Id}",
            new { kalem = kalem.Ad, kanal = "MEZAT", tutar = 1_000m, ayinGunu = 1, aktif = false })).EnsureSuccessStatusCode();
        Assert.Empty(await Bekleyenler(c, eski.Id));
        // PUT'ta başlangıç ayı verilmezse eskisi korunur.
        var liste = await c.GetFromJsonAsync<List<Sablon>>("/api/tekrarlayangiderler", Json);
        Assert.Equal(new DateOnly(2026, 3, 1), liste!.Single(x => x.Id == eski.Id).BaslangicAyi);
    }

    [Fact]
    public async Task Gecersiz_sablon_400()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Kira"));

        var yokKalem = await SablonYaz(c, Ad("Olmayan kalem"), 5, "2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, yokKalem.StatusCode);
        Assert.Contains("sabit gider kalemi yok", (await yokKalem.Content.ReadFromJsonAsync<HataYanit>(Json))!.Hata);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, "", 5, "2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, kalem.Ad, 5, "2026-09-01", kanal: "YOK KANAL")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, kalem.Ad, 0, "2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, kalem.Ad, 32, "2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, kalem.Ad, 5, "2026-09-01", tutar: 0m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SablonYaz(c, kalem.Ad, 5, "2026-09-01", tutar: 1.234m)).StatusCode);

        // Başlangıç ayı verilmezse bu ay; ay ortası verilirse ayın 1'ine çekilir.
        var varsayilan = await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = kalem.Ad, kanal = "Ortak", tutar = 5m, ayinGunu = 1 });
        Assert.Equal(HttpStatusCode.Created, varsayilan.StatusCode);
        var v = (await varsayilan.Content.ReadFromJsonAsync<Sablon>(Json))!;
        Assert.Equal((new DateOnly(2026, 9, 1), true), (v.BaslangicAyi, v.Aktif));
        Assert.Equal(new DateOnly(2026, 11, 1), (await SablonEkle(c, kalem.Ad, 1, "2026-11-17")).BaslangicAyi);
    }

    [Fact]
    public async Task Kalem_adi_degisince_sablon_da_degisir_kullanilan_kalem_silinemez()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Kira"));
        var s = await SablonEkle(c, kalem.Ad, 1, "2026-09-01");

        var yeniAd = Ad("Dükkan kirası");
        (await c.PutAsJsonAsync($"/api/giderkalemleri/{kalem.Id}", new { ad = yeniAd, aktif = true })).EnsureSuccessStatusCode();

        var liste = await c.GetFromJsonAsync<List<Sablon>>("/api/tekrarlayangiderler", Json);
        Assert.Equal(yeniAd, liste!.Single(x => x.Id == s.Id).Kalem);
        Assert.Equal(yeniAd, Assert.Single(await Bekleyenler(c, s.Id)).Kalem);

        var sil = await c.DeleteAsync($"/api/giderkalemleri/{kalem.Id}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
        Assert.Contains("tekrarlayan gider", (await sil.Content.ReadFromJsonAsync<HataYanit>(Json))!.Hata);

        // Şablon silinince kalem (işlemi olmadığından) silinebilir.
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/tekrarlayangiderler/{s.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/giderkalemleri/{kalem.Id}")).StatusCode);
    }

    [Fact]
    public async Task Kanal_adi_degisince_sablon_da_degisir_kullanilan_kanal_silinemez()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Kira"));
        var kanalAd = Ad("ŞUBE");
        var kanal = await (await c.PostAsJsonAsync("/api/kanallar", new { ad = kanalAd, sira = 9 })).Content.ReadFromJsonAsync<Kalem>(Json);
        var s = await SablonEkle(c, kalem.Ad, 1, "2026-09-01", kanal: kanalAd);

        var yeniAd = Ad("ŞUBE 2");
        (await c.PutAsJsonAsync($"/api/kanallar/{kanal!.Id}", new { ad = yeniAd, aktif = true, sira = 9 })).EnsureSuccessStatusCode();
        var liste = await c.GetFromJsonAsync<List<Sablon>>("/api/tekrarlayangiderler", Json);
        Assert.Equal(yeniAd, liste!.Single(x => x.Id == s.Id).Kanal);

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{kanal.Id}")).StatusCode);
        (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-09-01" })).EnsureSuccessStatusCode();
        Assert.Contains((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", Json))!, i => i.Kanal == yeniAd);
    }

    [Fact]
    public async Task Sablon_silinince_kararlari_silinir_islem_kalir()
    {
        var c = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(c, Ad("Su"));
        var s = await SablonEkle(c, kalem.Ad, 1, "2026-08-01");
        var islem = await (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-08-01" }))
            .Content.ReadFromJsonAsync<IslemYanit>(Json);
        (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/atla", new { ay = "2026-09-01" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/tekrarlayangiderler/{s.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/tekrarlayangiderler/{s.Id}")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.False(db.TekrarlayanGirisler.Any(g => g.TekrarlayanGiderId == s.Id));
        Assert.True(db.Islemler.Any(i => i.Id == islem!.Id));
    }

    [Fact]
    public async Task Izleyici_okur_ama_yazamaz()
    {
        var editor = await EditorAsync(new DateOnly(2026, 9, 24));
        var kalem = await KalemEkle(editor, Ad("Kira"));
        var s = await SablonEkle(editor, kalem.Ad, 1, "2026-09-01");
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izle123" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/tekrarlayangiderler")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/tekrarlayangiderler/bekleyen")).StatusCode);
        var govde = new { kalem = kalem.Ad, kanal = "MEZAT", tutar = 5m, ayinGunu = 1, aktif = true, baslangicAyi = "2026-09-01" };
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/tekrarlayangiderler", govde)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}", govde)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/tekrarlayangiderler/{s.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/onayla", new { ay = "2026-09-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync($"/api/tekrarlayangiderler/{s.Id}/atla", new { ay = "2026-09-01" })).StatusCode);
        Assert.Single(await Bekleyenler(editor, s.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/tekrarlayangiderler/bekleyen")).StatusCode);
    }
}

/// <summary>Var olan DB'de tekrarlayan gider tabloları açılışta kendiliğinden kurulur.</summary>
public class TekrarlayanSemaTests
{
    [Fact]
    public void Eksik_tekrarlayan_tablolari_FK_ve_tekil_indexle_eklenir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        foreach (var sql in new[] { "DROP TABLE \"TekrarlayanGirisler\"", "DROP TABLE \"TekrarlayanGiderler\"" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance);

        Assert.Contains("tablo+ TekrarlayanGiderler", yapilan);
        Assert.Contains("tablo+ TekrarlayanGirisler", yapilan);
        List<string> Oku(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var l = new List<string>();
            while (r.Read()) l.Add(r.GetString(0));
            return l;
        }
        var fk = Oku("SELECT \"table\" || ':' || on_delete FROM pragma_foreign_key_list('TekrarlayanGirisler')");
        Assert.Contains("TekrarlayanGiderler:CASCADE", fk);
        Assert.Contains("Islemler:SET NULL", fk);
        Assert.Contains("IX_TekrarlayanGirisler_TekrarlayanGiderId_Ay", Oku("SELECT name FROM pragma_index_list('TekrarlayanGirisler') WHERE \"unique\" = 1"));
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance));
    }
}
