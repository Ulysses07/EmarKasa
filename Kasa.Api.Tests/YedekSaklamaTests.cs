using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Yedek saklama: otomatik ve elle yedekler ayrı adla yazılır, otomatik yedekler yaşa göre,
/// elle yedekler sayıya göre ve yalnız kendi türü içinde temizlenir. Saat sabit verilir (saklama
/// kuralına ve sunucuya); dosya zamanları File.SetLastWriteTimeUtc ile kurulur, karar dosya adındaki
/// zamana dayanır. Silinemeyen dosya enjekte edilen silme işleviyle üretilir (işletim sisteminden bağımsız).
/// </summary>
public class YedekSaklamaTests
{
    private static readonly DateTimeOffset Simdi = new(2026, 9, 27, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("kasa-20260901-030000-0a1b2c3d.zip", YedekTuru.Otomatik)] // 2.3 ve öncesinin adı: otomatik sayılır
    [InlineData("kasa-oto-20260901-030000-0a1b2c3d.zip", YedekTuru.Otomatik)]
    [InlineData("kasa-elle-20260901-030000-0a1b2c3d.zip", YedekTuru.Elle)]
    public void Eski_ve_yeni_adlar_turune_ve_zamanina_gore_taninir(string ad, YedekTuru tur)
    {
        var yedek = YedekSaklama.Tani(ad);
        Assert.NotNull(yedek);
        Assert.Equal(tur, yedek.Value.Tur);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero), yedek.Value.Zaman);
    }

    [Theory]
    [InlineData("kasa-oncesi-gecis.zip")]
    [InlineData("kasa-yedek-2026-09-27.zip")]
    [InlineData("kasa-20261399-030000-0a1b2c3d.zip")]
    [InlineData("kasa-oto-20260901-030000-0a1b2c3d.zip.part")]
    [InlineData("kasa-oto-20260901-030000-0A1B2C3D.zip")]
    [InlineData("kasa-el-20260901-030000-0a1b2c3d.zip")]
    [InlineData(".0a1b2c3d4e5f.db")]
    public void Servisin_kalibina_uymayan_dosya_yedek_sayilmaz(string ad) => Assert.Null(YedekSaklama.Tani(ad));

    [Fact]
    public void Uretilen_ad_turu_ve_zamani_tasir()
    {
        var zaman = new DateTimeOffset(2026, 9, 27, 6, 30, 15, TimeSpan.FromHours(3));
        Assert.Equal("kasa-oto-20260927-033015-0a1b2c3d.zip", YedekSaklama.DosyaAdi(YedekTuru.Otomatik, zaman, "0a1b2c3d"));
        var elle = YedekSaklama.Tani(YedekSaklama.DosyaAdi(YedekTuru.Elle, zaman, "0a1b2c3d"));
        Assert.Equal(new YedekDosyasi("kasa-elle-20260927-033015-0a1b2c3d.zip", YedekTuru.Elle, zaman), elle);
    }

    [Fact]
    public void Otomatik_saklama_son_30_gunu_ve_ayin_ilk_yedegini_tutar_elle_ve_yabanci_dosyaya_dokunmaz()
    {
        // 45 günlük geçmiş: son 10 gün yeni adla, öncesi 2.3'ün eski adıyla yazılmış.
        var otomatik = Enumerable.Range(0, 45).Select(g => Ad(g < 10 ? "oto-" : "", Simdi.AddDays(-g), g)).ToList();
        var elle = Enumerable.Range(0, 25).Select(i => Ad("elle-", Simdi.AddDays(-100 - i), 100 + i)).ToList();
        var silinecek = YedekSaklama.Silinecekler([.. otomatik, .. elle, "kasa-oncesi-gecis.zip"], YedekTuru.Otomatik, Simdi);
        // 28 Ağustos tam 30 gün önce: günlük pencerenin dışında. 14 Ağustos, Ağustos'un ilk yedeği olarak kalır.
        Assert.Equal(Enumerable.Range(30, 14).Select(g => otomatik[g]).Order(), silinecek.Order());
    }

    [Fact]
    public void Otomatik_saklama_son_12_takvim_ayinin_ilk_yedeklerini_tutar_daha_eskileri_siler()
    {
        var adlar = Enumerable.Range(0, 400).Select(g => Ad("oto-", Simdi.AddDays(-g), g)).ToList();
        var silinecek = YedekSaklama.Silinecekler(adlar, YedekTuru.Otomatik, Simdi).ToHashSet();
        var kalan = adlar.Where(a => !silinecek.Contains(a)).Select(a => YedekSaklama.Tani(a)!.Value.Zaman).Order().ToList();
        var beklenen = Enumerable.Range(0, 11).Select(i => new DateTimeOffset(2025, 10, 1, 3, 0, 0, TimeSpan.Zero).AddMonths(i)) // Ekim 2025 - Ağustos 2026
            .Concat(Enumerable.Range(0, 30).Select(g => Simdi.AddDays(-g))).Order().ToList();
        Assert.Equal(beklenen, kalan);
    }

    [Fact]
    public void Aylik_temsilci_Istanbul_takvim_ayina_gore_secilir()
    {
        var yeni = Enumerable.Range(0, 7).Select(g => Ad("oto-", Simdi.AddDays(-g), g));
        var haziranSonuUtc = Ad("oto-", new DateTimeOffset(2026, 6, 30, 22, 30, 0, TimeSpan.Zero), 100); // İstanbul'da 1 Temmuz 01:30
        var temmuz5 = Ad("oto-", new DateTimeOffset(2026, 7, 5, 3, 0, 0, TimeSpan.Zero), 101);
        var haziran15 = Ad("oto-", new DateTimeOffset(2026, 6, 15, 3, 0, 0, TimeSpan.Zero), 102);
        Assert.Equal([temmuz5], YedekSaklama.Silinecekler([.. yeni, haziranSonuUtc, temmuz5, haziran15], YedekTuru.Otomatik, Simdi));
    }

    [Fact]
    public void Uzun_kesintiden_sonra_da_en_yeni_7_otomatik_yedek_korunur()
    {
        var eski = Enumerable.Range(0, 10).Select(i => Ad("", Simdi.AddDays(-800 - i), i)).ToList();
        Assert.Equal(eski.Skip(7).Order(), YedekSaklama.Silinecekler(eski, YedekTuru.Otomatik, Simdi).Order());
    }

    [Fact]
    public void Elle_saklama_yalniz_en_yeni_10_elle_yedegi_tutar_otomatik_yedege_dokunmaz()
    {
        var otomatik = Enumerable.Range(0, 45).Select(g => Ad(g % 2 == 0 ? "oto-" : "", Simdi.AddDays(-g), g)).ToList();
        var elle = Enumerable.Range(0, 25).Select(i => Ad("elle-", Simdi.AddHours(-i), 100 + i)).ToList();
        var silinecek = YedekSaklama.Silinecekler([.. otomatik, .. elle, "kasa-oncesi-gecis.zip"], YedekTuru.Elle, Simdi);
        Assert.Equal(elle.Skip(10).Order(), silinecek.Order());
    }

    [Fact]
    public void Ayni_saniyedeki_yedeklerde_yeni_olusturulan_dosya_silinmez()
    {
        // Ad saniye duyarlıdır; art arda alınan elle yedeklerin zamanı eşitlenir ve sıra rastgele eke kalır.
        var ayniSaniye = Enumerable.Range(0, 12).Select(i => Ad("elle-", Simdi, i)).ToList();
        var yeni = ayniSaniye[0]; // en küçük ek: yalnız ada göre sıralansa silinecek olan
        var silinecek = YedekSaklama.Silinecekler(ayniSaniye, YedekTuru.Elle, Simdi, koru: yeni);
        Assert.Equal(2, silinecek.Count);
        Assert.DoesNotContain(yeni, silinecek);
    }

    [Fact]
    public async Task Otomatik_rotasyon_diskte_yas_kuralina_uymayanlari_siler_uyanlari_ve_diger_turleri_tutar()
    {
        await using var f = new YedekFabrikasi();
        Directory.CreateDirectory(f.Dizin);
        var otomatik = Enumerable.Range(0, 45).Select(g => Yaz(f.Dizin, Ad(g < 10 ? "oto-" : "", Simdi.AddDays(-g), g), Simdi.AddDays(-g))).ToList();
        var elle = Enumerable.Range(0, 12).Select(i => Yaz(f.Dizin, Ad("elle-", Simdi.AddDays(-200 - i), 100 + i), Simdi.AddDays(-200 - i))).ToList();
        var yabanci = Yaz(f.Dizin, "kasa-oncesi-gecis.zip", Simdi.AddDays(-300));

        f.Services.GetRequiredService<YedekServisi>().Dondur(YedekTuru.Otomatik, Simdi);

        var silinmesiGereken = Enumerable.Range(30, 14).Select(g => otomatik[g]).ToHashSet();
        Assert.All(otomatik, yol => Assert.Equal(!silinmesiGereken.Contains(yol), File.Exists(yol)));
        Assert.All(elle, yol => Assert.True(File.Exists(yol), "Otomatik rotasyon elle yedeği silmemeli."));
        Assert.True(File.Exists(yabanci), "Servisin üretmediği dosyaya dokunulmamalı.");
    }

    [Fact]
    public async Task Kirk_elle_yedek_otomatik_gecmisi_ve_yabanci_dosyayi_silmez()
    {
        await using var f = new YedekFabrikasi();
        Directory.CreateDirectory(f.Dizin);
        var otomatik = Enumerable.Range(0, 45).Select(g => Yaz(f.Dizin, Ad(g < 10 ? "oto-" : "", Simdi.AddDays(-g), g), Simdi.AddDays(-g))).ToList();
        var eskiElle = Enumerable.Range(0, 3).Select(i => Yaz(f.Dizin, Ad("elle-", Simdi.AddDays(-1 - i), 100 + i), Simdi.AddDays(-1 - i))).ToList();
        var yabanci = Yaz(f.Dizin, "kasa-oncesi-gecis.zip", Simdi.AddDays(-300));
        using var c = await f.EditorClientAsync();

        for (var i = 0; i < 40; i++)
        {
            using var r = await c.PostAsync("/api/yedek", null, TestContext.Current.CancellationToken);
            r.EnsureSuccessStatusCode();
            Assert.StartsWith("kasa-elle-", r.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }

        Assert.All(otomatik, yol => Assert.True(File.Exists(yol), "Elle yedek otomatik yedeği silmemeli: " + Path.GetFileName(yol)));
        Assert.True(File.Exists(yabanci));
        Assert.All(eskiElle, yol => Assert.False(File.Exists(yol), "Elle yedekler kendi aralarında dönmeli."));
        Assert.Equal(10, Directory.GetFiles(f.Dizin, "kasa-elle-*.zip").Length);

        var durum = await c.GetFromJsonAsync<JsonElement>("/api/yedek/durum", cancellationToken: TestContext.Current.CancellationToken);
        foreach (var alan in new[] { "otomatikEtkin", "sonYedek", "sonDogrulama", "hata" })
            Assert.True(durum.TryGetProperty(alan, out _), alan);
        Assert.Equal(45, durum.GetProperty("otomatikYedekSayisi").GetInt32());
        Assert.Equal(10, durum.GetProperty("elleYedekSayisi").GetInt32());
        Assert.Equal(Simdi, durum.GetProperty("sonOtomatikYedek").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.String, durum.GetProperty("sonElleYedek").ValueKind);
        Assert.Equal(JsonValueKind.String, durum.GetProperty("sonYedek").ValueKind);
        Assert.Equal(JsonValueKind.Null, durum.GetProperty("hata").ValueKind);
    }

    [Fact]
    public async Task Rotasyon_hatasi_yedegi_basarisiz_saymaz_durumda_uyari_olarak_gorunur_sonraki_yedekte_yeniden_denenir()
    {
        var kilitli = true;
        string? silinemeyen = null;
        // Silme işlevi enjekte edilir: hata yolu işletim sisteminin dosya kilidine bağlı değildir.
        await using var f = new YedekFabrikasi(silici: yol =>
        {
            if (kilitli && yol == silinemeyen)
                throw new IOException("Dosya başka bir işlem tarafından kullanılıyor.");
            File.Delete(yol);
        })
        { Saat = new SabitSaat(Simdi) };
        Directory.CreateDirectory(f.Dizin);
        var elle = Enumerable.Range(0, 10).Select(i => Yaz(f.Dizin, Ad("elle-", Simdi.AddDays(-1 - i), 100 + i), Simdi.AddDays(-1 - i))).ToList();
        silinemeyen = elle[^1];
        using var c = await f.EditorClientAsync();

        using (var r = await c.PostAsync("/api/yedek", null, TestContext.Current.CancellationToken))
            r.EnsureSuccessStatusCode();
        var durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Null(durum.Hata);
        Assert.Equal(Simdi, durum.SonDogrulama);
        Assert.NotNull(durum.RotasyonUyarisi);
        Assert.Contains(Path.GetFileName(silinemeyen), durum.RotasyonUyarisi);
        Assert.True(File.Exists(silinemeyen));
        Assert.Equal(11, Directory.GetFiles(f.Dizin, "kasa-elle-*.zip").Length);

        kilitli = false;
        using (var r = await c.PostAsync("/api/yedek", null, TestContext.Current.CancellationToken))
            r.EnsureSuccessStatusCode();
        var json = await c.GetFromJsonAsync<JsonElement>("/api/yedek/durum", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("rotasyonUyarisi").ValueKind);
        Assert.Equal(10, Directory.GetFiles(f.Dizin, "kasa-elle-*.zip").Length);
        Assert.False(File.Exists(silinemeyen));
    }

    [Fact]
    public async Task Rotasyon_uyarisi_turune_aittir_diger_turun_basarili_rotasyonu_onu_silmez()
    {
        var bozuk = true;
        await using var f = new YedekFabrikasi(silici: yol => { if (bozuk) throw new UnauthorizedAccessException("Erişim reddedildi."); File.Delete(yol); });
        Directory.CreateDirectory(f.Dizin);
        var eskiOtomatik = Yaz(f.Dizin, Ad("oto-", Simdi.AddDays(-400), 1), Simdi.AddDays(-400));
        foreach (var g in Enumerable.Range(0, 7))
            Yaz(f.Dizin, Ad("oto-", Simdi.AddDays(-g), 10 + g), Simdi.AddDays(-g));
        var yedek = f.Services.GetRequiredService<YedekServisi>();

        yedek.Dondur(YedekTuru.Otomatik, Simdi);
        var uyari = yedek.Durum().RotasyonUyarisi;
        Assert.NotNull(uyari);
        Assert.Contains("Otomatik", uyari);
        Assert.Contains(Path.GetFileName(eskiOtomatik), uyari);

        bozuk = false;
        yedek.Dondur(YedekTuru.Elle, Simdi); // silinecek elle yedek yok; otomatik uyarısı kalır
        Assert.Equal(uyari, yedek.Durum().RotasyonUyarisi);
        Assert.Null(yedek.Durum().Hata);

        yedek.Dondur(YedekTuru.Otomatik, Simdi);
        Assert.Null(yedek.Durum().RotasyonUyarisi);
        Assert.False(File.Exists(eskiOtomatik));
    }

    [Fact]
    public async Task Elle_yedek_gunluk_otomatik_yedegi_ertelemez()
    {
        await using var f = new YedekFabrikasi(otomatik: true) { Saat = new SabitSaat(Simdi) };
        Directory.CreateDirectory(f.Dizin);
        var elle = Yaz(f.Dizin, Ad("elle-", Simdi.AddMinutes(-5), 1), Simdi.AddMinutes(-5));

        _ = f.Services; // sunucuyu ve OtomatikYedek arka plan servisini başlatır
        var sure = System.Diagnostics.Stopwatch.StartNew();
        while (Directory.GetFiles(f.Dizin, "kasa-oto-*.zip").Length == 0 && sure.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(100, TestContext.Current.CancellationToken);

        // Otomatik yedek sunucunun saatiyle adlandırılır: elle yedekten 5 dakika sonra, aynı günde.
        var oto = Assert.Single(Directory.GetFiles(f.Dizin, "kasa-oto-*.zip"));
        Assert.Equal(Simdi, YedekSaklama.Tani(Path.GetFileName(oto))!.Value.Zaman);
        Assert.True(File.Exists(elle));
        var durum = f.Services.GetRequiredService<YedekServisi>().Durum();
        Assert.Equal(1, durum.OtomatikYedekSayisi);
        Assert.Equal(1, durum.ElleYedekSayisi);
        Assert.Equal(Simdi, durum.SonOtomatikYedek);
        Assert.Equal(Simdi.AddMinutes(-5), durum.SonElleYedek);
    }

    private static string Ad(string onek, DateTimeOffset zaman, int sira) => $"kasa-{onek}{zaman.UtcDateTime:yyyyMMdd-HHmmss}-{sira:x8}.zip";

    private static string Yaz(string dizin, string ad, DateTimeOffset zaman)
    {
        var yol = Path.Combine(dizin, ad);
        File.WriteAllBytes(yol, []);
        File.SetLastWriteTimeUtc(yol, zaman.UtcDateTime);
        return yol;
    }

    private sealed class YedekFabrikasi(bool otomatik = false, YedekDosyaSilici? silici = null) : KasaWebFactory
    {
        public string Dizin { get; } = Path.Combine(Path.GetTempPath(), "kasa-yedek-saklama-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = Dizin,
                ["Yedek:Etkin"] = otomatik ? "true" : "false",
                ["Bildirim:PushEtkin"] = "false",
                ["Bildirim:WorkerEtkin"] = "false",
                ["Bildirim:AnahtarDosyasi"] = Path.Combine(Dizin, ".kasa-push-keys.json")
            }));
            if (silici is not null)
                builder.ConfigureServices(services => services.AddSingleton(silici));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(Dizin))
                Directory.Delete(Dizin, true);
        }
    }
}
