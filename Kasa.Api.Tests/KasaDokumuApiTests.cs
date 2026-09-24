using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>21 · "Kasa neden değişti?" uç noktası ve motorun ay ay çağrılmasıyla eşdeğerliği.</summary>
public class KasaDokumuApiTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public KasaDokumuApiTests(PaketBFactory f) => _f = f;

    private static readonly DateOnly Haz1 = new(2026, 6, 1);

    [Fact]
    public async Task Ay_dokumu_acilis_arti_adimlar_kapanis_ve_haftalik_devirle_ayni()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, Haz1, 10_000m);
        await GelenYaz(c, new DateOnly(2026, 6, 1), "MEZAT", 5_000m);
        await GelenYaz(c, new DateOnly(2026, 7, 6), "PERAKENDE", 7_500.50m);
        await IslemEkle(c, new DateOnly(2026, 6, 10), 300m, "MEZAT", "KrediKarti", "Market");   // kartsız K.K → Temmuz sonu
        await IslemEkle(c, new DateOnly(2026, 7, 2), 1_000m, "TOPTAN");
        await IslemEkle(c, new DateOnly(2026, 7, 3), 250m, "Ortak");
        var kart = (await (await c.PostAsJsonAsync("/api/kredikartlari", new { ad = "Kart B", kesimTarihi = "2026-07-10", sonOdemeTarihi = "2026-07-20", limit = 10000, borc = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-07-15", tutar = 400m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Müşteri", tutar = 2_000m, duzenlemeTarihi = "2026-06-01", vadeTarihi = "2026-07-20",
            kanal = "PERAKENDE", durum = "TahsilEdildi", islemTarihi = "2026-07-20",
        })).EnsureSuccessStatusCode();

        var d = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-07-01&bitis=2026-07-31");
        var adimlar = d.GetProperty("adimlar").EnumerateArray().ToList();
        Assert.Equal(D(d, "kapanis"), D(d, "acilis") + adimlar.Sum(a => D(a, "tutar")));
        Assert.Equal(D(d, "kapanis"), D(adimlar[^1], "bakiye"));
        Assert.Equal("2026-07-01", d.GetProperty("baslangic").GetString());
        Assert.Equal("2026-07-31", d.GetProperty("bitis").GetString());

        var haftalik = (await Oku(c, "/api/rapor/haftalik")).EnumerateArray().ToList();
        var temmuz = haftalik.Where(h => h.GetProperty("donem").GetProperty("ay").GetInt32() == 7).ToList();
        var haziran = haftalik.Where(h => h.GetProperty("donem").GetProperty("ay").GetInt32() == 6).ToList();
        Assert.Equal(D(temmuz[^1], "kasaDevir"), D(d, "kapanis"));
        Assert.Equal(D(haziran[^1], "kasaDevir"), D(d, "acilis"));

        var ozet = adimlar.Select(a => (a.GetProperty("tur").GetString(), a.GetProperty("kanal").ValueKind == JsonValueKind.Null ? null : a.GetProperty("kanal").GetString(), D(a, "tutar"))).ToList();
        Assert.Contains(("Gelen", "PERAKENDE", 7_500.50m), ozet);
        Assert.Contains(("CekTahsilat", "PERAKENDE", 2_000m), ozet);
        Assert.Contains(("CariGider", "TOPTAN", -1_000m), ozet);
        Assert.Contains(("OrtakGider", "Ortak", -250m), ozet);
        Assert.Contains(("KartOdemesi", null, -400m), ozet);
        Assert.Contains(("ErtelenenKk", "MEZAT", -300m), ozet);
        Assert.Equal("K.K (geçen ay, kartsız)", adimlar.Single(a => a.GetProperty("tur").GetString() == "ErtelenenKk").GetProperty("turAdi").GetString());

        // Tüm takvim: açılış = kasa açılış devri, kapanış = bugünkü kasa (panel).
        var tum = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-01-01&bitis=2026-12-31");
        Assert.Equal(10_000m, D(tum, "acilis"));
        Assert.Equal(D(await Oku(c, "/api/rapor/panel"), "guncelKasa"), D(tum, "kapanis"));
    }

    [Fact]
    public async Task Hafta_ortasindan_istenen_aralik_donem_basina_genisler()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, Haz1);
        await IslemEkle(c, new DateOnly(2026, 9, 7), 100m);
        var d = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-09-09&bitis=2026-09-09");
        Assert.Equal("2026-09-07", d.GetProperty("baslangic").GetString());
        Assert.Equal("2026-09-13", d.GetProperty("bitis").GetString());
        Assert.Equal(-100m, D(d, "kapanis") - D(d, "acilis"));
    }

    [Theory]
    [InlineData("/api/rapor/kasa-dokumu", "Başlangıç ve bitiş tarihi gerekli.")]
    [InlineData("/api/rapor/kasa-dokumu?baslangic=2026-09-10&bitis=2026-09-01", "Bitiş tarihi başlangıçtan önce olamaz.")]
    [InlineData("/api/rapor/kasa-dokumu?baslangic=1990-01-01&bitis=2026-09-01", "Tarih 2000 ile 2100 arasında olmalı.")]
    [InlineData("/api/rapor/kasa-dokumu?baslangic=2020-01-01&bitis=2020-01-31", "Bu aralıkta takip dönemi yok.")]
    public async Task Gecersiz_aralik_400(string url, string mesaj)
    {
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, Haz1);
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(mesaj, await HataMetni(r));
    }

    [Fact]
    public async Task Izleyici_okur_oturumsuz_401()
    {
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, Haz1);
        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/rapor/kasa-dokumu?baslangic=2026-09-01&bitis=2026-09-30")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/rapor/kasa-dokumu?baslangic=2026-09-01&bitis=2026-09-30")).StatusCode);
    }
}

/// <summary>Ay ay hesap (HesapServisi) tek çağrıyla aynı kasa kalemlerini üretir; her dönem Σ kalem = kasa sonucu.</summary>
public class KasaDokumuAylaraBolmeTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Kalemler_ay_ay_hesapta_da_ayni(int tohum)
    {
        var r = new Random(tohum);
        var takip = new DateOnly(2025, 3, 1).AddDays(r.Next(0, 40));
        var bitis = new DateOnly(2026, 9, 24);
        var kanallar = new List<Kanal> { new("MEZAT", 10m), new("PERAKENDE", 0m), new("TOPTAN", 5m, false) };
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN", Kanallar.Ortak };
        int gun = bitis.DayNumber - takip.DayNumber;
        var islemler = Enumerable.Range(0, 1500).Select(_ =>
        {
            var tip = (GiderTipi)r.Next(0, 3);
            int? kart = r.Next(3) == 0 ? 1 : null;
            return new Islem(takip.AddDays(r.Next(-40, gun)), "C", r.Next(1, 100_000) / 100m, adlar[r.Next(adlar.Length)], tip, null, kart);
        }).ToList();
        var donemler = DonemUretici.Uret(takip, bitis);
        var gelenler = donemler.SelectMany(d => kanallar.Select(k => new Gelen(d.Start, k.Ad, r.Next(0, 50_000)))).ToList();
        var odemeler = Enumerable.Range(0, 30).Select(_ => new KartOdeme(takip.AddDays(r.Next(0, gun)), r.Next(1, 9000))).ToList();
        var cekler = Enumerable.Range(0, 60).Select(_ => new Cek((CekYonu)r.Next(2), r.Next(1, 90_000), adlar[r.Next(adlar.Length)],
            (CekDurumu)r.Next(6), takip.AddDays(r.Next(0, gun)))).ToList();

        var tek = HesapMotoru.HaftalikHesapla(500m, kanallar, islemler, gelenler, donemler, odemeler, cekler);
        var parcali = HesapServisi.HaftalikAylaraBolerek(500m, kanallar, islemler, gelenler, donemler, odemeler, cekler);
        Assert.Equal(tek.Count, parcali.Count);
        for (int i = 0; i < tek.Count; i++)
        {
            Assert.Equal(tek[i].Kalemler, parcali[i].Kalemler);
            Assert.Equal(parcali[i].KasaSonucu, parcali[i].Kalemler.Sum(k => k.Tutar));
        }
        var d = KasaDokumuHesap.Olustur(parcali, kanallar)!;
        Assert.Equal(500m, d.Acilis);
        Assert.Equal(parcali[^1].KasaDevir, d.Acilis + d.Adimlar.Sum(a => a.Tutar));
    }
}

/// <summary>Master şemasındaki (paket B tabloları olmayan) DB güncellenir ve verisini korur.</summary>
public class PaketBSemaTests
{
    private static readonly string[] YeniTablolar = ["AyKilitleri", "AyYayinlari", "KanalHedefleri", "GiderButceleri", "Kurlar"];

    private static List<string> Oku(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetValue(0)?.ToString() ?? "");
        return l;
    }

    private static void Calistir(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Master_semali_db_yeni_tablolari_alir_veri_korunur_ikinci_calisma_bos()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options))
        {
            db.Database.EnsureCreated();
            foreach (var t in YeniTablolar) Calistir(conn, $"DROP TABLE \"{t}\"");
            // Master şemasında veri: kanal, kalem, işlem, gelen, çek, sayım, ayar.
            Calistir(conn, "INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) VALUES ('MEZAT', 1, 0, '100.0')");
            Calistir(conn, "INSERT INTO GiderKalemleri (Ad, Aktif) VALUES ('Kira', 1)");
            Calistir(conn, "INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, EditorOturumSurumu, IzleyiciOturumSurumu) VALUES ('2026-06-01', '50.0', 0, 0)");
            Calistir(conn, "INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, Tip) VALUES ('2026-06-02', 'X', '12.5', 'MEZAT', 'Cari')");
            Calistir(conn, "INSERT INTO Gelenler (DonemStart, Kanal, TutarTl) VALUES ('2026-06-01', 'MEZAT', '99.0')");
            Calistir(conn, "INSERT INTO KasaSayimlari (Tarih, SayilanTutar, HesaplananTutar, KayitZamaniUtc, FarkDurumu) VALUES ('2026-06-03', '1.0', '2.0', '2026-06-03 10:00:00', 0)");
        }
        var once = Oku(conn, "SELECT COUNT(*) FROM Islemler").Single() + Oku(conn, "SELECT COUNT(*) FROM Gelenler").Single()
                   + Oku(conn, "SELECT COUNT(*) FROM KasaSayimlari").Single();

        using var db2 = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        var yapilan = SemaGuncelleyici.Guncelle(db2, NullLogger.Instance);
        foreach (var t in YeniTablolar) Assert.Contains($"tablo+ {t}", yapilan);

        Assert.Equal(once, Oku(conn, "SELECT COUNT(*) FROM Islemler").Single() + Oku(conn, "SELECT COUNT(*) FROM Gelenler").Single()
                           + Oku(conn, "SELECT COUNT(*) FROM KasaSayimlari").Single());
        Assert.Equal("12.5", Oku(conn, "SELECT TutarTl FROM Islemler").Single());
        Assert.Contains("Kanallar:CASCADE", Oku(conn, "SELECT \"table\" || ':' || on_delete FROM pragma_foreign_key_list('KanalHedefleri')"));
        Assert.Contains("GiderKalemleri:CASCADE", Oku(conn, "SELECT \"table\" || ':' || on_delete FROM pragma_foreign_key_list('GiderButceleri')"));
        Assert.Contains("IX_KanalHedefleri_Ay_KanalId", Oku(conn, "SELECT name FROM pragma_index_list('KanalHedefleri') WHERE \"unique\" = 1"));
        Assert.Contains("IX_GiderButceleri_Ay_GiderKalemiId", Oku(conn, "SELECT name FROM pragma_index_list('GiderButceleri') WHERE \"unique\" = 1"));
        foreach (var t in new[] { "AyKilitleri", "AyYayinlari", "Kurlar" })
            Assert.Contains($"IX_{t}_Ay", Oku(conn, $"SELECT name FROM pragma_index_list('{t}') WHERE \"unique\" = 1"));
        Assert.Empty(SemaGuncelleyici.Guncelle(db2, NullLogger.Instance));

        // Yeni tablolar kullanılabilir; kilit tablosu varken de eski veri okunur.
        db2.AyKilitleri.Add(new AyKilidiEntity { Ay = new DateOnly(2026, 6, 1), Etiket = "Haziran 2026", KilitZamaniUtc = DateTime.UtcNow });
        db2.SaveChanges();
        Assert.Single(db2.Islemler.AsNoTracking().ToList());
    }

    [Fact]
    public void Kilit_tablosu_yoksa_yazma_engellenmez()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        Calistir(conn, "DROP TABLE \"AyKilitleri\"");
        db.Islemler.Add(new IslemEntity { Tarih = new DateOnly(2026, 6, 2), Cari = "X", TutarTl = 1m, Kanal = "MEZAT", Tip = GiderTipi.Cari });
        db.SaveChanges();
        Assert.Equal(1, db.Islemler.Count());
    }
}
