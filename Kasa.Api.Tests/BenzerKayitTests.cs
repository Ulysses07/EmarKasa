using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class BenzerKayitTests
{
    private static DateOnly Date => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Kart_harcamasi_alis_gideri_ile_tek_gosterilir_baska_kart_ve_odeme_karismaz()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        Seed(f, db =>
        {
            db.KrediKartlari.AddRange(Card(1), Card(2)); db.SaveChanges();
            db.TakipKartlar.AddRange(new() { KrediKartiId = 1, Baslangic = Date }, new() { KrediKartiId = 2, Baslangic = Date }); db.SaveChanges();
            db.Islemler.AddRange(Expense(1, 1), Expense(2, 2), Expense(3, 1, 101), Expense(4, 1, 100, Date.AddDays(1)));
            db.SaveChanges();
            db.TakipHarcamalar.AddRange(
                new() { KrediKartiId = 1, IslemId = 1, Tarih = Date, Tutar = 100, Aciklama = "Aynı gider" },
                new() { KrediKartiId = 1, Tarih = Date, Tutar = 100, Aciklama = "Doğrudan harcama" },
                new() { KrediKartiId = 1, Tarih = Date, Tutar = 100, Aciklama = "İptal", Iptal = true });
            db.TakipKartOdemeler.Add(new() { KrediKartiId = 1, Tarih = Date, Tutar = 100 });
            db.SaveChanges();
        });
        var rows = await Find(c, new("KartHarcama", Date, 100, 1));
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.Kaynak == "Islem" && r.Id == 1);
        Assert.Single(rows, r => r.Kaynak == "KartHarcama");
        Assert.DoesNotContain(rows, r => r.Kaynak == "KartOdeme");
        Assert.Empty(await Find(c, new("KartHarcama", Date, -100, 1)));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(4, db.Islemler.Count()); Assert.Equal(3, db.TakipHarcamalar.Count());
        Assert.Empty(db.TakipEkstreler); // Benzerlik kontrolü Sync veya başka yazma yapmaz.
    }

    [Fact]
    public async Task Kart_odeme_uyarisi_ayni_karttaki_aktif_odemelere_bakar()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        Seed(f, db =>
        {
            db.KrediKartlari.AddRange(Card(1), Card(2)); db.SaveChanges();
            db.TakipKartlar.AddRange(new() { KrediKartiId = 1, Baslangic = Date }, new() { KrediKartiId = 2, Baslangic = Date }); db.SaveChanges();
            db.TakipKartOdemeler.AddRange(
                new() { KrediKartiId = 1, Tarih = Date, Tutar = 100 },
                new() { KrediKartiId = 1, Tarih = Date, Tutar = 100, Iptal = true },
                new() { KrediKartiId = 2, Tarih = Date, Tutar = 100 });
            db.KartOdemeler.Add(new() { KrediKartiId = 1, Tarih = Date, Tutar = 100 });
            db.Islemler.Add(Expense(1, 1)); db.SaveChanges();
        });
        var rows = await Find(c, new("KartOdeme", Date, 100, 1));
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.Kaynak == "KartOdeme");
        Assert.Single(rows, r => r.Kaynak == "EskiKartOdeme");
    }

    [Fact]
    public async Task Nakit_benzerligi_kanala_bakar_ve_kullanici_ayri_kayit_olusturabilir()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        var write = new IslemYazDto(Date, "Malzeme", 100, "MEZAT", GiderTipi.Cari);
        (await c.PostAsJsonAsync("/api/islemler", write)).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/islemler", write with { Kanal = "TOPTAN" })).EnsureSuccessStatusCode();
        Assert.Single(await Find(c, new("Gider", Date, 100, Kanal: "MEZAT")));
        Assert.Empty(await Find(c, new("Gider", Date, 100, Kanal: "PERAKENDE")));
        (await c.PostAsJsonAsync("/api/islemler", write)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await Find(c, new("Gider", Date, 100, Kanal: "MEZAT"))).Count);
    }

    [Fact]
    public async Task Alisin_onayli_paylari_nakit_benzerliginde_kullanilir_taslakta_tahmin_yapilmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        var draft = await Purchase(c);
        var paid = await Post<AlisDto>(c, $"/api/alis/{draft.Id}/odemeler", new AlisOdemeYaz(draft.Surum, Guid.NewGuid(), Date, 100));
        Assert.Single(await Find(c, new("AlisOdeme", Date, 100, AlisId: draft.Id)));
        Assert.Empty(await Find(c, new("Gider", Date, 100, Kanal: "MEZAT")));
        var sent = await Post<AlisDto>(c, $"/api/alis/{draft.Id}/gonder", new AlisDurumYaz(paid.Surum));
        await Post<AlisDto>(c, $"/api/alis/{draft.Id}/onayla", new AlisDurumYaz(sent.Surum));
        var rows = await Find(c, new("Gider", Date, 100, Kanal: "MEZAT"));
        Assert.Equal(draft.Id, Assert.Single(rows).AlisId);
        Assert.Empty(await Find(c, new("Gider", Date, 100, Kanal: "TOPTAN")));
    }

    [Fact]
    public async Task Benzerlik_sadece_editore_acik_ve_gecersiz_sorgular_reddedilir()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        using var anonymous = f.CreateClient();
        var valid = new BenzerKayitSorgu("Gider", Date, 100, Kanal: "MEZAT");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/islemler/benzerlik", valid)).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici123" })).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient();
        (await viewer.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/islemler/benzerlik", valid)).StatusCode);
        var buyer = await Post<AliciDto>(c, "/api/alicilar", new AliciYaz("benzer-alici", "Alıcı", "alici12345"));
        using var buyerClient = f.CreateClient();
        (await buyerClient.PostAsJsonAsync("/api/auth/login", new { kullanici = buyer.Kullanici, sifre = "alici12345" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await buyerClient.PostAsJsonAsync("/api/islemler/benzerlik", valid)).StatusCode);
        foreach (var invalid in new[] { valid with { Tur = "Hatalı" }, valid with { Tutar = 1.001m }, valid with { Tarih = default }, valid with { Kanal = "" }, valid with { KrediKartiId = -1 }, valid with { Tur = "KartOdeme" }, valid with { Tur = "AlisOdeme" }, valid with { Kanal = "Yok" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/islemler/benzerlik", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/islemler/benzerlik", valid with { Tur = "AlisOdeme", AlisId = 99999 })).StatusCode);
    }

    [Fact]
    public async Task Alis_satirinda_yalniz_kullanilan_kartin_adi_ve_panelde_kanal_kimligi_doner()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        Seed(f, db => { db.KrediKartlari.AddRange(Card(1), Card(2)); db.SaveChanges(); });
        var draft = await Purchase(c);
        var paid = await Post<AlisDto>(c, $"/api/alis/{draft.Id}/odemeler", new AlisOdemeYaz(draft.Surum, Guid.NewGuid(), Date, 100, 1));
        Assert.Equal("Kart 1", Assert.Single(paid.Odemeler).KrediKartiAdi);
        Seed(f, db => { db.KrediKartlari.Find(1)!.Ad = "Yeni kart adı"; db.SaveChanges(); });
        var list = (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!;
        Assert.Equal("Yeni kart adı", list.Single().Odemeler.Single().KrediKartiAdi);
        Assert.DoesNotContain("Kart 2", await (await c.GetAsync("/api/alis")).Content.ReadAsStringAsync());
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(1, panel.Kanallar.Single(k => k.Kanal == "MEZAT").KanalId);
    }

    [Fact]
    public async Task Uyari_en_fazla_on_eslesen_kaydi_gosterir()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        Seed(f, db => { db.KrediKartlari.Add(Card(1)); db.SaveChanges(); for (var i = 1; i <= 12; i++) db.Islemler.Add(Expense(i, 1)); db.SaveChanges(); });
        Assert.Equal(10, (await Find(c, new("KartHarcama", Date, 100, 1))).Count);
    }

    private static KrediKartiEntity Card(int id) => new() { Id = id, Ad = $"Kart {id}", KesimTarihi = Date, SonOdemeTarihi = Date.AddDays(10), Limit = 10000 };
    private static IslemEntity Expense(int id, int? card, decimal amount = 100, DateOnly? date = null) => new() { Id = id, Tarih = date ?? Date, TutarTl = amount, KrediKartiId = card, Cari = "Malzeme", KanalId = 1, Kanal = "MEZAT", Tip = GiderTipi.KrediKarti };
    private static void Seed(KasaWebFactory f, Action<KasaDbContext> action) { using var scope = f.Services.CreateScope(); action(scope.ServiceProvider.GetRequiredService<KasaDbContext>()); }
    private static Task<AlisDto> Purchase(HttpClient c) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Date, "Satıcı", null, [new("Mal", 500, [new(1, 500)])]));
    private static Task<List<BenzerKayitDto>> Find(HttpClient c, BenzerKayitSorgu body) => Post<List<BenzerKayitDto>>(c, "/api/islemler/benzerlik", body);
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var response = await c.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
