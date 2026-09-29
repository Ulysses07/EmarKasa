using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Kart hesabının maliyeti (gap-okuma-yolu-maliyet-kilit-cekismesi-1, finance-5, finance-6,
/// gap-coklu-giris-cift-sayim-mutabakat-14): rapor ve takip okumalarının SQL komut sayısı kartın ödeme,
/// harcama ve alış bağı sayısıyla büyümez; taksit tablosu hiçbir zaman süzgeçsiz okunmaz; bir istek içinde
/// aynı kartın ödemeleri bir kez okunur.
/// </summary>
public class KartHesapMaliyetiTests(ITestOutputHelper cikti)
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;
    private static readonly DateOnly Baslangic = new(2026, 1, 1);

    /// <summary>EF Core komut kesicisi: yalnız etkinken çalışan komutların SQL metnini sayar.</summary>
    internal sealed class SorguSayaci : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _komutlar = new();
        public volatile bool Etkin;
        public IReadOnlyList<string> Komutlar => _komutlar.ToArray();
        public void Sifirla() { while (_komutlar.TryDequeue(out _)) { } }
        private void Kaydet(DbCommand komut) { if (Etkin) _komutlar.Enqueue(komut.CommandText); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Kaydet(command); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Kaydet(command); return result; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Kaydet(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Kaydet(command); return ValueTask.FromResult(result); }
    }

    internal sealed class SayacliFabrika : KasaWebFactory
    {
        public SorguSayaci Sayac { get; } = new();
        public SayacliFabrika() => Saat = new SabitSaat(VarsayilanBugun);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.ConfigureDbContext<KasaDbContext>(o => o.AddInterceptors(Sayac)));
        }
    }

    private static async Task<(SayacliFabrika F, HttpClient C, int Kart)> Kur(int alisliHarcama, int odeme, int ikinciKartTaksiti = 0)
    {
        var f = new SayacliFabrika();
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 100_000m })).EnsureSuccessStatusCode();
        var kart = await AltinTohum.Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Yoğun kart", 1_000_000m, 10, 20, Baslangic, 0m, []));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            AlisliHarcamalar(db, kart.Id, alisliHarcama, new(2026, 1, 3));
            if (ikinciKartTaksiti > 0)
            {
                // Başka bir kartın çok sayıda taksidi: hedef kartın hesabı bunları okumamalı.
                var diger = new KrediKartiEntity { Ad = "Diğer kart", KesimTarihi = new(2026, 1, 5), SonOdemeTarihi = new(2026, 1, 15), Limit = 1m };
                db.KrediKartlari.Add(diger); db.SaveChanges();
                db.TakipKartlar.Add(new() { KrediKartiId = diger.Id, Baslangic = Baslangic }); db.SaveChanges();
                for (var i = 0; i < ikinciKartTaksiti; i++)
                    db.Islemler.Add(new() { Tarih = Baslangic.AddDays(i % 200), Cari = "Diğer " + i, TutarTl = 10m, KanalId = 1, Kanal = "MEZAT", Tip = GiderTipi.KrediKarti, KrediKartiId = diger.Id });
                db.SaveChanges();
            }
            FinansTakipServisi.GetNotificationEvents(db, Bugun); // Kart harcamalarını türet (yazma yolu).
        }
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        for (var i = 0; i < odeme; i++)
            kart = await AltinTohum.Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler",
                new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Baslangic.AddDays(20 + i * 3), 25m, null, "Ödeme " + i));
        return (f, c, kart.Id);
    }

    /// <summary>Onaylı alışlara kartla yapılmış ödemeler: her biri Sync ile alışa bağlı bir kart harcaması olur.</summary>
    internal static void AlisliHarcamalar(KasaDbContext db, int kartId, int adet, DateOnly ilk)
    {
        for (var i = 0; i < adet; i++)
        {
            var tarih = ilk.AddDays(i % 250);
            db.Alislar.Add(new AlisEntity
            {
                Tarih = tarih, Tedarikci = "Tedarikçi " + i, Durum = AlisDurumlari.Onaylandi,
                Kalemler = [new() { Aciklama = "Mal", Tutar = 90m, Dagilimlar = [new() { KanalId = 1, Tutar = 50m }, new() { KanalId = 2 + i % 2, Tutar = 40m }] }],
                Odemeler = [new() { IstekId = Guid.NewGuid(), IstekOzeti = "tohum", Islem = new() { Tarih = tarih, Cari = "Tedarikçi " + i, TutarTl = 60m,
                    Kanal = Kanallar.DagilimBekliyor, Tip = GiderTipi.KrediKarti, KrediKartiId = kartId } }],
            });
        }
        db.SaveChanges();
    }

    private static async Task<IReadOnlyList<string>> Olc(SayacliFabrika f, HttpClient c, string uc)
    {
        f.Sayac.Sifirla(); f.Sayac.Etkin = true;
        try { (await c.GetAsync(uc)).EnsureSuccessStatusCode(); }
        finally { f.Sayac.Etkin = false; }
        return f.Sayac.Komutlar;
    }

    [Theory]
    [InlineData("/api/rapor/panel")]
    [InlineData("/api/rapor/haftalik")]
    [InlineData("/api/takip/kartlar/{kart}")]
    [InlineData("/api/takip/kartlar")]
    [InlineData("/api/takip/ozet")]
    public async Task Okuma_komut_sayisi_odeme_ve_alisli_harcama_sayisiyla_buyumez(string sablon)
    {
        var (kucukF, kucukC, kucukKart) = await Kur(alisliHarcama: 5, odeme: 3);
        var (buyukF, buyukC, buyukKart) = await Kur(alisliHarcama: 40, odeme: 20);
        try
        {
            var kucuk = await Olc(kucukF, kucukC, sablon.Replace("{kart}", kucukKart.ToString()));
            var buyuk = await Olc(buyukF, buyukC, sablon.Replace("{kart}", buyukKart.ToString()));
            cikti.WriteLine($"{sablon}: küçük {kucuk.Count}, büyük {buyuk.Count} komut");
            Assert.Equal(kucuk.Count, buyuk.Count);
        }
        finally { kucukC.Dispose(); buyukC.Dispose(); await kucukF.DisposeAsync(); await buyukF.DisposeAsync(); }
    }

    [Theory]
    [InlineData("/api/rapor/panel")]
    [InlineData("/api/takip/kartlar/{kart}")]
    [InlineData("/api/takip/ozet")]
    public async Task Taksit_tablosu_suzgecsiz_okunmaz_ve_istek_icinde_odemeler_kart_basina_bir_kez_okunur(string sablon)
    {
        var (f, c, kart) = await Kur(alisliHarcama: 4, odeme: 4, ikinciKartTaksiti: 300);
        try
        {
            var komutlar = await Olc(f, c, sablon.Replace("{kart}", kart.ToString()));
            var taksit = komutlar.Where(k => k.Contains("FROM \"TakipKartTaksitler\"", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(taksit);
            Assert.All(taksit, k => Assert.Contains("WHERE", k, StringComparison.Ordinal));
            var takipliKart = sablon.Contains("{kart}") ? 1 : 2;
            Assert.Equal(takipliKart, komutlar.Count(k => k.Contains("FROM \"TakipKartOdemeler\"", StringComparison.Ordinal)));
        }
        finally { c.Dispose(); await f.DisposeAsync(); }
    }

    /// <summary>Ana sayfa tekrarı (gap-okuma-yolu-maliyet-kilit-cekismesi-4): birleşik uç, panel + kasa eşikleri + takip
    /// özetini ayrı uçlarla birebir aynı üretir; tek anlık görüntüde ve tek hesap bağlamında olduğundan kartın ödemeleri
    /// bir kez okunur ve toplam komut sayısı üç ayrı isteğinkinden azdır.</summary>
    [Fact]
    public async Task Ana_sayfa_ozeti_ayri_uclarla_ayni_ve_kart_verisini_bir_kez_okur()
    {
        var (f, c, _) = await Kur(alisliHarcama: 6, odeme: 4);
        try
        {
            (await c.PutAsJsonAsync("/api/kasa-esikleri/2", new KasaEsikYaz(0, 1_000_000m, true))).EnsureSuccessStatusCode();
            var ana = System.Text.Json.Nodes.JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa?gun=7"))!;
            Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(await c.GetStringAsync("/api/rapor/panel"))!.ToJsonString(), ana["panel"]!.ToJsonString());
            Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(await c.GetStringAsync("/api/kasa-esikleri"))!.ToJsonString(), ana["kasaEsikleri"]!.ToJsonString());
            Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(await c.GetStringAsync("/api/takip/ozet?gun=7"))!.ToJsonString(), ana["takipOzeti"]!.ToJsonString());
            Assert.Contains(ana["kasaEsikleri"]!.AsArray(), e => e!["esikAltinda"]!.GetValue<bool>());

            var ayri = (await Olc(f, c, "/api/rapor/panel")).Count + (await Olc(f, c, "/api/kasa-esikleri")).Count + (await Olc(f, c, "/api/takip/ozet?gun=7")).Count;
            var birlesik = await Olc(f, c, "/api/rapor/ana-sayfa?gun=7");
            cikti.WriteLine($"Ayrı uçlar {ayri}, birleşik uç {birlesik.Count} komut");
            Assert.True(birlesik.Count < ayri, $"Birleşik {birlesik.Count}, ayrı {ayri} komut.");
            Assert.Equal(1, birlesik.Count(k => k.Contains("FROM \"TakipKartOdemeler\"", StringComparison.Ordinal)));
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/rapor/ana-sayfa?gun=0")).StatusCode);
        }
        finally { c.Dispose(); await f.DisposeAsync(); }
    }

    /// <summary>Süre notu: 3 yıllık sentetik veride (3 kart, 1.590 kart harcaması, bunların 390'ı alışa bağlı, 108 ödeme)
    /// panel, haftalık, takip özeti ve kart listesi. Veri doğrudan yazılır (kesim ekstreleri, harcama, taksit ve ödeme
    /// payları, yazma yollarının üreteceği biçimde). Süre makineye bağlıdır; yalnız çıktıya yazılır (ölçüm: her uç
    /// ≈0,1–0,3 sn ve ≈45 komut; değişiklikten önce ödeme × harcama başına sorgu ve her ödemede bütün taksit tablosu).
    /// Komut sayısı sınırı veri hacminden bağımsızdır.</summary>
    [Fact]
    public async Task Uc_yillik_sentetik_veride_sure_notu_ve_sabit_komut_sayisi()
    {
        await using var f = new SayacliFabrika();
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2023, 10, 1), kasaAcilisDevri = 500_000m })).EnsureSuccessStatusCode();
        var kartlar = new List<KartTakipDto>();
        for (var k = 0; k < 3; k++)
            kartlar.Add(await AltinTohum.Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart " + k, 5_000_000m, 5 + k * 7, 15, new(2023, 10, 1), 0m, [])));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            foreach (var kart in kartlar)
            {
                AlisliHarcamalar(db, kart.Id, 130, new(2023, 10, 5));
                var giderler = db.Islemler.Where(i => i.KrediKartiId == kart.Id).OrderBy(i => i.Id).ToList();
                // Kesim ekstreleri (38 ay), harcamalar (tek taksit, kesim ekstresine) ve alışa bağlı giderlerin harcamaları.
                var mevcut = db.TakipEkstreler.Where(e => e.KrediKartiId == kart.Id).ToList(); // kart açılışında yazılan güncel kesimler
                var ekstreler = mevcut.Concat(Enumerable.Range(0, 38).Select(ay => KesimGunu(new DateOnly(2023, 10, 1).AddMonths(ay), kart.KesimGunu))
                    .Where(kesim => mevcut.All(m => m.KesimTarihi != kesim))
                    .Select(kesim => new TakipEkstreEntity { KrediKartiId = kart.Id, KesimTarihi = kesim, SonOdemeTarihi = kesim.AddDays(10) })).OrderBy(e => e.KesimTarihi).ToList();
                db.TakipEkstreler.AddRange(ekstreler.Where(e => e.Id == 0)); db.SaveChanges();
                var harcamalar = Enumerable.Range(0, 400).Select(i => (Tarih: new DateOnly(2023, 10, 2).AddDays(i * 1080 / 400), IslemId: (int?)null, Tutar: 100m + i % 7, Kanal: 1 + i % 3))
                    .Concat(giderler.Select(g => (g.Tarih, IslemId: (int?)g.Id, Tutar: g.TutarTl, Kanal: 0))).ToList();
                var kayitlar = harcamalar.Select(h => new TakipHarcamaEntity { KrediKartiId = kart.Id, IslemId = h.IslemId, Tarih = h.Tarih, Aciklama = "Harcama",
                    Tutar = h.Tutar, DagilimJson = h.Kanal == 0 ? "[]" : $"[{{\"KanalId\":{h.Kanal},\"Tutar\":{h.Tutar.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]" }).ToList();
                db.TakipHarcamalar.AddRange(kayitlar); db.SaveChanges();
                db.TakipKartTaksitler.AddRange(kayitlar.Select(h => new TakipKartTaksitEntity { HarcamaId = h.Id, Tutar = h.Tutar,
                    EkstreId = ekstreler.First(e => e.KesimTarihi >= h.Tarih).Id }));
                db.SaveChanges();
                // Aylık 1.000 TL ödeme: en eski açık taksitlerden başlayarak (ödeme ucunun ürettiği taksit payları).
                var kalan = db.TakipKartTaksitler.AsNoTracking().Where(t => db.TakipHarcamalar.Any(h => h.Id == t.HarcamaId && h.KrediKartiId == kart.Id))
                    .OrderBy(t => t.Id).ToList().Select(t => (t.Id, Kalan: t.Tutar)).ToList();
                var sira = 0;
                for (var ay = 0; ay < 36; ay++)
                {
                    decimal tutar = 1_000m; var paylar = new List<object>();
                    while (tutar > 0 && sira < kalan.Count)
                    {
                        var pay = Math.Min(tutar, kalan[sira].Kalan);
                        paylar.Add(new { TaksitId = kalan[sira].Id, Tutar = pay, OncedenOdenen = 0m });
                        tutar -= pay; kalan[sira] = (kalan[sira].Id, kalan[sira].Kalan - pay);
                        if (kalan[sira].Kalan == 0) sira++;
                    }
                    db.TakipKartOdemeler.Add(new() { KrediKartiId = kart.Id, Tarih = new DateOnly(2023, 10, 20).AddMonths(ay), Tutar = 1_000m - tutar,
                        PaylarJson = System.Text.Json.JsonSerializer.Serialize(paylar) });
                }
                db.SaveChanges();
            }
        }
        foreach (var uc in new[] { "/api/rapor/panel", "/api/rapor/haftalik", "/api/takip/ozet", "/api/takip/kartlar" })
        {
            var sure = Stopwatch.StartNew();
            var komutlar = await Olc(f, c, uc);
            cikti.WriteLine($"{uc}: {sure.ElapsedMilliseconds} ms, {komutlar.Count} komut");
            Assert.True(komutlar.Count < 120, $"{uc} {komutlar.Count} komut çalıştırdı.");
        }
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(500_000m - 3 * 36 * 1_000m, panel.GuncelKasa);
    }

    private static DateOnly KesimGunu(DateOnly ay, int gun) => new(ay.Year, ay.Month, Math.Min(gun, DateTime.DaysInMonth(ay.Year, ay.Month)));
}
