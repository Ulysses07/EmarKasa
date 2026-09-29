using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Belge deposu biçimli yedekler ve disk koruması (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8): otomatik yedek ZIP'i belge
/// içeriği taşımaz, içerikler yedek aynasına yalnız eksik olanlar özeti doğrulanarak kopyalanır; elle indirilen yedek kendi kendine
/// yeterlidir ve restore_backup.py ile geri açılınca özetler eşleşir; yedek diskinde yer yoksa yedek alınmaz (507), hiçbir dosya
/// yazılmaz; isteğe bağlı toplam boyut sınırı en eski otomatik yedekleri siler.
/// </summary>
public class YedekBelgeTests
{
    private static readonly byte[] Fatura = "%PDF-1.7 yedeklenecek fatura"u8.ToArray();
    private static readonly byte[] Dekont = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 9, 8, 7];
    private const long Mb = 1024 * 1024;
    private static string Ozet(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private sealed class SahteDisk : IDiskAlani
    {
        public long? Yedek, Veri;
        public string VeriKoku = "";
        public long? BosAlan(string yol) => Path.GetFullPath(yol).StartsWith(VeriKoku, StringComparison.OrdinalIgnoreCase) ? Veri : Yedek;
    }

    private sealed class YedekFabrikasi : KasaWebFactory
    {
        public string Dizin { get; } = Path.Combine(Path.GetTempPath(), "kasa-yedek-belge-" + Guid.NewGuid().ToString("N"));
        public IDiskAlani? Disk { get; init; }
        public Dictionary<string, string?> Ek { get; init; } = [];
        public YedekFabrikasi() => Saat = new SabitSaat(VarsayilanBugun);
        public string Ayna => Path.Combine(Dizin, "belgeler");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            var ayarlar = new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = Dizin, ["Yedek:Etkin"] = "false", ["Bildirim:PushEtkin"] = "false", ["Bildirim:WorkerEtkin"] = "false", ["Finans:BakimEtkin"] = "false",
            };
            foreach (var (k, v) in Ek) ayarlar[k] = v;
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(ayarlar));
            if (Disk is not null) builder.ConfigureServices(s => { s.RemoveAll<IDiskAlani>(); s.AddSingleton(Disk); });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            try { if (disposing && Directory.Exists(Dizin)) Directory.Delete(Dizin, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<int> BelgeliAlis(KasaWebFactory f, HttpClient c)
    {
        var alis = (await (await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, f.Bugun, "Firma", null, []))).Content.ReadFromJsonAsync<AlisDto>())!;
        foreach (var (icerik, ad) in new[] { (Fatura, "fatura.pdf"), (Dekont, "dekont.png"), (Fatura, "fatura-yine.pdf") })
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(icerik), "dosya", ad);
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsync($"/api/alis/{alis.Id}/belgeler", form)).StatusCode);
        }
        return alis.Id;
    }

    private static async Task<string> OtomatikYedek(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return await f.Services.GetRequiredService<YedekServisi>().Olustur(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), YedekTuru.Otomatik, CancellationToken.None);
    }

    private static string[] Girdiler(ZipArchive z) => z.Entries.Select(e => e.FullName).Where(e => e != ".kasa-push-keys.json").Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Otomatik_yedek_icerik_tasimaz_aynaya_yalniz_eksikleri_dogrulayarak_kopyalar_sahipsizleri_temizler()
    {
        await using var f = new YedekFabrikasi();
        using var c = await f.EditorClientAsync();
        await BelgeliAlis(f, c);
        var depo = f.Services.GetRequiredService<BelgeDeposu>();
        var yedek = f.Services.GetRequiredService<YedekServisi>();
        // Satırı kaydedilememiş eski yükleme (sahipsiz) ve hiçbir yedeğin göstermediği ayna dosyası bakımda silinir.
        var sahipsiz = depo.Yaz("%PDF-kaydedilemeyen yukleme"u8.ToArray()).Ozet;
        File.SetLastWriteTimeUtc(depo.Yol(sahipsiz), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        var yabanci = Ozet("%PDF-eski yedekten kalma"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(f.Ayna, yabanci[..2]));
        File.WriteAllBytes(Path.Combine(f.Ayna, yabanci[..2], yabanci), "%PDF-eski yedekten kalma"u8.ToArray());

        var yol = await OtomatikYedek(f);

        Assert.Equal(2, yedek.SonYansitilanBelge);
        using (var zip = ZipFile.OpenRead(yol))
        {
            Assert.Equal(new[] { "belgeler.json", "kasa.db", "manifest.json" }, Girdiler(zip));
            JsonElement manifest;
            using (var akis = zip.GetEntry("manifest.json")!.Open()) manifest = JsonDocument.Parse(akis).RootElement.Clone();
            byte[] liste;
            using (var akis = zip.GetEntry("belgeler.json")!.Open()) { using var m = new MemoryStream(); akis.CopyTo(m); liste = m.ToArray(); }
            Assert.Equal(YedekServisi.DepoluSurum, manifest.GetProperty("surum").GetString());
            Assert.Equal(("otomatik", 2, false, false), (manifest.GetProperty("tur").GetString(), manifest.GetProperty("belgeSayisi").GetInt32(),
                manifest.GetProperty("belgelerDahil").GetBoolean(), manifest.GetProperty("belgelerGomulu").GetBoolean()));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(liste)), manifest.GetProperty("belgeListesiSha256").GetString());
            var belgeler = YedekServisi.BelgeListesiniOku(new MemoryStream(liste));
            Assert.Equal(new[] { new YedekBelgesi(Ozet(Dekont), Dekont.Length), new YedekBelgesi(Ozet(Fatura), Fatura.Length) }.OrderBy(b => b.Ozet, StringComparer.Ordinal), belgeler.Belgeler);
            Assert.Empty(belgeler.Eksik);
        }
        Assert.Equal(Fatura, File.ReadAllBytes(BelgeDeposu.DosyaYolu(f.Ayna, Ozet(Fatura))));
        Assert.Equal(Dekont, File.ReadAllBytes(BelgeDeposu.DosyaYolu(f.Ayna, Ozet(Dekont))));
        Assert.False(File.Exists(depo.Yol(sahipsiz)));
        Assert.False(File.Exists(BelgeDeposu.DosyaYolu(f.Ayna, yabanci)));
        Assert.True(depo.Var(Ozet(Fatura)));

        // İkinci yedek aynaya yeni kopya yapmaz (artımlı); ayna dosyaları yerinde durur.
        var zaman = File.GetLastWriteTimeUtc(BelgeDeposu.DosyaYolu(f.Ayna, Ozet(Fatura)));
        var ikinci = await OtomatikYedek(f);
        Assert.NotEqual(yol, ikinci);
        Assert.Equal(0, yedek.SonYansitilanBelge);
        Assert.Equal(zaman, File.GetLastWriteTimeUtc(BelgeDeposu.DosyaYolu(f.Ayna, Ozet(Fatura))));
        Assert.Equal(new[] { Ozet(Dekont), Ozet(Fatura) }.Order(StringComparer.Ordinal), BelgeDeposu.Ozetler(f.Ayna).Order(StringComparer.Ordinal));
        var durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum"))!;
        Assert.Null(durum.Hata); Assert.Null(durum.BelgeUyarisi);
        Assert.Equal(new FileInfo(yol).Length + new FileInfo(ikinci).Length + Fatura.Length + Dekont.Length, durum.ToplamYedekBayt);
    }

    [Fact]
    public async Task Elle_yedek_kendi_kendine_yeterlidir_restore_araci_belgeleri_ozetleriyle_acar()
    {
        await using var f = new YedekFabrikasi();
        using var c = await f.EditorClientAsync();
        var alisId = await BelgeliAlis(f, c);

        using var yanit = await c.PostAsync("/api/yedek", null);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.StartsWith("kasa-elle-", yanit.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var indirilen = Path.Combine(f.Dizin, "indirilen.zip");
        await File.WriteAllBytesAsync(indirilen, await yanit.Content.ReadAsByteArrayAsync());
        using (var zip = ZipFile.OpenRead(indirilen))
        {
            Assert.Equal(new[] { "belgeler.json", "belgeler/" + Ozet(Dekont), "belgeler/" + Ozet(Fatura), "kasa.db", "manifest.json" }.Order(StringComparer.Ordinal), Girdiler(zip));
            foreach (var icerik in new[] { Fatura, Dekont })
            {
                using var akis = zip.GetEntry("belgeler/" + Ozet(icerik))!.Open(); using var m = new MemoryStream(); akis.CopyTo(m);
                Assert.Equal(icerik, m.ToArray());
            }
            using var man = zip.GetEntry("manifest.json")!.Open();
            var manifest = JsonDocument.Parse(man).RootElement;
            Assert.True(manifest.GetProperty("belgelerGomulu").GetBoolean());
            Assert.Equal("elle", manifest.GetProperty("tur").GetString());
        }
        // Sunucudaki elle yedek yalnız veritabanı ve listedir: belgeler aynada, rotasyon ve disk ölçüsü bunlara göre.
        var sunucudaki = Assert.Single(Directory.GetFiles(f.Dizin, "kasa-elle-*.zip"));
        using (var zip = ZipFile.OpenRead(sunucudaki)) Assert.Equal(new[] { "belgeler.json", "kasa.db", "manifest.json" }, Girdiler(zip));

        // restore_backup.py: indirilen yedek kendi başına, sunucudaki yedek yedek aynasıyla açılır; aynasız sunucu yedeği reddedilir.
        var python = Python();
        if (python is null) return; // Python yoksa biçim yukarıda yapısal olarak doğrulandı.
        foreach (var (zipYolu, ek) in new[] { (indirilen, Array.Empty<string>()), (sunucudaki, new[] { "--belge-aynasi", f.Ayna }) })
        {
            var cikti = Path.Combine(f.Dizin, "geri-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cikti);
            var (kod, metin) = Calistir(python, [Arac(), zipYolu, "--output", Path.Combine(cikti, "kasa.db"), .. ek]);
            Assert.True(kod == 0, metin);
            Assert.Contains("2 belge içeriği", metin);
            foreach (var icerik in new[] { Fatura, Dekont })
                Assert.Equal(icerik, File.ReadAllBytes(BelgeDeposu.DosyaYolu(Path.Combine(cikti, "belgeler"), Ozet(icerik))));
            using var oku = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(cikti, "kasa.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            oku.Open();
            using var k = oku.CreateCommand(); k.CommandText = $"SELECT group_concat(IcerikOzeti, ',') FROM (SELECT IcerikOzeti FROM Belgeler WHERE AlisId = {alisId} ORDER BY Id);";
            Assert.Equal(string.Join(',', Ozet(Fatura), Ozet(Dekont), Ozet(Fatura)), k.ExecuteScalar());
        }
        var red = Path.Combine(f.Dizin, "geri-aynasiz");
        Directory.CreateDirectory(red);
        var (redKod, redMetin) = Calistir(python, [Arac(), sunucudaki, "--output", Path.Combine(red, "kasa.db")]);
        Assert.NotEqual(0, redKod);
        Assert.Contains("--belge-aynasi", redMetin);
        Assert.False(File.Exists(Path.Combine(red, "kasa.db")));
    }

    [Fact]
    public async Task Yedek_diskinde_yer_yoksa_yedek_alinmaz_507_doner_hicbir_dosya_yazilmaz_durum_uyarir()
    {
        var disk = new SahteDisk { Yedek = 100 * Mb, Veri = 1024 * Mb };
        await using var f = new YedekFabrikasi { Disk = disk };
        disk.VeriKoku = Path.GetFullPath(f.BelgeDizini);
        using var c = await f.EditorClientAsync();
        await BelgeliAlis(f, c);

        using var yanit = await c.PostAsync("/api/yedek", null);
        Assert.Equal((HttpStatusCode)507, yanit.StatusCode);
        var hata = (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString();
        Assert.Contains("yeterli boş alan yok", hata);
        Assert.Empty(Directory.Exists(f.Dizin) ? Directory.GetFiles(f.Dizin, "*", SearchOption.AllDirectories) : []);
        await Assert.ThrowsAsync<YedekDiskAlaniYetersizException>(() => OtomatikYedek(f));
        Assert.Empty(Directory.Exists(f.Dizin) ? Directory.GetFiles(f.Dizin, "*", SearchOption.AllDirectories) : []);

        var durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum"))!;
        Assert.Contains("yeterli boş alan yok", durum.Hata);
        Assert.Equal((100 * Mb, 1024 * Mb, 2048 * Mb, 0L), (durum.YedekDiskiBosAlanBayt, durum.VeriDiskiBosAlanBayt, durum.AsgariBosAlanBayt, durum.ToplamYedekBayt));
        Assert.Contains("Yedek diskinde 0,1 GB boş alan kaldı (asgari 2,0 GB)", durum.DiskUyarisi);
        Assert.Contains("Veri diskinde 1,0 GB boş alan kaldı", durum.DiskUyarisi);

        // Yer açılınca yedek alınır, hata ve uyarı kalkar.
        disk.Yedek = disk.Veri = 50L * 1024 * Mb;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/yedek", null)).StatusCode);
        durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum"))!;
        Assert.Null(durum.Hata); Assert.Null(durum.DiskUyarisi);
    }

    [Fact]
    public async Task Toplam_boyut_siniri_asilinca_en_eski_otomatik_yedekler_silinir_en_yeni_yedi_elle_ve_goc_oncesi_korunur()
    {
        await using var f = new YedekFabrikasi { Ek = { ["Yedek:AzamiToplamMb"] = "1" } };
        using var c = await f.EditorClientAsync();
        Directory.CreateDirectory(f.Dizin);
        var simdi = f.Saat!.GetUtcNow();
        string Sahte(string ad)
        {
            var yol = Path.Combine(f.Dizin, ad);
            using var zip = ZipFile.Open(yol, ZipArchiveMode.Create);
            using var akis = zip.CreateEntry("kasa.db", CompressionLevel.NoCompression).Open();
            akis.Write(RandomNumberGenerator.GetBytes(200 * 1024));
            return yol;
        }
        var otomatik = Enumerable.Range(1, 10).Select(i => Sahte(YedekSaklama.DosyaAdi(YedekTuru.Otomatik, simdi.AddDays(-i), $"{i:x8}"))).ToList();
        var elle = Sahte(YedekSaklama.DosyaAdi(YedekTuru.Elle, simdi.AddDays(-20), "0000abcd"));
        var goc = Sahte(YedekSaklama.GocOncesiDosyaAdi(simdi.AddDays(-25), "0000beef"));

        var yeni = await OtomatikYedek(f);

        // En yeni 7 otomatik (az önce alınan + 6 sahte) kalır; 4 en eski silindi; elle ve göç öncesi dokunulmadı.
        Assert.All(otomatik.Take(6).Append(yeni).Append(elle).Append(goc), y => Assert.True(File.Exists(y), Path.GetFileName(y)));
        Assert.All(otomatik.Skip(6), y => Assert.False(File.Exists(y), Path.GetFileName(y)));
        var durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum"))!;
        Assert.Equal(7, durum.OtomatikYedekSayisi);
        Assert.Contains("Yedek:AzamiToplamMb", durum.DiskUyarisi); // korunanlar yine sınırın üstünde
    }

    private static string Arac([System.Runtime.CompilerServices.CallerFilePath] string kaynak = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(kaynak)!, "..", "deploy", "restore_backup.py"));

    /// <summary>Gerçek Python yorumlayıcısı (Windows'taki uygulama mağazası kısayolu değil); yoksa null.</summary>
    private static string? Python()
    {
        foreach (var aday in new[] { "python3", "python" })
        {
            try
            {
                var (kod, metin) = Calistir(aday, ["-c", "import sys; print(sys.version_info[0])"]);
                if (kod == 0 && metin.Trim().StartsWith('3')) return aday;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }

    private static (int Kod, string Metin) Calistir(string dosya, string[] arguman)
    {
        var bilgi = new ProcessStartInfo(dosya) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        bilgi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var a in arguman) bilgi.ArgumentList.Add(a);
        using var p = Process.Start(bilgi)!;
        var cikis = p.StandardOutput.ReadToEndAsync(); var hata = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "Süreç zamanında bitmedi.");
        return (p.ExitCode, cikis.Result + hata.Result);
    }
}
