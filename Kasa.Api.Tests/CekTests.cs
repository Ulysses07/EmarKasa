using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>Çek uçları (CRUD, doğrulama, özet, yetki) ve raporlara "vadede kasaya" yansıması.</summary>
public class CekTests : IClassFixture<CekTests.SabitSaatFactory>
{
    /// <summary>Bugün = 24 Eylül 2026 Perşembe (İstanbul).</summary>
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

    private record CekYanit(int Id, string Yon, string? CekNo, string? Banka, string Kisi, decimal Tutar,
        DateOnly DuzenlemeTarihi, DateOnly VadeTarihi, string Kanal, string Durum, DateOnly? IslemTarihi, string? Not);
    private record OzetYanit(decimal PortfoydekiAlinanToplam, int PortfoydekiAlinanAdet, decimal OdenecekVerilenToplam,
        int OdenecekVerilenAdet, int YaklasanGun, List<CekYanit> Yaklasanlar, List<CekYanit> VadesiGecenler);
    private record KanalHaftalikYanit(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir, decimal CekGelen, decimal CekGiden);
    private record HaftalikYanit(Donem Donem, List<KanalHaftalikYanit> Kanallar, decimal ToplamGelen, decimal ToplamGiden,
        decimal KasaSonucu, decimal KasaDevir, decimal ToplamCekGelen, decimal ToplamCekGiden);
    private record PanelYanit(decimal GuncelKasa, List<KanalBakiye> Kanallar, decimal BuHaftaSonucu, decimal BuAySonucu);
    private record HataYanit(string Hata);

    private readonly SabitSaatFactory _factory;
    public CekTests(SabitSaatFactory factory) => _factory = factory;

    private static object Govde(string yon = "Alinan", string durum = "Portfoyde", string kanal = "MEZAT",
        decimal tutar = 1_000m, string? islemTarihi = null, string kisi = "Ahmet Yılmaz",
        string duzenleme = "2026-09-01", string vade = "2026-10-15", string? cekNo = "0012345", string? banka = "Ziraat")
        => new
        {
            yon, cekNo, banka, kisi, tutar, duzenlemeTarihi = duzenleme, vadeTarihi = vade,
            kanal, durum, islemTarihi, not = (string?)null,
        };

    private void CekleriTemizle()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Cekler.ExecuteDelete();
    }

    private async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    [Fact]
    public async Task Editor_cek_ekler_filtreler_gunceller_ve_siler()
    {
        CekleriTemizle();
        var c = await _factory.EditorClientAsync();

        // Portföydeki çekin işlem tarihi tutulmaz; metinler kırpılır, boş çek no null olur.
        var r1 = await c.PostAsJsonAsync("/api/cekler", Govde(islemTarihi: "2026-09-10", kisi: "  Ahmet Yılmaz ", cekNo: "  "));
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var alinan = (await r1.Content.ReadFromJsonAsync<CekYanit>())!;
        Assert.Null(alinan.IslemTarihi);
        Assert.Equal("Ahmet Yılmaz", alinan.Kisi);
        Assert.Null(alinan.CekNo);
        Assert.Equal("Alinan", alinan.Yon);

        var r2 = await c.PostAsJsonAsync("/api/cekler", Govde(yon: "Verilen", kanal: Kanallar.Ortak, tutar: 250.5m, vade: "2026-11-01"));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var verilen = (await r2.Content.ReadFromJsonAsync<CekYanit>())!;

        var hepsi = (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler"))!;
        Assert.Equal([verilen.Id, alinan.Id], hepsi.Select(x => x.Id));   // vadeye göre en yeni önce
        Assert.Equal([alinan.Id], (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler?yon=alinan"))!.Select(x => x.Id));
        Assert.Equal(2, (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler?durum=Portfoyde"))!.Count);
        Assert.Equal([verilen.Id], (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler?baslangic=2026-10-20&bitis=2026-11-30"))!.Select(x => x.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cekler?yon=Kesilen")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cekler?durum=1")).StatusCode);

        var put = await c.PutAsJsonAsync($"/api/cekler/{alinan.Id}", Govde(durum: "TahsilEdildi", islemTarihi: "2026-09-20"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var tahsil = (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler?durum=TahsilEdildi"))!;
        Assert.Equal(new DateOnly(2026, 9, 20), tahsil.Single().IslemTarihi);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cekler/{verilen.Id}")).StatusCode);
        Assert.DoesNotContain((await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler"))!, x => x.Id == verilen.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/cekler/99999", Govde())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/cekler/99999")).StatusCode);
    }

    [Theory]
    [InlineData("Alinan", "Odendi", "MEZAT", "2026-09-10", "Alınan çek 'Ödendi' durumunda olamaz.")]
    [InlineData("Verilen", "TahsilEdildi", "MEZAT", "2026-09-10", "Verilen çek 'Tahsil edildi' durumunda olamaz.")]
    [InlineData("Verilen", "CiroEdildi", "MEZAT", "2026-09-10", "Verilen çek 'Ciro edildi' durumunda olamaz.")]
    [InlineData("Verilen", "Karsiliksiz", "MEZAT", null, "Verilen çek 'Karşılıksız' durumunda olamaz.")]
    [InlineData("Alinan", "Portfoyde", "Ortak", null, "yalnız verilen çekte")]
    [InlineData("Alinan", "Portfoyde", "YOK", null, "adında bir kanal yok")]
    [InlineData("Alinan", "TahsilEdildi", "MEZAT", null, "tahsil tarihi girilmeli")]
    [InlineData("Verilen", "Odendi", "MEZAT", null, "ödeme tarihi girilmeli")]
    [InlineData("Alinan", "CiroEdildi", "MEZAT", null, "ciro tarihi girilmeli")]
    [InlineData("Alinan", "TahsilEdildi", "MEZAT", "2026-08-01", "düzenleme tarihinden önce")]
    public async Task Yon_durum_kanal_ve_islem_tarihi_dogrulanir(string yon, string durum, string kanal, string? islemTarihi, string beklenen)
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/cekler", Govde(yon: yon, durum: durum, kanal: kanal, islemTarihi: islemTarihi));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(beklenen, (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata);
    }

    [Theory]
    [InlineData(0, "Ahmet", "2026-09-01", "2026-10-01", "sıfırdan büyük")]
    [InlineData(10.555, "Ahmet", "2026-09-01", "2026-10-01", "2 ondalık")]
    [InlineData(10, "  ", "2026-09-01", "2026-10-01", "boş olamaz")]
    [InlineData(10, "Ahmet", "2026-09-01", "2026-08-01", "Vade tarihi düzenleme tarihinden önce olamaz")]
    [InlineData(10, "Ahmet", "1999-12-31", "2026-08-01", "Düzenleme tarihi")]
    public async Task Tutar_kisi_ve_tarihler_dogrulanir(decimal tutar, string kisi, string duzenleme, string vade, string beklenen)
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/cekler", Govde(tutar: tutar, kisi: kisi, duzenleme: duzenleme, vade: vade));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(beklenen, (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata);
    }

    [Fact]
    public async Task Izleyici_cekleri_ve_ozeti_okur_ama_yazamaz()
    {
        var editor = await _factory.EditorClientAsync();
        var eklenen = (await (await editor.PostAsJsonAsync("/api/cekler", Govde())).Content.ReadFromJsonAsync<CekYanit>())!;
        var izleyici = await IzleyiciAsync();

        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/cekler")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/cekler/ozet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/cekler", Govde())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync($"/api/cekler/{eklenen.Id}", Govde())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/cekler/{eklenen.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/cekler")).StatusCode);
    }

    [Fact]
    public async Task Ozet_portfoy_toplamlarini_yaklasan_ve_vadesi_gecen_cekleri_doner()
    {
        CekleriTemizle();
        var c = await _factory.EditorClientAsync();
        async Task<int> Ekle(object g)
        {
            var r = await c.PostAsJsonAsync("/api/cekler", g);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<CekYanit>())!.Id;
        }
        var gecikenAlinan = await Ekle(Govde(tutar: 100m, vade: "2026-09-20"));
        var bugunVerilen = await Ekle(Govde(yon: "Verilen", tutar: 40m, vade: "2026-09-24"));
        var yakinAlinan = await Ekle(Govde(tutar: 200m, vade: "2026-10-24"));                 // bugün + 30: dahil
        await Ekle(Govde(tutar: 300m, vade: "2026-10-25"));                                    // bugün + 31: uzak
        await Ekle(Govde(yon: "Verilen", kanal: Kanallar.Ortak, tutar: 60m, vade: "2026-12-01"));
        await Ekle(Govde(durum: "TahsilEdildi", islemTarihi: "2026-09-15", tutar: 999m, vade: "2026-09-10"));  // portföyde değil
        await Ekle(Govde(yon: "Verilen", durum: "Odendi", islemTarihi: "2026-09-15", tutar: 888m, vade: "2026-09-30"));

        var o = (await c.GetFromJsonAsync<OzetYanit>("/api/cekler/ozet"))!;
        Assert.Equal(600m, o.PortfoydekiAlinanToplam);
        Assert.Equal(3, o.PortfoydekiAlinanAdet);
        Assert.Equal(100m, o.OdenecekVerilenToplam);
        Assert.Equal(2, o.OdenecekVerilenAdet);
        Assert.Equal(30, o.YaklasanGun);
        Assert.Equal([bugunVerilen, yakinAlinan], o.Yaklasanlar.Select(x => x.Id));
        Assert.Equal([gecikenAlinan], o.VadesiGecenler.Select(x => x.Id));
    }

    [Fact]
    public async Task Raporlar_ceki_yalniz_tahsil_ve_odeme_gununde_ayri_alanda_sayar()
    {
        CekleriTemizle();
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1), kasaAcilis: 1_000m);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.ExecuteDelete();
            db.Gelenler.ExecuteDelete();
        }

        async Task Ekle(object g) => (await c.PostAsJsonAsync("/api/cekler", g)).EnsureSuccessStatusCode();
        await Ekle(Govde(durum: "TahsilEdildi", islemTarihi: "2026-09-10", tutar: 500m));                          // 7–13 Eyl
        await Ekle(Govde(yon: "Verilen", kanal: Kanallar.Ortak, durum: "Odendi", islemTarihi: "2026-09-15", tutar: 200m)); // 14–20 Eyl
        await Ekle(Govde(yon: "Verilen", kanal: "TOPTAN", durum: "Odendi", islemTarihi: "2026-09-30", tutar: 50m));      // ileri tarihli
        await Ekle(Govde(kanal: "PERAKENDE", durum: "CiroEdildi", islemTarihi: "2026-09-11", tutar: 999m));           // kasaya dokunmaz
        await Ekle(Govde(tutar: 77m));                                                                                 // portföyde

        var haftalik = (await c.GetFromJsonAsync<List<HaftalikYanit>>("/api/rapor/haftalik"))!;
        var h2 = haftalik.Single(d => d.Donem.Start == new DateOnly(2026, 9, 7));
        Assert.Equal(0m, h2.ToplamGelen);
        Assert.Equal(500m, h2.ToplamCekGelen);
        Assert.Equal(500m, h2.KasaSonucu);
        var mezat = h2.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal((0m, 500m, 500m, 500m), (mezat.Gelen, mezat.CekGelen, mezat.Sonuc, mezat.Devir));
        var h3 = haftalik.Single(d => d.Donem.Start == new DateOnly(2026, 9, 14));
        Assert.Equal((0m, 200m, -200m), (h3.ToplamGiden, h3.ToplamCekGiden, h3.KasaSonucu));
        Assert.All(h3.Kanallar, k => Assert.Equal(0m, k.CekGiden));     // Ortak ödeme yalnız kasadan
        Assert.Equal(1_300m, haftalik[^1].KasaDevir);                      // ileri tarihli 50 henüz düşmez
        Assert.Equal(0m, haftalik[^1].Kanallar.Single(k => k.Kanal == "TOPTAN").Devir);

        var aylik = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=9"))!;
        var am = aylik.Kanallar.Single(k => k.Kanal == "MEZAT");
        // Ortak çek ödemesi o ay hareketli tek kanal MEZAT'a düşer (Ortak Cari gider gibi).
        Assert.Equal((0m, 500m, 0m, 200m, 300m), (am.Gelen, am.CekGelen, am.OrtakPay, am.CekGiden, am.AySonucu));
        Assert.All(aylik.Kanallar.Where(k => k.Kanal != "MEZAT"), k => Assert.Equal(0m, k.AySonucu));

        var panel = (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!;
        Assert.Equal(1_300m, panel.GuncelKasa);
        Assert.Equal(500m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
        Assert.Equal(300m, panel.BuAySonucu);
        Assert.Equal(0m, panel.BuHaftaSonucu);
    }

    [Fact]
    public async Task Kanal_adi_degisince_cek_tasinir_ceki_olan_kanal_silinemez()
    {
        var c = await _factory.EditorClientAsync();
        var kr = await c.PostAsJsonAsync("/api/kanallar", new { ad = "CEKKANAL", aktif = true, sira = 9, acilisDevri = 0m });
        kr.EnsureSuccessStatusCode();
        var kanalId = (await kr.Content.ReadFromJsonAsync<KanalEntity>())!.Id;
        var cek = (await (await c.PostAsJsonAsync("/api/cekler", Govde(kanal: "CEKKANAL"))).Content.ReadFromJsonAsync<CekYanit>())!;

        (await c.PutAsJsonAsync($"/api/kanallar/{kanalId}", new { ad = "CEKKANAL2", aktif = true, sira = 9, acilisDevri = 0m })).EnsureSuccessStatusCode();
        Assert.Equal("CEKKANAL2", (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler"))!.Single(x => x.Id == cek.Id).Kanal);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{kanalId}")).StatusCode);
    }
}

/// <summary>Çek tablosu var olan DB'lere otomatik eklenir; ay ay hesap çeklerle de tek çağrıya eşittir.</summary>
public class CekSemaVeHesapTests
{
    [Fact]
    public void Cek_tablosu_var_olan_dbde_indexleriyle_olusturulur()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        // Çek özelliğinden önceki canlı DB: tablo yok, diğer her şey güncel ve içinde veri var.
        db.Database.ExecuteSqlRaw("DROP TABLE \"Cekler\"");
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        db.SaveChanges();

        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance);

        Assert.Equal(["tablo+ Cekler"], yapilan);
        var indexler = Oku(conn, "SELECT name FROM pragma_index_list('Cekler')");
        Assert.Contains("IX_Cekler_IslemTarihi", indexler);
        Assert.Contains("IX_Cekler_VadeTarihi", indexler);
        Assert.Contains("IslemTarihi", Oku(conn, "SELECT name FROM pragma_table_info('Cekler')"));

        db.Cekler.Add(new CekEntity
        {
            Yon = CekYonu.Verilen, Kisi = "X", Tutar = 5m, Kanal = Kanallar.Ortak, Durum = CekDurumu.Odendi,
            DuzenlemeTarihi = new DateOnly(2026, 9, 1), VadeTarihi = new DateOnly(2026, 9, 1), IslemTarihi = new DateOnly(2026, 9, 2),
        });
        db.SaveChanges();
        Assert.Equal(1, db.Kanallar.Count());
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance));   // idempotent
    }

    private static List<string> Oku(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(Convert.ToString(r.GetValue(0))!);
        return l;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Aylara_bolerek_hesap_ceklerle_de_tek_cagriyla_ayni(int tohum)
    {
        var r = new Random(tohum);
        var takip = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 60));
        var bitis = new DateOnly(2026, 9, 24);
        var kanallar = new List<Kanal> { new("MEZAT", 100m, true, 0), new("PERAKENDE", -5m, true, 1), new("TOPTAN", 0m, false, 2) };
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN", Kanallar.Ortak };
        int gun = bitis.DayNumber - takip.DayNumber + 60;

        var islemler = Enumerable.Range(0, 1500).Select(_ =>
        {
            var tip = (GiderTipi)r.Next(0, 3);
            int? kart = tip == GiderTipi.KrediKarti && r.Next(2) == 0 ? r.Next(1, 3) : null;
            return new Islem(takip.AddDays(r.Next(-45, gun - 60)), "C", r.Next(1, 100_000) / 100m, adlar[r.Next(adlar.Length)], tip, null, kart);
        }).ToList();
        var donemler = DonemUretici.Uret(takip, bitis);
        var gelenler = donemler.SelectMany(d => kanallar.Select(k => new Gelen(d.Start, k.Ad, r.Next(0, 50_000)))).ToList();
        var odemeler = Enumerable.Range(0, 40).Select(_ => new KartOdeme(takip.AddDays(r.Next(0, gun - 60)), r.Next(1, 9000))).ToList();
        var cekler = Enumerable.Range(0, 400).Select(_ =>
        {
            var yon = (CekYonu)r.Next(2);
            var kanal = yon == CekYonu.Verilen ? adlar[r.Next(adlar.Length)] : adlar[r.Next(3)];
            DateOnly? tarih = r.Next(8) == 0 ? null : takip.AddDays(r.Next(-45, gun - 60));
            return new Cek(yon, r.Next(1, 500_000) / 100m, kanal, (CekDurumu)r.Next(6), tarih);
        }).ToList();

        var tek = HesapMotoru.HaftalikHesapla(1234.56m, kanallar, islemler, gelenler, donemler, odemeler, cekler);
        var parcali = HesapServisi.HaftalikAylaraBolerek(1234.56m, kanallar, islemler, gelenler, donemler, odemeler, cekler);

        Assert.Equal(JsonSerializer.Serialize(tek), JsonSerializer.Serialize(parcali));
        Assert.Contains(tek, d => d.ToplamCekGelen != 0m);
        Assert.Contains(tek, d => d.ToplamCekGiden != 0m);
    }
}
