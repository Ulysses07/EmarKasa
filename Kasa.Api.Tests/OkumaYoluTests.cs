using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Salt okunur okuma yolu (gap-okuma-yolu-maliyet-kilit-cekismesi-2, finance-4): rapor ve takip GET'leri yazma
/// kilidi almaz ve veri yazmaz; uzun bir okuma sürerken paralel yazma beklemeden tamamlanır ve okuma kendi
/// tutarlı anlık görüntüsünü görür. Tarihe bağlı türetme (ekstre) yazılmadan da okumalar doğrudur.
/// </summary>
public class OkumaYoluTests
{
    /// <summary>Kurulduktan sonra adı verilen tablodan okuyan ilk komut çalıştıktan sonra (okumanın anlık görüntüsü
    /// alınmışken) test bırakana kadar bekler.</summary>
    internal sealed class OkumaKapisi : DbCommandInterceptor
    {
        private string? _tablo;
        private readonly SemaphoreSlim _girildi = new(0), _birak = new(0);
        public void Kur(string tablo) => Volatile.Write(ref _tablo, tablo);
        public Task<bool> Girildi() => _girildi.WaitAsync(TimeSpan.FromSeconds(20));
        public void Birak() => _birak.Release();
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            var tablo = Volatile.Read(ref _tablo);
            if (tablo is not null && command.CommandText.Contains($"FROM \"{tablo}\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _tablo, null, tablo) == tablo)
            {
                _girildi.Release();
                _birak.Wait(TimeSpan.FromSeconds(30));
            }
            return result;
        }
    }

    [Theory]
    [InlineData("/api/rapor/panel", "Islemler")]
    [InlineData("/api/rapor/haftalik", "Islemler")]
    [InlineData("/api/rapor/aylik?yil=2026&ay=9", "Islemler")]
    [InlineData("/api/donemler", "Islemler")]
    [InlineData("/api/islemler", "Islemler")]
    [InlineData("/api/alis", "Alislar")]
    [InlineData("/api/takip/ozet", "TakipKartOdemeler")]
    [InlineData("/api/takip/kartlar", "TakipKartOdemeler")]
    [InlineData("/api/kasa-esikleri", "Islemler")]
    [InlineData("/api/ay-kilidi", "AyKilidiOlaylar")]
    public async Task Uzun_okuma_surerken_paralel_yazma_beklemeden_tamamlanir(string uc, string tablo)
    {
        var kapi = new OkumaKapisi();
        await using var f = new DosyaFabrikasi { Kesiciler = [kapi] };
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 1, 1), kasaAcilisDevri = 1_000m })).EnsureSuccessStatusCode();
        var kart = await AltinTohum.Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 10_000m, 5, 15, new(2026, 1, 1), 100m, [new(1, 100m)]));
        await AltinTohum.Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, new(2026, 2, 1), 40m));
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 3, 1), "Önceki", 10m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        await AltinTohum.Post<AlisDto>(c, "/api/alis", new AlisYaz(0, new(2026, 3, 1), "Satıcı", null, [new("Mal", 50m, [new(1, 50m)])]));
        var once = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;

        kapi.Kur(tablo);
        var okuma = c.GetAsync(uc);
        Assert.True(await kapi.Girildi(), "Okuma beklenen tabloya ulaşmadı.");
        HttpResponseMessage yazma;
        var sure = Stopwatch.StartNew();
        try
        { yazma = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 9, 1), "Okuma sırasında", 25m, "MEZAT", GiderTipi.Cari)); }
        finally { sure.Stop(); kapi.Birak(); }
        var okumaYaniti = await okuma;
        Assert.Equal(HttpStatusCode.Created, yazma.StatusCode);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(3), $"Yazma okumayı {sure.ElapsedMilliseconds} ms bekledi.");
        Assert.Equal(HttpStatusCode.OK, okumaYaniti.StatusCode);
        var govde = await okumaYaniti.Content.ReadAsStringAsync();
        // Okuma, başladığı andaki anlık görüntüyü görür; paralel yazma sonraki okumada görünür.
        Assert.DoesNotContain("Okuma sırasında", govde);
        if (uc == "/api/rapor/panel")
            Assert.Equal(once, JsonNode.Parse(govde)!["guncelKasa"]!.GetValue<decimal>());
        Assert.Equal(once - 25m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    [Fact]
    public async Task Gun_donumu_Sync_calismadan_okumalar_yazmaz_ve_bakim_sonrasiyla_ayni_sonucu_verir()
    {
        await using var f = KasaWebFactory.Sabit(AltinTohum.Bugun);
        using var c = await f.EditorClientAsync();
        await AltinTohum.Kur(f, c);
        // Kartların kesim günleri geçer; Sync (bakım adımı) henüz çalışmadı: yeni kesim ekstreleri kayıtlı değil.
        ((SabitSaat)f.Saat!).Ayarla(new DateOnly(2026, 11, 16));
        var raporlar = new[] { "/api/rapor/panel", "/api/rapor/haftalik", "/api/donemler", "/api/rapor/aylik?yil=2026&ay=11",
            "/api/rapor/aylik?yil=2026&ay=12", "/api/kasa-esikleri", "/api/islemler" };
        var takip = new[] { "/api/takip/kartlar", "/api/takip/ozet", "/api/takip/ozet?gun=366", "/api/takip/krediler" };

        var degisiklik = ToplamDegisiklik(f);
        var ekstre = EkstreSayisi(f);
        var once = await Oku(c, raporlar.Concat(takip));
        Assert.Equal(degisiklik, ToplamDegisiklik(f));

        using (var scope = f.Services.CreateScope())
            FinansTakipServisi.Bakim(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
        Assert.True(EkstreSayisi(f) > ekstre, "Bakım adımı tarihe bağlı ekstreleri yazmalı.");
        var sonra = await Oku(c, raporlar.Concat(takip));

        foreach (var uc in raporlar)
            Assert.True(JsonNode.DeepEquals(once[uc], sonra[uc]), $"{uc} Sync'siz ve bakım sonrası farklı:\n{once[uc]!.ToJsonString()}\n{sonra[uc]!.ToJsonString()}");
        // Kaydedilmemiş (okumada türetilen) ekstrenin kimliği 0'dır; kimlikler dışında bütün alanlar aynıdır.
        foreach (var uc in takip)
            Assert.True(JsonNode.DeepEquals(KimliksizEkstre(once[uc]), KimliksizEkstre(sonra[uc])), $"{uc} Sync'siz ve bakım sonrası farklı:\n{once[uc]!.ToJsonString()}\n{sonra[uc]!.ToJsonString()}");
        var kartA = sonra["/api/takip/kartlar"]!.AsArray().Single(k => k!["ad"]!.GetValue<string>() == "Takip kart A")!;
        Assert.Contains(kartA["ekstreler"]!.AsArray(), e => e!["kesimTarihi"]!.GetValue<string>() == "2026-12-12");
    }

    [Fact]
    public async Task Bakim_adimi_acilista_tarihe_bagli_kesim_ekstrelerini_yazar()
    {
        await using var f = new DosyaFabrikasi { EkAyarlar = new() { ["Finans:BakimEtkin"] = "true" } };
        // Uygulama açılmadan önce: takipli aktif kart var, kesim ekstresi hiç yazılmamış (okumalar yazmaz).
        using (var db = f.Baglam())
        {
            KasaVeritabaniBaslatici.Baslat(db);
            db.Ayarlar.Add(new() { TakipBaslangic = new DateOnly(2026, 1, 1) });
            var kart = new KrediKartiEntity { Ad = "Bakım kartı", KesimTarihi = new(2026, 1, 10), SonOdemeTarihi = new(2026, 1, 20), Limit = 1m };
            db.KrediKartlari.Add(kart);
            db.SaveChanges();
            db.TakipKartlar.Add(new() { KrediKartiId = kart.Id, Baslangic = new DateOnly(2026, 1, 1) });
            db.SaveChanges();
        }
        _ = f.Services; // Açılış: bakım adımı ilk turunu hemen çalıştırır.
        var bekle = Stopwatch.StartNew();
        List<DateOnly> kesimler;
        do
        {
            await Task.Delay(100);
            using var db = f.Baglam();
            kesimler = db.TakipEkstreler.AsNoTracking().Select(e => e.KesimTarihi).OrderBy(d => d).ToList();
        } while (kesimler.Count < 2 && bekle.Elapsed < TimeSpan.FromSeconds(15));
        // Bugün 25.09.2026: sonraki kesim 10.10.2026, önceki 10.09.2026.
        Assert.Equal(new[] { new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 10) }, kesimler);
    }

    [Fact]
    public async Task Okuma_anlik_goruntusunde_veri_yazilamaz()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        using (db.OkumaBaslat())
        {
            db.Kanallar.Add(new() { Ad = "Yazılmamalı" });
            var hata = Assert.ThrowsAny<Exception>(() => db.SaveChanges());
            // SQLITE_READONLY (8): bağlantı düzeyinde query_only yazmayı reddeder.
            Assert.Equal(8, (hata as SqliteException ?? hata.InnerException as SqliteException)?.SqliteErrorCode);
            db.ChangeTracker.Clear();
        }
        Assert.Null(db.Database.CurrentTransaction);
        db.Kanallar.Add(new() { Ad = "Yazılır" });
        db.SaveChanges();
        Assert.True(db.Kanallar.Any(k => k.Ad == "Yazılır"));
    }

    [Fact]
    public void Okuma_anlik_goruntusu_bitince_baglanti_kapanir_ve_yazmaya_acilir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "kasa-anlik-" + Guid.NewGuid().ToString("N") + ".db");
        var baglanti = new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false }.ToString();
        try
        {
            using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(baglanti).Options))
            {
                KasaVeritabaniBaslatici.Baslat(db);
                using (db.OkumaBaslat())
                {
                    Assert.Equal(0, db.Kanallar.Count());
                    Assert.Equal(System.Data.ConnectionState.Open, db.Database.GetDbConnection().State);
                }
                // EF'in bağlantı sayacı dengeli: dosya tanıtıcısı istek sonunu beklemeden bırakılır.
                Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
                Assert.Null(db.Database.CurrentTransaction);
                db.Kanallar.Add(new() { Ad = "Sonra yazılır" });
                db.SaveChanges();
            }
            File.Delete(yol);
            Assert.False(File.Exists(yol));
        }
        finally { foreach (var ek in new[] { "", "-wal", "-shm" }) try { File.Delete(yol + ek); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    private static long ToplamDegisiklik(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        return db.Database.SqlQueryRaw<long>("SELECT total_changes() AS Value").AsEnumerable().Single();
    }
    private static int EkstreSayisi(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipEkstreler.Count();
    }
    private static async Task<Dictionary<string, JsonNode?>> Oku(HttpClient c, IEnumerable<string> uclar)
    {
        var sonuc = new Dictionary<string, JsonNode?>();
        foreach (var uc in uclar)
        {
            var r = await c.GetAsync(uc);
            var govde = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"GET {uc} {r.StatusCode}: {govde}");
            sonuc[uc] = JsonNode.Parse(govde);
        }
        return sonuc;
    }
    /// <summary>Ekstre kimliklerini (ekstreler[].id ve kart kesim/son ödeme olaylarının kalemId'si) sıfırlar.</summary>
    private static JsonNode? KimliksizEkstre(JsonNode? dugum)
    {
        var kopya = dugum?.DeepClone();
        void Gez(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                if (o["ekstreler"] is JsonArray ekstreler)
                    foreach (var e in ekstreler)
                        e!["id"] = 0;
                if (o["kaynak"]?.GetValue<string>() == "Kart" && o.ContainsKey("kalemId"))
                    o["kalemId"] = 0;
                foreach (var (_, v) in o.ToList())
                    Gez(v);
            }
            else if (n is JsonArray a)
                foreach (var v in a)
                    Gez(v);
        }
        Gez(kopya);
        return kopya;
    }
}
