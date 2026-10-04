using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Kasa.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class GuvenlikGunluguKesimTests
{
    private static string GeciciGunluk() => Path.Combine(Path.GetTempPath(),
        "kasa-gunluk-kesim-" + Guid.NewGuid().ToString("N"), GuvenlikGunlugu.DosyaAdi);

    private static WebApplicationFactory<Program> Ac(KasaWebFactory temel, string yol) => temel.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GuvenlikGunlugu:Etkin"] = "true",
            ["GuvenlikGunlugu:Yol"] = yol,
        })));

    private static void Temizle(string yol)
    {
        var dizin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(yol)!));
        var ad = Path.GetFileName(dizin);
        const string onEk = "kasa-gunluk-kesim-";
        var geciciKok = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var kiyas = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(dizin), geciciKok, kiyas)
            || !ad.StartsWith(onEk, StringComparison.Ordinal)
            || !Guid.TryParseExact(ad[onEk.Length..], "N", out _))
            throw new InvalidOperationException($"Test dışı dizin silinemez: {dizin}");
        if (Directory.Exists(dizin))
            Directory.Delete(dizin, recursive: true);
    }

    [Fact]
    public void Gecerli_son_satirin_kesilmesi_acilista_reddedilir()
    {
        var yol = GeciciGunluk();
        try
        {
            using (var temel = new KasaWebFactory())
            using (var host = Ac(temel, yol))
            using (var istemci = host.CreateClient())
                host.Services.GetRequiredService<GuvenlikGunlugu>().Yaz(GuvenlikGunlugu.EditorSifresiDegisti, zorunlu: true);

            var baytlar = File.ReadAllBytes(yol);
            var ilkSatirSonu = Array.IndexOf(baytlar, (byte)'\n') + 1;
            Assert.True(ilkSatirSonu > 0 && ilkSatirSonu < baytlar.Length);
            File.WriteAllBytes(yol, baytlar[..ilkSatirSonu]);

            using var ikinciTemel = new KasaWebFactory();
            using var ikinciHost = Ac(ikinciTemel, yol);
            var hata = Record.Exception(() => ikinciHost.CreateClient());
            Assert.NotNull(hata);
            Assert.Contains("Güvenlik günlüğünün bütünlüğü doğrulanamadı", hata.ToString(), StringComparison.Ordinal);
        }
        finally { Temizle(yol); }
    }

    [Fact]
    public void Eski_surum_kontrolsuz_satir_eklerse_acilis_durur()
    {
        var yol = GeciciGunluk();
        try
        {
            using (var temel = new KasaWebFactory())
            using (var host = Ac(temel, yol))
            using (var istemci = host.CreateClient())
                host.Services.GetRequiredService<GuvenlikGunlugu>().Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti, zorunlu: true);

            File.AppendAllText(yol, "{\"zaman\":\"2026-10-04T00:00:00+00:00\",\"tur\":\"EditorSifresiDegisti\"}\n");
            using var ikinciTemel = new KasaWebFactory();
            using var ikinciHost = Ac(ikinciTemel, yol);
            var hata = Record.Exception(() => ikinciHost.CreateClient());
            Assert.NotNull(hata);
            Assert.Contains("Güvenlik günlüğünün bütünlüğü doğrulanamadı", hata.ToString(), StringComparison.Ordinal);
        }
        finally { Temizle(yol); }
    }

    [Fact]
    public void Eski_gunluk_yeniden_yazilmadan_kapsama_baslangici_kaydedilir()
    {
        var yol = GeciciGunluk();
        try
        {
            using (var temel = new KasaWebFactory())
            using (var host = Ac(temel, yol))
            using (var istemci = host.CreateClient())
                host.Services.GetRequiredService<GuvenlikGunlugu>().Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti, zorunlu: true);

            var onceki = File.ReadAllBytes(yol);
            File.Delete(yol + GuvenlikGunlugu.KontrolDosyasiEki);
            using var ikinciTemel = new KasaWebFactory();
            using var ikinciHost = Ac(ikinciTemel, yol);
            using var ikinciIstemci = ikinciHost.CreateClient();
            var gunluk = ikinciHost.Services.GetRequiredService<GuvenlikGunlugu>();
            Assert.Equal(onceki, File.ReadAllBytes(yol));
            Assert.Equal(onceki.LongLength, gunluk.Oku()!.DogrulamaBaslangiciBayt);
            var kesim = gunluk.KesimNoktasiAl();
            Assert.Equal(onceki.LongLength, kesim.Bayt);
            gunluk.Yaz(GuvenlikGunlugu.EditorSifresiDegisti, zorunlu: true);
            Assert.Equal(GuvenlikGunlugu.EditorSifresiDegisti, Assert.Single(gunluk.KesimdenSonraOku(kesim).Olaylar).Tur);
            Assert.Throws<InvalidDataException>(() => gunluk.KesimdenSonraOku(new GuvenlikGunlugu.KesimNoktasi(0,
                Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())))));
        }
        finally { Temizle(yol); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Yarim_kalmis_kontrol_guncellemesi_eski_veya_tam_yeni_satirdan_kurtarilir(bool satirYazildi)
    {
        var yol = GeciciGunluk();
        try
        {
            using (var temel = new KasaWebFactory())
            using (var host = Ac(temel, yol))
            using (var istemci = host.CreateClient())
                host.Services.GetRequiredService<GuvenlikGunlugu>().Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti, zorunlu: true);

            var onceki = File.ReadAllBytes(yol);
            var ek = Encoding.UTF8.GetBytes("{\"zaman\":\"2026-10-04T00:00:00+00:00\",\"tur\":\"EditorSifresiDegisti\"}\n");
            var yeni = new byte[onceki.Length + ek.Length];
            onceki.CopyTo(yeni, 0);
            ek.CopyTo(yeni, onceki.Length);
            var kontrolYolu = yol + GuvenlikGunlugu.KontrolDosyasiEki;
            var kontrol = JsonNode.Parse(File.ReadAllText(kontrolYolu))!.AsObject();
            kontrol["BekleyenBayt"] = yeni.LongLength;
            kontrol["BekleyenSha256"] = Convert.ToHexString(SHA256.HashData(yeni));
            File.WriteAllText(kontrolYolu, kontrol.ToJsonString());
            if (satirYazildi)
                File.AppendAllText(yol, Encoding.UTF8.GetString(ek));

            using var ikinciTemel = new KasaWebFactory();
            using var ikinciHost = Ac(ikinciTemel, yol);
            using var ikinciIstemci = ikinciHost.CreateClient();
            var gunluk = ikinciHost.Services.GetRequiredService<GuvenlikGunlugu>();
            Assert.Equal(satirYazildi ? yeni : onceki, File.ReadAllBytes(yol));
            Assert.Equal(satirYazildi ? 2 : 1, gunluk.Oku()!.Olaylar.Count);
            var duzeltilmis = JsonNode.Parse(File.ReadAllText(kontrolYolu))!.AsObject();
            Assert.Null(duzeltilmis["BekleyenBayt"]);
            Assert.Equal(satirYazildi ? yeni.LongLength : onceki.LongLength, duzeltilmis["Bayt"]!.GetValue<long>());
        }
        finally { Temizle(yol); }
    }
}
