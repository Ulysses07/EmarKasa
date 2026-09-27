using System.Data.Common;
using System.Net.Http.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>Bildirim işçisinin tur maliyeti, hata yalıtımı ve görünürlüğü. Saat sabittir (<see cref="Saat"/>).</summary>
public sealed class BildirimIscisiTests
{
    private static readonly DateOnly Gun = new(2026, 9, 23);
    private static TakipOlayDto Olay(int id) => new("Kart", id, id, "Kart " + id, Gun, 125m, "SonOdeme", false);

    [Fact]
    public async Task TurBasinaHesapAbonelikVeTeslimSayisindanBagimsizdir()
    {
        // Aynı veriyle yalnız Yenile (tek hesap), 1 cihazlı ve 3 cihazlı tur: kaynak ve kasa eşiği hesabı sayısı aynı kalmalı.
        async Task<(int Okuma, int Esik, int Gonderim)> Calistir(int cihaz, bool yalnizYenile)
        {
            using var f = new Fikstur(); f.EsikAltindaKanal();
            for (var i = 0; i < cihaz; i++) f.Cihaz("c" + i);
            f.Kaynak.Olaylar = [Olay(1), Olay(2)];
            f.Sayac.Esik = 0;
            if (yalnizYenile) await f.Servis().Yenile(); else await f.Servis().Gonder();
            return (f.Kaynak.Okuma, f.Sayac.Esik, f.Gonderici.Hedefler.Count);
        }
        var tekHesap = await Calistir(1, yalnizYenile: true);
        var tek = await Calistir(1, false); var cok = await Calistir(3, false);
        Assert.Equal(1, tekHesap.Okuma); Assert.True(tekHesap.Esik > 0);
        Assert.Equal(1, tek.Okuma); Assert.Equal(1, cok.Okuma);
        Assert.Equal(tekHesap.Esik, tek.Esik); Assert.Equal(tekHesap.Esik, cok.Esik);
        // Cihaz başına iki kart hatırlatması ve bir kasa alt sınırı uyarısı.
        Assert.Equal(3, tek.Gonderim); Assert.Equal(9, cok.Gonderim);
    }

    [Fact]
    public async Task DevreDisiAbonelikTeslimiTekrarHesaplatmazVeKiralanmaz()
    {
        using var f = new Fikstur(); f.Cihaz("a"); f.Cihaz("b");
        f.Kaynak.Olaylar = [Olay(1), Olay(2)];
        f.Gonderici.SonucSec = s => s.Endpoint.EndsWith("/a", StringComparison.Ordinal) ? PushSonuc.AbonelikBitti : PushSonuc.Basarili;
        await f.Servis().Gonder();
        Assert.Equal(1, f.Gonderici.Hedefler.Count(x => x.EndsWith("/a", StringComparison.Ordinal)));
        Assert.Equal(2, f.Gonderici.Hedefler.Count(x => x.EndsWith("/b", StringComparison.Ordinal)));
        var okuma = f.Kaynak.Okuma;
        var aId = f.Db.Set<PushAbonelikEntity>().AsNoTracking().Single(x => x.CihazAdi == "a").Id;
        // a'nın ikinci teslimi, abonelik kapandığı için kalıcı iptal edilir; deneme hakkı geri verilip her dakika dönmez.
        var bekleyen = f.Db.Set<BildirimTeslimEntity>().AsNoTracking().Where(x => x.AbonelikId == aId).OrderBy(x => x.Id).Last();
        Assert.Null(bekleyen.Gonderildi); Assert.True(bekleyen.Iptal); Assert.Null(bekleyen.Kilit);
        for (var i = 0; i < 3; i++) { f.Saat.Utc = f.Saat.Utc.AddMinutes(2); await f.Servis().Gonder(); }
        Assert.Equal(okuma + 3, f.Kaynak.Okuma);
        Assert.Equal(1, f.Gonderici.Hedefler.Count(x => x.EndsWith("/a", StringComparison.Ordinal)));
        Assert.Equal(bekleyen.Deneme, f.Db.Set<BildirimTeslimEntity>().AsNoTracking().Single(x => x.Id == bekleyen.Id).Deneme);
    }

    [Fact]
    public async Task EskiOturumluCihazinBekleyenTeslimiKiralanmazDigerCihazaGider()
    {
        using var f = new Fikstur(); f.Cihaz("a"); f.Cihaz("b");
        f.Kaynak.Olaylar = [Olay(1)];
        f.Gonderici.Sonuc = PushSonuc.GeciciHata;
        await f.Servis().Gonder(); // iki teslim de geçici hatayla bekliyor (Deneme 1)
        // a'nın oturumu parola değişikliğiyle eskidi.
        f.Db.Set<PushAbonelikEntity>().Where(x => x.CihazAdi == "a").ExecuteUpdate(p => p.SetProperty(x => x.OturumDamgasi, "eski-oturum"));
        f.Gonderici.Sonuc = PushSonuc.Basarili; f.Gonderici.Hedefler.Clear();
        var okuma = f.Kaynak.Okuma;
        for (var i = 0; i < 3; i++) { f.Saat.Utc = f.Saat.Utc.AddMinutes(3); await f.Servis().Gonder(); }
        Assert.Equal(okuma + 3, f.Kaynak.Okuma);
        Assert.Equal(["https://fcm.googleapis.com/fcm/send/b"], f.Gonderici.Hedefler);
        var aId = f.Db.Set<PushAbonelikEntity>().AsNoTracking().Single(x => x.CihazAdi == "a").Id;
        Assert.False(f.Db.Set<PushAbonelikEntity>().AsNoTracking().Single(x => x.Id == aId).Etkin);
        var bekleyen = f.Db.Set<BildirimTeslimEntity>().AsNoTracking().Single(x => x.AbonelikId == aId);
        Assert.Equal(1, bekleyen.Deneme); Assert.Null(bekleyen.Gonderildi); Assert.Null(bekleyen.Kilit);
    }

    [Fact]
    public async Task KapaliAboneliginBekleyenTeslimleriTurKotasiniTuketmez()
    {
        using var f = new Fikstur(); f.Cihaz("a");
        f.Kaynak.Olaylar = Enumerable.Range(1, 120).Select(Olay).ToList();
        f.Gonderici.Sonuc = PushSonuc.GeciciHata;
        await f.Servis().Gonder(); // a'nın 120 teslimi oluşur; 100'ü geçici hatayla ertelenir.
        f.Db.Set<PushAbonelikEntity>().ExecuteUpdate(p => p.SetProperty(x => x.Etkin, false)); // cihaz kaldırıldı
        f.Cihaz("b"); f.Gonderici.Sonuc = PushSonuc.Basarili; f.Gonderici.Hedefler.Clear();
        f.Saat.Utc = f.Saat.Utc.AddMinutes(2);
        await f.Servis().Gonder();
        Assert.Equal(100, f.Gonderici.Hedefler.Count(x => x.EndsWith("/b", StringComparison.Ordinal)));
        Assert.DoesNotContain(f.Gonderici.Hedefler, x => x.EndsWith("/a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GondericiIstisnasiDigerCihazlariDurdurmazVeAbonelikKimligiyleLoglanir()
    {
        using var f = new Fikstur(); f.Cihaz("a"); f.Cihaz("b");
        f.Kaynak.Olaylar = [Olay(1)];
        f.Gonderici.Istisna = s => s.Endpoint.EndsWith("/a", StringComparison.Ordinal) ? new InvalidOperationException("beklenmeyen") : null;
        await f.Servis().Gonder();
        Assert.Contains(f.Gonderici.Hedefler, x => x.EndsWith("/b", StringComparison.Ordinal));
        var aId = f.Db.Set<PushAbonelikEntity>().AsNoTracking().Single(x => x.CihazAdi == "a").Id;
        var teslim = f.Db.Set<BildirimTeslimEntity>().AsNoTracking().Single(x => x.AbonelikId == aId);
        // Beklenmeyen istisna geçici sayılır: kilit bırakılır, teslim iptal edilmez, sonraki denemeye kalır.
        Assert.False(teslim.Iptal); Assert.Null(teslim.Kilit); Assert.Null(teslim.Gonderildi);
        var kayit = Assert.Single(f.Log.Kayitlar, x => x.Seviye == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(kayit.Istisna);
        Assert.Contains($"#{aId}", kayit.Mesaj);
    }

    [Fact]
    public async Task DisaridanYazmaTeslimOncesiYenidenHesaplatirYazmaYoksaTurHesabiKullanilir()
    {
        // Dosya veritabanı: işçinin bağlantısı dışındaki commit PRAGMA data_version'u değiştirir.
        async Task<(int Okuma, int Gonderim)> Calistir(bool disYazma)
        {
            using var f = new Fikstur(dosya: true); f.Cihaz("a"); f.Cihaz("b");
            f.Kaynak.Olaylar = [Olay(1)];
            f.Gonderici.Gonderirken = () =>
            {
                // İlk teslim sırasında borç ödendi: kaynak artık olay üretmiyor.
                f.Kaynak.Olaylar = [];
                if (disYazma)
                {
                    // Başka bir bağlantının (ör. ödeme kaydeden isteğin) commit'i.
                    using var baska = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(f.BaglantiMetni).Options);
                    baska.Kanallar.Add(new() { Ad = "Dış yazma" }); baska.SaveChanges();
                }
            };
            await f.Servis().Gonder();
            return (f.Kaynak.Okuma, f.Gonderici.Hedefler.Count);
        }
        var yazmali = await Calistir(disYazma: true);
        Assert.Equal(2, yazmali.Okuma); Assert.Equal(1, yazmali.Gonderim);
        var yazmasiz = await Calistir(disYazma: false);
        Assert.Equal(1, yazmasiz.Okuma); Assert.Equal(2, yazmasiz.Gonderim);
    }

    [Fact]
    public async Task IsciTurHatasiniIstisnaNesnesiyleLoglarVeCalismayaDevamEder()
    {
        using var f = new Fikstur();
        f.Kaynak.Hata = new InvalidOperationException("kaynak bozuk");
        await using var sp = f.IsciServisleri();
        var isci = ActivatorUtilities.CreateInstance<BildirimWorker>(sp);
        using var dur = new CancellationTokenSource();
        await isci.StartAsync(dur.Token);
        var kayit = await f.Log.IlkHata.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await isci.StopAsync(CancellationToken.None);
        Assert.Equal(LogLevel.Error, kayit.Seviye);
        Assert.Same(f.Kaynak.Hata, kayit.Istisna);
        Assert.DoesNotContain(f.Log.Kayitlar, x => x.Seviye == LogLevel.Warning && x.Istisna is null);
    }

    [Fact]
    public async Task GondericiBozukSunucuAnahtarindaIstisnaFirlatmazYapilandirmaHatasiDoner()
    {
        var log = new LogToplayici();
        await using var sp = GondericiServisleri(log, new() { ["Bildirim:PublicKey"] = "kisa", ["Bildirim:PrivateKey"] = "kisa" });
        using var gonderici = ActivatorUtilities.CreateInstance<WebPushGonderici>(sp);
        var abonelik = new PushAbonelikEntity { Id = 7, Endpoint = "https://fcm.googleapis.com/fcm/send/anahtar-testi" };
        for (var i = 0; i < 3; i++)
            Assert.Equal(PushSonuc.YapilandirmaHatasi, await gonderici.Gonder(abonelik, new(1, "Başlık", "Mesaj", "/#home", "kasa-1"), 60, CancellationToken.None));
        // Her tur aynı hatayı üretir; yığın izi bir kez yazılır.
        var kayit = Assert.Single(log.Kayitlar, x => x.Seviye == LogLevel.Error);
        Assert.IsType<ArgumentException>(kayit.Istisna);
    }

    [Fact]
    public async Task GondericiBozukAnahtarDosyasindaYapilandirmaHatasiDoner()
    {
        var dosya = Path.Combine(Path.GetTempPath(), $"kasa-push-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(dosya, "{bozuk");
        try
        {
            var log = new LogToplayici();
            await using var sp = GondericiServisleri(log, new() { ["Bildirim:AnahtarDosyasi"] = dosya });
            using var gonderici = ActivatorUtilities.CreateInstance<WebPushGonderici>(sp);
            var abonelik = new PushAbonelikEntity { Id = 7, Endpoint = "https://fcm.googleapis.com/fcm/send/dosya-testi" };
            Assert.Equal(PushSonuc.YapilandirmaHatasi, await gonderici.Gonder(abonelik, new(1, "Başlık", "Mesaj", "/#home", "kasa-1"), 60, CancellationToken.None));
            Assert.Contains(log.Kayitlar, x => x.Seviye == LogLevel.Error && x.Istisna is System.Text.Json.JsonException);
        }
        finally { File.Delete(dosya); }
    }

    [Fact]
    public async Task YapilandirmaHatasiDenemeHakkiHarcamazTuruDurdururVeDuzelinceTemizlenir()
    {
        using var f = new Fikstur(); f.Cihaz("a"); f.Cihaz("b");
        f.Kaynak.Olaylar = [Olay(1)];
        f.Gonderici.Sonuc = PushSonuc.YapilandirmaHatasi;
        await f.Servis().Gonder();
        // İlk cihazda anahtar hatası: ikinci cihaz denenmez, teslimler iptal edilmez ve deneme hakkı harcanmaz.
        Assert.Single(f.Gonderici.Hedefler);
        Assert.All(f.Db.Set<BildirimTeslimEntity>().AsNoTracking().ToList(), t => { Assert.False(t.Iptal); Assert.Equal(0, t.Deneme); Assert.Null(t.Kilit); });
        Assert.Contains("anahtar", f.Saglik.Son?.Mesaj);
        for (var i = 0; i < 6; i++) { f.Saat.Utc = f.Saat.Utc.AddMinutes(1); await f.Servis().Gonder(); }
        Assert.All(f.Db.Set<BildirimTeslimEntity>().AsNoTracking().ToList(), t => Assert.Equal(0, t.Deneme));
        // Anahtar düzelince bugünkü hatırlatmalar kaybolmadan gider ve görünür hata temizlenir.
        f.Gonderici.Sonuc = PushSonuc.Basarili; f.Gonderici.Hedefler.Clear();
        f.Saat.Utc = f.Saat.Utc.AddMinutes(1); await f.Servis().Gonder();
        Assert.Equal(2, f.Gonderici.Hedefler.Count);
        Assert.Null(f.Saglik.Son);
    }

    [Fact]
    public async Task IsciArdisikHatalariSayarVeDuzelinceGorunurHatayiTemizler()
    {
        using var f = new Fikstur();
        f.Kaynak.Hata = new InvalidOperationException("kaynak bozuk");
        await using var sp = f.IsciServisleri();
        var isci = ActivatorUtilities.CreateInstance<BildirimWorker>(sp);
        await isci.Tur(CancellationToken.None); await isci.Tur(CancellationToken.None);
        var hatalar = f.Log.Kayitlar.Where(x => x.Seviye == LogLevel.Error && x.Kategori.EndsWith(nameof(BildirimWorker), StringComparison.Ordinal)).ToList();
        Assert.Equal(2, hatalar.Count);
        Assert.Contains("1. ardışık hata", hatalar[0].Mesaj); Assert.Contains("2. ardışık hata", hatalar[1].Mesaj);
        Assert.All(hatalar, x => Assert.Contains("iz ", x.Mesaj));
        Assert.StartsWith("Bildirim denetimi tamamlanamadı", f.Saglik.Son?.Mesaj);
        f.Kaynak.Hata = null;
        await isci.Tur(CancellationToken.None);
        Assert.Null(f.Saglik.Son);
        Assert.Contains(f.Log.Kayitlar, x => x.Seviye == LogLevel.Information && x.Mesaj.Contains("2 ardışık hatadan sonra", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestBildirimiYapilandirmaHatasindaAnlasilirYanitVerirVeAyarlardaGorunur()
    {
        await using var f = new GondericiliFabrika(new SabitGonderici(PushSonuc.YapilandirmaHatasi));
        var c = await f.EditorClientAsync();
        const string endpoint = "https://fcm.googleapis.com/fcm/send/test-yapilandirma";
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            db.Add(new PushAbonelikEntity { Endpoint = endpoint, CihazId = Guid.NewGuid().ToString(), CihazAdi = "Test",
                OturumDamgasi = OturumDamgasi.Uret("editor", cfg, db)! }); db.SaveChanges();
        }
        var yanit = await c.PostAsJsonAsync("/api/bildirimler/test", new PushEndpointYaz(endpoint));
        Assert.Equal(System.Net.HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(govde.GetProperty("basarili").GetBoolean());
        Assert.Contains("bildirim anahtarı", govde.GetProperty("mesaj").GetString());
        var ayar = await c.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/bildirimler/ayarlar");
        Assert.Contains("anahtar", ayar.GetProperty("sonHata").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.String, ayar.GetProperty("sonHataZamani").ValueKind);
    }

    [Fact]
    public async Task BozukKartDigerKartinHatirlatmasiniDurdurmazVeEditoreHataBildirimiOlusur()
    {
        var bugun = KasaWebFactory.VarsayilanBugun;
        var baslangic = new DateOnly(bugun.Year, bugun.Month, 1).AddMonths(-2);
        await using var f = KasaWebFactory.Sabit(bugun); using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        // İki kartın da kesim günü bugün: sağlam kartın "kesim günü" hatırlatması üretilir.
        var bozuk = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Bozuk kart", 10000m, bugun.Day, 5, baslangic, 0, []));
        var saglam = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Sağlam kart", 10000m, bugun.Day, 5, baslangic, 0, []));
        var bozukHarcamali = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{bozuk.Id}/harcamalar",
            new KartHarcamaYaz(Guid.NewGuid(), bozuk.Surum, baslangic, "Malzeme", 100m, 1, null, [new(1, 60m), new(2, 40m)]));
        // Kart ödemesi kasadan harcamanın dağılımıyla kanallara bölünerek çıkar: kasa paneli bu dağılıma bağlıdır.
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{bozuk.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), bozukHarcamali.Surum, bugun, 30m));
        // Taksidi 3 gün sonra olan kredi: "ödemeye 3 gün kaldı" hatırlatması bozuk karttan etkilenmemeli.
        var ilkTaksit = bugun.AddDays(3).AddMonths(-1);
        var kredi = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Kredi", 100m, ilkTaksit.AddDays(-1), ilkTaksit, 3, 10m, [1]));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        // Sağlıklı veride işçinin yalıtılmış kaynağı, takip özetinin kullandığı olaylarla birebir aynıdır.
        var saglikli = new List<BildirimKaynakHatasi>();
        Assert.Equal(FinansTakipServisi.GetNotificationEvents(db, bugun), new FinansBildirimKaynaklari().Oku(db, bugun, saglikli));
        Assert.Empty(saglikli);
        db.Database.ExecuteSql($"UPDATE TakipHarcamalar SET DagilimJson = '{{bozuk' WHERE KrediKartiId = {bozuk.Id}");
        // Bozuk dağılım kartın hesabını düşürür; kartın ödemesi bu dağılımla bölündüğünden ve eşik tanımlı olduğundan kasa
        // paneli (tüm kartlar) de düşer. Panel yalnız ödeme paylarını hesaplar: ödemesiz kartın bozuk harcaması paneli düşürmez.
        var kanal = db.Kanallar.AsNoTracking().OrderBy(k => k.Id).First().Id;
        db.KasaEsikleri.Add(new() { KanalId = kanal, Etkin = true, Tutar = 1_000_000m }); db.SaveChanges();

        var log = new LogToplayici();
        var servis = ActivatorUtilities.CreateInstance<BildirimServisi>(scope.ServiceProvider, new LoggerFactory([log]).CreateLogger<BildirimServisi>());
        await servis.Yenile();

        var satirlar = db.Set<BildirimEntity>().AsNoTracking().Where(x => !x.Iptal).ToList();
        Assert.Contains(satirlar, x => x.Tur == "Kesim" && x.KaynakId == saglam.Id);
        Assert.DoesNotContain(satirlar, x => x.Tur != "Hata" && x.KaynakId == bozuk.Id && x.Hedef.StartsWith("/#cards/", StringComparison.Ordinal));
        var kartHatasi = Assert.Single(satirlar, x => x.Tur == "Hata" && x.Hedef == $"/#cards/{bozuk.Id}");
        Assert.Equal("Kayıt hesaplanamadı", kartHatasi.Baslik);
        Assert.Contains("Bozuk kart", kartHatasi.Mesaj);
        Assert.Contains(satirlar, x => x.Tur == "Hata" && x.Hedef == "/#home");
        Assert.Contains(satirlar, x => x.Tur == "Taksit" && x.KaynakId == kredi.Id);
        var kartKaydi = Assert.Single(log.Kayitlar, x => x.Seviye == LogLevel.Error && x.Mesaj.Contains($"Kart #{bozuk.Id}", StringComparison.Ordinal));
        Assert.IsType<System.Text.Json.JsonException>(kartKaydi.Istisna);
        Assert.Contains(servis.Iz, kartKaydi.Mesaj);
        Assert.Contains(log.Kayitlar, x => x.Seviye == LogLevel.Error && x.Mesaj.Contains("KasaEsik", StringComparison.Ordinal) && x.Istisna is not null);
    }

    private static ServiceProvider GondericiServisleri(LogToplayici log, Dictionary<string, string?> ayarlar)
    {
        ayarlar["Bildirim:PushEtkin"] = "true";
        return new ServiceCollection().AddLogging(l => l.AddProvider(log))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ayarlar).Build())
            .AddSingleton<IWebHostEnvironment>(new Ortam()).AddSingleton<PushKimligi>().BuildServiceProvider();
    }

    private sealed class SabitGonderici(PushSonuc sonuc) : IPushGonderici
    {
        public Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct) => Task.FromResult(sonuc);
    }
    /// <summary>Gerçek sağlayıcı yerine verilen göndericiyi kullanan uygulama.</summary>
    private sealed class GondericiliFabrika(IPushGonderici gonderici) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => { s.RemoveAll<IPushGonderici>(); s.AddSingleton(gonderici); });
        }
    }

    private static async Task<T> Post<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed class Saat : TimeProvider
    {
        public DateTimeOffset Utc = new(2026, 9, 23, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class SayanKaynak : IBildirimKaynaklari
    {
        public IReadOnlyList<TakipOlayDto> Olaylar = [];
        public Exception? Hata;
        public int Okuma;
        public IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today, ICollection<BildirimKaynakHatasi> hatalar)
        { Okuma++; if (Hata is not null) throw Hata; return Olaylar; }
    }
    private sealed class Gonderici : IPushGonderici
    {
        public readonly List<string> Hedefler = [];
        public PushSonuc Sonuc = PushSonuc.Basarili;
        public Func<PushAbonelikEntity, PushSonuc>? SonucSec;
        public Func<PushAbonelikEntity, Exception?>? Istisna;
        public Action? Gonderirken;
        public Task<PushSonuc> Gonder(PushAbonelikEntity s, PushIleti m, int ttl, CancellationToken ct)
        {
            if (Istisna?.Invoke(s) is { } e) throw e;
            Hedefler.Add(s.Endpoint); Gonderirken?.Invoke();
            return Task.FromResult(SonucSec?.Invoke(s) ?? Sonuc);
        }
    }
    /// <summary>Kasa alt sınırı hesabının (KasaEsikServisi) çalıştırdığı komutları sayar.</summary>
    private sealed class KomutSayaci : DbCommandInterceptor
    {
        public int Esik;
        private void Say(DbCommand c) { if (c.CommandText.Contains("\"KasaEsikleri\"", StringComparison.Ordinal)) Interlocked.Increment(ref Esik); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r) { Say(c); return r; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Say(c); return ValueTask.FromResult(r); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r) { Say(c); return r; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default) { Say(c); return ValueTask.FromResult(r); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r) { Say(c); return r; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Say(c); return ValueTask.FromResult(r); }
    }
    internal sealed record LogKaydi(LogLevel Seviye, string Kategori, string Mesaj, Exception? Istisna, int OlayId = 0);
    internal sealed class LogToplayici : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<LogKaydi> Kayitlar { get; } = new();
        public TaskCompletionSource<LogKaydi> IlkHata { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ILogger CreateLogger(string kategori) => new Yazici(this, kategori);
        public void Dispose() { }
        private sealed class Yazici(LogToplayici sahip, string kategori) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel seviye, EventId id, TState state, Exception? istisna, Func<TState, Exception?, string> bicim)
            {
                var kayit = new LogKaydi(seviye, kategori, bicim(state, istisna), istisna, id.Id);
                sahip.Kayitlar.Enqueue(kayit);
                if (seviye >= LogLevel.Error) sahip.IlkHata.TrySetResult(kayit);
            }
        }
    }
    private sealed class Ortam : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Kasa.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
    }
    private sealed class Fikstur : IDisposable
    {
        private readonly SqliteConnection? bellek;
        private readonly string? dosya;
        private readonly ServiceProvider uygulama;
        public readonly KasaDbContext Db;
        public readonly string BaglantiMetni;
        public readonly SayanKaynak Kaynak = new(); public readonly Gonderici Gonderici = new(); public readonly Saat Saat = new();
        public readonly KomutSayaci Sayac = new(); public readonly LogToplayici Log = new();
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Kasa:EditorKullanici"] = "editor", ["Kasa:EditorSifre"] = "test", ["Kasa:JwtKey"] = "notification-tests-only-long-enough-key",
          ["Bildirim:WorkerEtkin"] = "true" }).Build();
        public Fikstur(bool dosya = false)
        {
            // Bağlamın "bugün"ü (db.Bugunu) da fikstürün sabit saatinden okunur.
            uygulama = new ServiceCollection().AddSingleton<TimeProvider>(Saat).BuildServiceProvider();
            var secenek = new DbContextOptionsBuilder<KasaDbContext>().AddInterceptors(Sayac).UseApplicationServiceProvider(uygulama);
            if (dosya)
            {
                this.dosya = Path.Combine(Path.GetTempPath(), $"kasa-bildirim-{Guid.NewGuid():N}.db");
                BaglantiMetni = $"Data Source={this.dosya};Pooling=False";
                Db = new(secenek.UseSqlite(BaglantiMetni).Options);
            }
            else
            {
                BaglantiMetni = "Data Source=:memory:";
                bellek = new(BaglantiMetni); bellek.Open();
                Db = new(secenek.UseSqlite(bellek).Options);
            }
            Db.Database.Migrate();
        }
        public void Cihaz(string ek)
        {
            Db.Add(new PushAbonelikEntity { Endpoint = "https://fcm.googleapis.com/fcm/send/" + ek,
                CihazId = Guid.NewGuid().ToString(), CihazAdi = ek, OturumDamgasi = OturumDamgasi.Uret("editor", Config, Db)!,
                Olusturuldu = Saat.GetUtcNow().ToUnixTimeSeconds() }); Db.SaveChanges(); Db.ChangeTracker.Clear();
        }
        public void EsikAltindaKanal()
        {
            Db.Ayarlar.Add(new() { TakipBaslangic = Gun.AddDays(-10) });
            var kanal = new KanalEntity { Ad = "Kanal" }; Db.Kanallar.Add(kanal); Db.SaveChanges();
            Db.KasaEsikleri.Add(new() { KanalId = kanal.Id, Etkin = true, Tutar = 100m }); Db.SaveChanges(); Db.ChangeTracker.Clear();
        }
        public readonly BildirimSagligi Saglik = new();
        public BildirimServisi Servis() => new(Db, Kaynak, Gonderici, Config, Saat, Saglik, new LoggerFactory([Log]).CreateLogger<BildirimServisi>());
        public ServiceProvider IsciServisleri() => new ServiceCollection()
            .AddLogging(l => l.AddProvider(Log)).AddSingleton(Config).AddSingleton<IWebHostEnvironment>(new Ortam())
            .AddSingleton<TimeProvider>(Saat).AddSingleton(Saglik).AddScoped(_ => Servis())
            .BuildServiceProvider();
        public void Dispose()
        {
            Db.Dispose(); bellek?.Dispose(); uygulama.Dispose();
            if (dosya is not null) foreach (var yol in new[] { dosya, dosya + "-wal", dosya + "-shm", dosya + "-journal" })
                try { File.Delete(yol); } catch (IOException) { }
        }
    }
}
