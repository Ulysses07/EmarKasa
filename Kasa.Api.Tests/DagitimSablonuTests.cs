using System.Text.RegularExpressions;

namespace Kasa.Api.Tests;

// Depodaki Compose şablonları canlıyı 2.0 öncesinden korunmuş ./kasa-data veritabanına
// bağlamamalı: /data ve /yedekler kaynağı yalnız zorunlu ortam değişkeninden gelir,
// değişken tanımsız veya boşsa `docker compose` hata verip durur. YAML bilerek düz metin
// olarak okunur; uzun bağlama sözdizimine geçilirse test açıkça başarısız olur.
public class DagitimSablonuTests
{
    private static readonly Regex ZorunluVeri = new(@"^\$\{KASA_DATA_DIR:\?[^}]+\}$");
    private static readonly Regex ZorunluYedek = new(@"^\$\{KASA_BACKUP_DIR:\?[^}]+\}$");
    // Kaynak en sondaki ":/hedef" parçasından ayrılır; zorunlu değişken iletisindeki ':' kaynağa kalır.
    private static readonly Regex KisaBaglama = new(@"^(?<kaynak>.+):(?<hedef>/[^:]*)(?::[a-z,]+)?$");

    public static TheoryData<string> ComposeDosyalari => new() { "docker-compose.nginx.yml", "docker-compose.yml" };

    [Theory]
    [MemberData(nameof(ComposeDosyalari))]
    public void Compose_goreli_kasa_data_ve_kasa_backups_baglamaz(string dosya)
    {
        var satirlar = YorumsuzSatirlar(DeployDosyasi(dosya));

        Assert.DoesNotContain(satirlar, s => s.Contains("kasa-data", StringComparison.Ordinal));
        Assert.DoesNotContain(satirlar, s => s.Contains("kasa-backups", StringComparison.Ordinal));
        Assert.All(Baglamalar(dosya), b => Assert.False(
            b.Kaynak.StartsWith('.') || b.Kaynak.StartsWith('~'),
            $"{dosya}: '{b.Kaynak}:{b.Hedef}' göreli host yolu bağlıyor; deploy/ altındaki eski veri dizinine gider."));
    }

    [Theory]
    [MemberData(nameof(ComposeDosyalari))]
    public void Veri_ve_yedek_baglamasi_zorunlu_degiskenden_gelir(string dosya)
    {
        var baglamalar = Baglamalar(dosya);

        var veri = Assert.Single(baglamalar, b => b.Hedef == "/data");
        Assert.Matches(ZorunluVeri, veri.Kaynak);
        foreach (var yedek in baglamalar.Where(b => b.Hedef == "/yedekler"))
            Assert.Matches(ZorunluYedek, yedek.Kaynak);
    }

    [Fact]
    public void Nginx_sablonu_yedek_dizinini_de_zorunlu_degiskenden_baglar()
    {
        var yedek = Assert.Single(Baglamalar("docker-compose.nginx.yml"), b => b.Hedef == "/yedekler");
        Assert.Matches(ZorunluYedek, yedek.Kaynak);
    }

    [Theory]
    [MemberData(nameof(ComposeDosyalari))]
    public void Veri_ve_yedek_degiskenine_varsayilan_deger_verilmez(string dosya)
    {
        // ${KASA_DATA_DIR:-./kasa-data} gibi bir varsayılan, değişken unutulduğunda eski veriyi sessizce bağlar.
        var metin = string.Join('\n', YorumsuzSatirlar(DeployDosyasi(dosya)));
        foreach (Match m in Regex.Matches(metin, @"\$\{?KASA_(?:DATA|BACKUP)_DIR(?<devam>.{0,2})"))
            Assert.StartsWith(":?", m.Groups["devam"].Value);
    }

    [Fact]
    public void Env_ornegi_veri_ve_yedek_dizinini_bos_birakir()
    {
        // Örnekten kopyalanan .env doldurulmadan kalırsa 'up' durmalı; hazır bir yol
        // Docker'ın o yolda boş dizin açmasına ve uygulamanın boş veritabanı kurmasına yol açar.
        var atamalar = YorumsuzSatirlar(DeployDosyasi(".env.example"))
            .Select(s => s.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        Assert.True(atamalar.TryGetValue("KASA_DATA_DIR", out var veri), ".env.example KASA_DATA_DIR satırını içermeli.");
        Assert.True(atamalar.TryGetValue("KASA_BACKUP_DIR", out var yedek), ".env.example KASA_BACKUP_DIR satırını içermeli.");
        Assert.Equal("", veri);
        Assert.Equal("", yedek);
    }

    private static List<(string Kaynak, string Hedef)> Baglamalar(string dosya)
    {
        var sonuc = new List<(string, string)>();
        int? blokGirintisi = null;
        foreach (var satir in YorumsuzSatirlar(DeployDosyasi(dosya)))
        {
            var icerik = satir.TrimStart();
            var girinti = satir.Length - icerik.Length;
            if (blokGirintisi is { } g && (girinti < g || (girinti == g && !icerik.StartsWith("- ", StringComparison.Ordinal))))
                blokGirintisi = null;
            if (icerik == "volumes:") { blokGirintisi = girinti; continue; }
            if (blokGirintisi is null || !icerik.StartsWith("- ", StringComparison.Ordinal)) continue;

            var deger = icerik[2..].Trim().Trim('"', '\'');
            var m = KisaBaglama.Match(deger);
            Assert.True(m.Success, $"{dosya}: '{deger}' kısa bağlama sözdizimi değil; bu testi yeni sözdizimine göre güncelleyin.");
            sonuc.Add((m.Groups["kaynak"].Value, m.Groups["hedef"].Value));
        }
        Assert.NotEmpty(sonuc);
        return sonuc;
    }

    private static List<string> YorumsuzSatirlar(string yol) =>
        File.ReadAllLines(yol)
            .Where(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith('#'))
            .ToList();

    private static string DeployDosyasi(string ad)
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx")))
                return Path.Combine(dizin.FullName, "deploy", ad);
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) test çıktısının üst dizinlerinde bulunamadı.");
    }
}
