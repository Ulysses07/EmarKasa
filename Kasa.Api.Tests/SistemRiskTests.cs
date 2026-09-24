using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>37 · Sistem ve risk kartı kuralları (saf fonksiyon).</summary>
public class RiskHesaplayiciTests
{
    private static readonly DateTime Simdi = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    private static RiskGirdisi Iyi() => new(
        new UzakYedekDurumu.Ozet(UzakYedekDurumu.Tamam, SonBasariYasSaat: 3),
        YerelYedekEtkin: true, YerelSonBasariliUtc: Simdi.AddHours(-5), YerelHata: null,
        SonDogrulama: new YedekDogrulamaEntity { ZamanUtc = Simdi.AddHours(-4), Dosya = "kasa-2026-09-24.db", Basarili = true, Mesaj = "ok" },
        DiskBosMb: 20_000, DunBasarisizGiris: 0, Karsiliksizlar: [], SimdiUtc: Simdi);

    private static IReadOnlyList<RiskMaddesiDto> H(RiskGirdisi g) => RiskHesaplayici.Hesapla(g, new RiskEsikleri());

    [Fact]
    public void Her_sey_yolundaysa_uyari_yok()
    {
        var m = H(Iyi());
        Assert.Empty(m);
        Assert.Equal("ok", RiskHesaplayici.Durum(m));
    }

    [Fact]
    public void Sunucu_disi_yedek_kurallari()
    {
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Yapilandirilmadi) })).Seviye);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Hatali, Hata: "rclone hata") })).Seviye);
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Eski, SonBasariYasSaat: 40) })).Seviye);
        var eski = Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Eski, SonBasariYasSaat: 72) }));
        Assert.Equal(RiskSeviyesi.Kirmizi, eski.Seviye);
        Assert.Contains("3 gün", eski.Baslik);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Eski) })).Seviye);
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Okunamadi) })).Seviye);
    }

    /// <summary>
    /// Bulgu: veritabanı gönderilip bir adım (ör. paket F'nin fiş/fatura ekleri) tamamlanmayınca durum
    /// "tamam" + uyarı oluyor, /health uyarıyı gösterdiği hâlde risk kartı "her şey yolunda" diyordu.
    /// </summary>
    [Fact]
    public void Sunucu_disi_yedek_tamam_ama_uyarili_ise_sari()
    {
        var m = Assert.Single(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Tamam, SonBasariYasSaat: 3, Uyari: "Fiş/fatura ekleri gönderilemedi.") }));
        Assert.Equal(RiskSeviyesi.Sari, m.Seviye);
        Assert.Equal("UzakYedek", m.Konu);
        Assert.Equal("Fiş/fatura ekleri gönderilemedi.", m.Aciklama);
        Assert.Empty(H(Iyi() with { UzakYedek = new(UzakYedekDurumu.Tamam, SonBasariYasSaat: 3, Uyari: "  ") }));
    }

    [Fact]
    public void Yerel_yedek_ve_dogrulama_kurallari()
    {
        var kapali = H(Iyi() with { YerelYedekEtkin = false, SonDogrulama = null });
        Assert.Equal("YerelYedek", Assert.Single(kapali).Konu);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { YerelHata = "disk dolu" })).Seviye);
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { YerelSonBasariliUtc = Simdi.AddHours(-30) })).Seviye);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { YerelSonBasariliUtc = Simdi.AddHours(-50) })).Seviye);

        var basarisiz = Assert.Single(H(Iyi() with { SonDogrulama = new YedekDogrulamaEntity { ZamanUtc = Simdi, Dosya = "kasa-2026-09-24.db", Basarili = false, Mesaj = "Kayıt sayıları tutmuyor" } }));
        Assert.Equal(RiskSeviyesi.Kirmizi, basarisiz.Seviye);
        Assert.Equal("Kayıt sayıları tutmuyor", basarisiz.Aciklama);
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { SonDogrulama = null })).Seviye);
    }

    [Fact]
    public void Disk_giris_ve_cek_kurallari()
    {
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { DiskBosMb = 800 })).Seviye);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { DiskBosMb = 100 })).Seviye);
        Assert.Empty(H(Iyi() with { DiskBosMb = null }));
        Assert.Empty(H(Iyi() with { DunBasarisizGiris = 4 }));
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { DunBasarisizGiris = 5 })).Seviye);
        Assert.Equal(RiskSeviyesi.Kirmizi, Assert.Single(H(Iyi() with { DunBasarisizGiris = 20 })).Seviye);

        var takipte = new KarsiliksizCek(1, "Ahmet", 1_000m, new DateOnly(2026, 9, 1), TakipNotuVar: true);
        var takipsiz = new KarsiliksizCek(2, "Mehmet", 2_500m, new DateOnly(2026, 9, 5), TakipNotuVar: false);
        Assert.Equal(RiskSeviyesi.Sari, Assert.Single(H(Iyi() with { Karsiliksizlar = [takipte] })).Seviye);
        var k = Assert.Single(H(Iyi() with { Karsiliksizlar = [takipte, takipsiz] }));
        Assert.Equal(RiskSeviyesi.Kirmizi, k.Seviye);
        Assert.Contains("2.500,00 ₺", k.Aciklama);
        Assert.Contains("Mehmet", k.Aciklama);
    }

    [Fact]
    public void Kirmizi_once_gelir_ve_durum_en_agir_maddedir()
    {
        var m = H(Iyi() with { DiskBosMb = 800, DunBasarisizGiris = 25 });
        Assert.Equal([RiskSeviyesi.Kirmizi, RiskSeviyesi.Sari], m.Select(x => x.Seviye));
        Assert.Equal("kirmizi", RiskHesaplayici.Durum(m));
        Assert.Equal("sari", RiskHesaplayici.Durum(H(Iyi() with { DiskBosMb = 800 })));
    }

    [Fact]
    public void Esikler_configten_okunur()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Kasa:RiskDiskSariMb"] = "5000" }).Build();
        var e = RiskEsikleri.Oku(cfg);
        Assert.Equal(5000, e.DiskSariMb);
        Assert.Equal(300, e.DiskKirmiziMb);
    }
}

/// <summary>GET /api/sistem/risk: yalnız editör; girdiler /health ve DB'den.</summary>
public class SistemRiskApiTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _f;
    public SistemRiskApiTests(KasaWebFactory f) => _f = f;

    [Fact]
    public async Task Editor_risk_kartini_gorur_izleyici_goremez()
    {
        var editor = await EditorAsync(_f);
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            // Dün (Türkiye) 6 başarısız giriş + sayılmayan "kod bekleniyor" ara adımı.
            var dunOgle = Saat.Bugun().AddDays(-1).ToDateTime(new TimeOnly(9, 0)); // TR 12:00
            for (var i = 0; i < 6; i++)
                db.GirisKayitlari.Add(new GirisKaydiEntity { ZamanUtc = dunOgle, KullaniciAdi = "deneme", Basarili = false, Neden = GirisNedenleri.HataliSifre });
            db.GirisKayitlari.Add(new GirisKaydiEntity { ZamanUtc = dunOgle, KullaniciAdi = "editor", Basarili = false, Neden = GirisNedenleri.KodBekleniyor });
            db.Cekler.Add(new CekEntity
            {
                Yon = Kasa.Core.CekYonu.Alinan, Kisi = "Karşılıksız Kişi", Tutar = 750m, DuzenlemeTarihi = new DateOnly(2026, 8, 1),
                VadeTarihi = new DateOnly(2026, 9, 1), Kanal = "MEZAT", Durum = Kasa.Core.CekDurumu.Karsiliksiz,
            });
            db.SaveChanges();
        }

        var r = await editor.GetFromJsonAsync<JsonElement>("/api/sistem/risk");
        Assert.Equal("kirmizi", r.GetProperty("durum").GetString());
        Assert.Equal(6, r.GetProperty("dunBasarisizGiris").GetInt32());
        var maddeler = r.GetProperty("maddeler").EnumerateArray().ToList();
        Assert.Contains(maddeler, m => m.GetProperty("konu").GetString() == "Cek" && m.GetProperty("seviye").GetString() == "Kirmizi");
        Assert.Contains(maddeler, m => m.GetProperty("konu").GetString() == "Giris" && m.GetProperty("seviye").GetString() == "Sari");
        Assert.Contains(maddeler, m => m.GetProperty("konu").GetString() == "UzakYedek");   // test ortamında kurulmamış
        Assert.Contains(maddeler, m => m.GetProperty("konu").GetString() == "YerelYedek");  // Kasa:YedekKlasoru yok

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "risk-izle" })).EnsureSuccessStatusCode();
        var izleyici = await GirisliAsync(_f, null, "risk-izle");
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.GetAsync("/api/sistem/risk")).StatusCode);
    }
}

/// <summary>Gece yedek doğrulaması ve güvenlik bakımı (gerçek dosyalarla).</summary>
public class YedekDogrulamaTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-dogrulama-" + Guid.NewGuid().ToString("N"));

    public YedekDogrulamaTests() => Directory.CreateDirectory(_klasor);

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
        return db;
    }

    private IConfiguration Cfg(params (string Anahtar, string Deger)[] ek)
    {
        var d = new Dictionary<string, string?> { ["Kasa:YedekKlasoru"] = Path.Combine(_klasor, "yedek") };
        foreach (var (a, v) in ek) d[a] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
    }

    [Fact]
    public void Saglam_yedek_dogrulanir_ayni_yedek_ikinci_kez_dogrulanmaz()
    {
        using var db = CanliDb();
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        db.Cariler.Add(new CariEntity { Ad = "Market" });
        db.SaveChanges();
        var cfg = Cfg();
        var dosya = YedekServisi.YedekAl(db, cfg["Kasa:YedekKlasoru"]!, new DateOnly(2026, 9, 24), 30);

        var sonuc = GuvenlikBakimServisi.Calistir(db, cfg, DateTime.UtcNow);
        Assert.NotNull(sonuc);
        Assert.True(sonuc!.Basarili, sonuc.Mesaj);
        Assert.Equal(Path.GetFileName(dosya), sonuc.Dosya);
        Assert.Null(GuvenlikBakimServisi.Calistir(db, cfg, DateTime.UtcNow));
        Assert.Single(db.YedekDogrulamalari.AsNoTracking());
        // Yedek dosyası salt okunur açıldı: değişmedi.
        Assert.Equal(sonuc.DosyaZamaniUtc, File.GetLastWriteTimeUtc(dosya));
    }

    [Fact]
    public void Kayit_sayisi_tutmayan_ya_da_bozuk_yedek_basarisiz_sayilir()
    {
        using var db = CanliDb();
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        db.SaveChanges();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 23), 30);
        // Yedekten sonra geçmişe yazılmadan eklenen kayıtlar (ör. yanlış dosyadan geri yükleme).
        for (var i = 0; i < 3; i++) db.Cariler.Add(new CariEntity { Ad = "Cari " + i });
        db.SaveChanges();

        var sonuc = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.False(sonuc.Basarili);
        Assert.Contains("Cariler: yedekte 0, canlıda 3", sonuc.Mesaj);

        var bozuk = Path.Combine(_klasor, "yedek", "kasa-2026-09-24.db");
        File.WriteAllText(bozuk, "bu bir sqlite dosyası değil");
        Assert.Equal(bozuk, YedekDogrulayici.EnYeniGunlukYedek(Path.Combine(_klasor, "yedek")));
        var b = YedekDogrulayici.Dogrula(db, bozuk, DateTime.UtcNow);
        Assert.False(b.Basarili);
        Assert.False(string.IsNullOrWhiteSpace(b.Mesaj));
    }

    [Fact]
    public void Yedekten_sonraki_gecmisli_degisiklikler_pay_sayilir()
    {
        using var db = CanliDb();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);
        db.Cariler.Add(new CariEntity { Ad = "Sonradan" });
        db.Degisiklikler.Add(new DegisiklikEntity { ZamanUtc = DateTime.UtcNow, Rol = "editor", Tur = "Cari", Eylem = "Eklendi", Ozet = "Cari eklendi: Sonradan" });
        db.SaveChanges();
        Assert.True(YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow).Basarili);
    }

    /// <summary>
    /// Bulgu: sonradan eklenen tablolar (paket B ay kilidi/yayını, hedef, bütçe, kur; D kart mutabakatı;
    /// F ekler ve POS) sayılmıyordu: bu satırları eksik yedek "sağlam" doğrulanıyordu.
    /// </summary>
    [Fact]
    public void Sonradan_eklenen_tablolardaki_eksik_satir_yakalanir()
    {
        using var db = CanliDb();
        var pos = new PosTanimEntity { Ad = "Banka POS", KomisyonOrani = 2.5m, BlokajGunu = 1 };
        db.PosTanimlari.Add(pos);
        db.AyKilitleri.Add(new AyKilidiEntity { Ay = new DateOnly(2026, 8, 1), Etiket = "Ağustos 2026", KilitZamaniUtc = DateTime.UtcNow });
        db.SaveChanges();
        db.PosSatislari.Add(new PosSatisEntity { Tarih = new DateOnly(2026, 9, 20), PosId = pos.Id, BrutTutar = 1_000m, KomisyonOrani = 2.5m, BlokajGunu = 1 });
        db.SaveChanges();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);
        Assert.True(YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow).Basarili);

        YedekteCalistir(dosya, "DELETE FROM \"PosSatislari\"; DELETE FROM \"PosTanimlari\"; DELETE FROM \"AyKilitleri\";");
        var sonuc = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.False(sonuc.Basarili);
        Assert.Contains("PosSatislari: yedekte 0, canlıda 1", sonuc.Mesaj);
        Assert.Contains("PosTanimlari: yedekte 0, canlıda 1", sonuc.Mesaj);
        Assert.Contains("AyKilitleri: yedekte 0, canlıda 1", sonuc.Mesaj);
    }

    [Fact]
    public void Guncellemeden_onceki_yedekte_olmayan_yeni_tablo_sayilmaz_mesajda_belirtilir()
    {
        using var db = CanliDb();
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        db.SaveChanges();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);
        YedekteCalistir(dosya, "DROP TABLE \"PosSatislari\"; DROP TABLE \"KartMutabakatlari\";");
        var sonuc = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.True(sonuc.Basarili, sonuc.Mesaj);
        Assert.Contains("PosSatislari", sonuc.Mesaj);
        Assert.Contains("KartMutabakatlari", sonuc.Mesaj);

        // Yedekte bulunan yeni tablonun eksik satırı yine yakalanır.
        db.PosTanimlari.Add(new PosTanimEntity { Ad = "Banka POS" });
        db.SaveChanges();
        var eksik = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.False(eksik.Basarili);
        Assert.Contains("PosTanimlari: yedekte 0, canlıda 1", eksik.Mesaj);
    }

    /// <summary>
    /// Toplu geçmiş satırı (takip birleştirmesi, gece ek temizliği) tek satırda birden çok kaydı
    /// değiştirir: pay, dizideki kayıt sayısı kadardır; yoksa meşru değişiklik "tutmuyor" sayılırdı.
    /// </summary>
    [Fact]
    public void Toplu_gecmis_satiri_dizideki_kayit_sayisi_kadar_pay_sayilir()
    {
        using var db = CanliDb();
        for (var i = 0; i < 3; i++)
            db.Gelenler.Add(new GelenEntity { DonemStart = new DateOnly(2026, 8, 3).AddDays(7 * i), Kanal = "MEZAT", TutarTl = 100m });
        db.SaveChanges();
        var dosya = YedekServisi.YedekAl(db, Path.Combine(_klasor, "yedek"), new DateOnly(2026, 9, 24), 30);

        // Üç gelen tek dönemde birleşti: canlıda 2 eksik, geçmişte tek (toplu) satır.
        var eskiler = db.Gelenler.OrderBy(g => g.Id).ToList();
        db.Gelenler.RemoveRange(eskiler.Skip(1));
        eskiler[0].TutarTl = 300m;
        db.Degisiklikler.Add(new DegisiklikEntity
        {
            ZamanUtc = DateTime.UtcNow, Rol = "editor", Tur = GecmisTurleri.Gelen, KayitId = eskiler[0].Id, Eylem = Eylemler.Guncellendi,
            Ozet = "Takip başlangıcı değişti: 3 gelen birleştirildi",
            EskiJson = "[" + string.Join(",", eskiler.Select(g => $"{{\"id\":{g.Id}}}")) + "]",
        });
        db.SaveChanges();
        var sonuc = YedekDogrulayici.Dogrula(db, dosya, DateTime.UtcNow);
        Assert.True(sonuc.Basarili, sonuc.Mesaj);
    }

    private static void YedekteCalistir(string dosya, string sql)
    {
        using (var bag = new SqliteConnection($"Data Source={dosya};Pooling=False"))
        {
            bag.Open();
            using var k = bag.CreateCommand();
            k.CommandText = sql;
            k.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Bakim_eski_giris_gunlugunu_ve_bitmis_oturumlari_siler()
    {
        using var db = CanliDb();
        var simdi = new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
        db.GirisKayitlari.AddRange(
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-200), KullaniciAdi = "eski" },
            new GirisKaydiEntity { ZamanUtc = simdi.AddDays(-10), KullaniciAdi = "yeni" });
        db.OturumKayitlari.AddRange(
            new OturumKaydiEntity { Jti = "bitti", Rol = "editor", BitisUtc = simdi.AddDays(-8) },
            new OturumKaydiEntity { Jti = "suruyor", Rol = "editor", BitisUtc = simdi.AddDays(3) });
        db.SaveChanges();

        Assert.Null(GuvenlikBakimServisi.Calistir(db, new ConfigurationBuilder().Build(), simdi));
        Assert.Equal(["yeni"], db.GirisKayitlari.AsNoTracking().Select(g => g.KullaniciAdi).ToList());
        Assert.Equal(["suruyor"], db.OturumKayitlari.AsNoTracking().Select(o => o.Jti).ToList());

        // Saklama süresi config'ten (en az 7 gün).
        Assert.Null(GuvenlikBakimServisi.Calistir(db, Cfg(("Kasa:GirisGunluguGun", "7"), ("Kasa:YedekKlasoru", "")), simdi));
        Assert.Empty(db.GirisKayitlari.AsNoTracking());
    }
}
