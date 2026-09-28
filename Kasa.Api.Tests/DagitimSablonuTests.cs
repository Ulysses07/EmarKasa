using System.Text.RegularExpressions;

namespace Kasa.Api.Tests;

// Depodaki Compose şablonları canlıyı 2.0 öncesinden korunmuş ./kasa-data veritabanına
// bağlamamalı: /data ve /yedekler kaynağı yalnız zorunlu ortam değişkeninden gelir,
// değişken tanımsız veya boşsa `docker compose` hata verip durur. Bağlamalar uzun sözdizimiyle
// (type: bind, bind.create_host_path: false) yazılır: yazım hatalı bir yol Docker'a boş dizin
// açtırmaz, 'up' hata verir. YAML bilerek düz metin olarak okunur (ek paket yok); kısa bağlama
// sözdizimine dönülürse test açıkça başarısız olur.
public class DagitimSablonuTests
{
    private static readonly Regex ZorunluVeri = new(@"^\$\{KASA_DATA_DIR:\?[^}]+\}$");
    private static readonly Regex ZorunluYedek = new(@"^\$\{KASA_BACKUP_DIR:\?[^}]+\}$");
    private static readonly Regex ZorunluVekil = new(@"^\$\{KASA_GUVENILIR_VEKILLER:\?[^}]+\}$");
    // Uzun sözdiziminde "anahtar: değer" satırı ya da alt eşleme açan "anahtar:" satırı.
    private static readonly Regex AnahtarDeger = new(@"^(?<anahtar>[a-z_]+):(?:\s+(?<deger>.+))?$");

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
            $"{dosya}: '{b}' göreli host yolu bağlıyor; deploy/ altındaki eski veri dizinine gider."));
    }

    [Theory]
    [MemberData(nameof(ComposeDosyalari))]
    public void Baglamalar_uzun_sozdizimiyle_host_yolu_olusturmaz(string dosya)
    {
        // Kısa sözdizimi (ve create_host_path: true) eksik kaynak yolunu Docker'a boş dizin olarak açtırır;
        // uygulama da o boş dizinde yeni, boş bir veritabanı kurar. false ile 'up' hata verip durur.
        Assert.All(Baglamalar(dosya), b =>
        {
            Assert.Equal("bind", b.Alan("type"));
            Assert.Equal("false", b.Alan("bind.create_host_path"));
            Assert.NotEqual("", b.Kaynak);
            Assert.StartsWith("/", b.Hedef);
        });
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
    public void Veri_yedek_ve_vekil_degiskenine_varsayilan_deger_verilmez(string dosya)
    {
        // ${KASA_DATA_DIR:-./kasa-data} gibi bir varsayılan, değişken unutulduğunda eski veriyi sessizce bağlar;
        // vekil listesinde varsayılan, ortak ağdaki bütün konteynerlere X-Forwarded-For güveni verir.
        var metin = string.Join('\n', YorumsuzSatirlar(DeployDosyasi(dosya)));
        foreach (Match m in Regex.Matches(metin, @"\$\{?KASA_(?:DATA_DIR|BACKUP_DIR|GUVENILIR_VEKILLER)(?<devam>.{0,2})"))
            Assert.StartsWith(":?", m.Groups["devam"].Value);
    }

    [Fact]
    public void Env_ornegi_veri_ve_yedek_dizinini_bos_birakir()
    {
        // Örnekten kopyalanan .env doldurulmadan kalırsa 'up' durmalı; hazır bir yol
        // Docker'ın o yolda boş dizin açmasına ve uygulamanın boş veritabanı kurmasına yol açar.
        var atamalar = EnvOrnegi();

        Assert.True(atamalar.TryGetValue("KASA_DATA_DIR", out var veri), ".env.example KASA_DATA_DIR satırını içermeli.");
        Assert.True(atamalar.TryGetValue("KASA_BACKUP_DIR", out var yedek), ".env.example KASA_BACKUP_DIR satırını içermeli.");
        Assert.Equal("", veri);
        Assert.Equal("", yedek);
    }

    [Fact]
    public void Caddy_sablonu_guvenilir_vekilleri_zorunlu_degiskenden_alir()
    {
        // docker-compose.yml harici orderdeck_web ağına katılır; uygulamanın varsayılan listesi (172.16.0.0/12 vb.)
        // o ağdaki bütün konteynerlerin X-Forwarded-For başlığına güvenir. Liste yalnız ters vekil konteynerinin
        // adresine/alt ağına daraltılmalı: değer .env'den zorunlu gelir, örnekte boş kalır ve Compose durur.
        var vekil = Assert.Single(YorumsuzSatirlar(DeployDosyasi("docker-compose.yml")).Select(s => s.Trim()),
            s => s.StartsWith("Kasa__GuvenilirVekiller:", StringComparison.Ordinal));
        Assert.Matches(ZorunluVekil, vekil["Kasa__GuvenilirVekiller:".Length..].Trim().Trim('"', '\''));

        Assert.True(EnvOrnegi().TryGetValue("KASA_GUVENILIR_VEKILLER", out var ornek), ".env.example KASA_GUVENILIR_VEKILLER satırını içermeli.");
        Assert.Equal("", ornek);
    }

    // devops-12: 'up -d --build' yereldeki önbellekten gelen temel imajla derler; temel imaj ve paket yamaları
    // 'build --pull' ile gelir. Güncel dağıtım belgeleri ve şablon yorumları derlemeyi yalnız '--pull' ile anlatır.
    // Tarihsel belgeler (docs/plans, docs/deploy/kasa-db-recreate.md) kapsam dışıdır.
    public static TheoryData<string> GuncelDagitimBelgeleri => new()
    {
        "deploy/README.md", "deploy/docker-compose.nginx.yml", "deploy/docker-compose.yml", "docs/deploy/operasyon-runbook.md",
    };

    [Theory]
    [MemberData(nameof(GuncelDagitimBelgeleri))]
    public void Guncel_dagitim_belgeleri_imaji_pull_ile_derler(string dosya)
    {
        var komutlar = File.ReadAllLines(DepoDosyasi(dosya)).Where(s => s.Contains("docker compose", StringComparison.Ordinal)).ToList();

        Assert.All(komutlar, s => Assert.False(Regex.IsMatch(s, @"\sup\s[^#`]*--build\b"),
            $"{dosya}: '{s.Trim()}' önbellekteki temel imajla derler; önce 'build --pull kasa', ardından 'up -d' kullanın."));
        Assert.All(komutlar.Where(s => Regex.IsMatch(s, @"\sbuild(\s|$)")), s => Assert.Contains("--pull", s));
    }

    [Fact]
    public void Guncelleme_akisi_temel_imaji_pull_ile_derleyip_ayri_adimda_baslatir()
    {
        var readme = File.ReadAllText(DeployDosyasi("README.md"));

        Assert.Contains("docker compose -f docker-compose.nginx.yml build --pull kasa", readme);
        Assert.Contains("docker compose -f docker-compose.nginx.yml up -d", readme);
    }

    [Fact]
    public void Sunucuda_derlemeden_once_temel_imaj_ozetlerinin_tazeligi_denetlenir()
    {
        // Özet sabit olduğundan .NET, OpenSSL ve Debian yamaları yalnız özet güncellenince gelir. Haftalık CI denetimi
        // varsayılan dala bağlıdır; bu yüzden README'nin derleme içeren her bölümünde deploy/temel_imaj.py derlemeden
        // önce çalışır ve eski özette akış durur.
        Assert.True(File.Exists(DeployDosyasi("temel_imaj.py")), "deploy/temel_imaj.py depoda olmalı.");
        var bolumler = Regex.Split(File.ReadAllText(DeployDosyasi("README.md")), @"^## ", RegexOptions.Multiline)
            .Where(b => b.Contains("build --pull kasa", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, bolumler.Count); // İlk kurulum ve Güncelleme
        Assert.All(bolumler, b =>
        {
            var denetim = b.IndexOf("python3 temel_imaj.py", StringComparison.Ordinal);
            Assert.True(denetim >= 0 && denetim < b.IndexOf("build --pull kasa", StringComparison.Ordinal),
                $"README '{b[..b.IndexOf('\n')].Trim()}': 'python3 temel_imaj.py' derlemeden önce çalışmıyor.");
        });
    }

    // devops-9: sunucu dışı yedek zamanlayıcıları kaçan çalışmayı telafi eder (Persistent=true: sunucu o saatte kapalıysa
    // açılışta çalışır) ve depodaki betiği çalıştırır; betik yeniden adlandırılırsa birim sessizce bozulmaz. Ayarlar depo
    // dışındaki, root'a ait dosyadan gelir: /opt/kasa altındaki kaynaklar her yayında yeniden yazılır.
    [Theory]
    [InlineData("kasa-uzak-yedek", "gonder")]
    [InlineData("kasa-uzak-dogrula", "dogrula")]
    public void Uzak_yedek_birimleri_depodaki_betigi_calistirir_ve_kacan_calismayi_telafi_eder(string birim, string komut)
    {
        var servis = YorumsuzSatirlar(DeployDosyasi($"systemd/{birim}.service")).Select(s => s.Trim()).ToList();
        var zamanlayici = YorumsuzSatirlar(DeployDosyasi($"systemd/{birim}.timer")).Select(s => s.Trim()).ToList();

        var calistir = Assert.Single(servis, s => s.StartsWith("ExecStart=", StringComparison.Ordinal));
        var m = Regex.Match(calistir, @"^ExecStart=/usr/bin/python3 /opt/kasa/deploy/(?<betik>[A-Za-z0-9_]+\.py) (?<komut>\S+)$");
        Assert.True(m.Success, $"{birim}.service: '{calistir}' depodaki deploy/ betiğini python3 ile çalıştırmıyor.");
        Assert.True(File.Exists(DeployDosyasi(m.Groups["betik"].Value)), $"{birim}.service depoda olmayan betiği çalıştırıyor: {m.Groups["betik"].Value}");
        Assert.Equal(komut, m.Groups["komut"].Value);
        Assert.Contains("Type=oneshot", servis);
        Assert.Contains("EnvironmentFile=/etc/kasa/uzak-yedek.env", servis);
        Assert.Contains("Persistent=true", zamanlayici);
        Assert.Single(zamanlayici, s => s.StartsWith("OnCalendar=", StringComparison.Ordinal));
    }

    [Fact]
    public void Uzak_yedek_ornek_ayarlari_depoda_bos_kalir()
    {
        // Hedef adı ve izleme adresi (adres gizli kimlik taşır) yalnız sunucudaki /etc/kasa/uzak-yedek.env'e yazılır;
        // uzak deponun anahtarları ve şifreleme parolaları rclone.conf'tadır. Örnekteki her değer boş kalır.
        var atamalar = Atamalar(DeployDosyasi("uzak-yedek.env.example"));

        Assert.Contains("KASA_UZAK_HEDEF", atamalar.Keys);
        Assert.All(atamalar, a => Assert.True(a.Value == "", $"uzak-yedek.env.example: {a.Key} boş olmalı; değer sunucuda yazılır."));
    }

    private sealed record Baglama(IReadOnlyDictionary<string, string> Alanlar)
    {
        public string Kaynak => Alan("source");
        public string Hedef => Alan("target");
        public string Alan(string yol) => Alanlar.GetValueOrDefault(yol, "");
        public override string ToString() => $"{Kaynak} -> {Hedef}";
    }

    // volumes: bloklarındaki öğeler; iç içe anahtarlar noktayla birleşir (ör. bind.create_host_path).
    private static List<Baglama> Baglamalar(string dosya)
    {
        var sonuc = new List<Baglama>();
        Dictionary<string, string>? oge = null;
        var ustler = new Stack<(int Girinti, string Anahtar)>();
        int? blokGirintisi = null;
        foreach (var satir in YorumsuzSatirlar(DeployDosyasi(dosya)))
        {
            var icerik = satir.TrimStart();
            var girinti = satir.Length - icerik.Length;
            if (blokGirintisi is { } g && (girinti < g || (girinti == g && !icerik.StartsWith("- ", StringComparison.Ordinal))))
                (blokGirintisi, oge) = (null, null);
            if (icerik == "volumes:") { blokGirintisi = girinti; continue; }
            if (blokGirintisi is null) continue;

            if (icerik.StartsWith("- ", StringComparison.Ordinal))
            {
                oge = new Dictionary<string, string>(StringComparer.Ordinal);
                sonuc.Add(new Baglama(oge));
                ustler.Clear();
                icerik = icerik[2..].TrimStart();
                girinti = satir.Length - icerik.Length;
            }
            var m = AnahtarDeger.Match(icerik);
            Assert.True(oge is not null && m.Success,
                $"{dosya}: '{icerik}' uzun bağlama sözdizimi değil; bağlamayı type/source/target ve bind.create_host_path: false ile yazın.");
            while (ustler.TryPeek(out var ust) && ust.Girinti >= girinti) ustler.Pop();
            var anahtar = m.Groups["anahtar"].Value;
            if (m.Groups["deger"].Success)
                oge![string.Join('.', ustler.Reverse().Select(u => u.Anahtar).Append(anahtar))] = m.Groups["deger"].Value.Trim().Trim('"', '\'');
            else
                ustler.Push((girinti, anahtar));
        }
        Assert.NotEmpty(sonuc);
        return sonuc;
    }

    private static Dictionary<string, string> EnvOrnegi() => Atamalar(DeployDosyasi(".env.example"));

    private static Dictionary<string, string> Atamalar(string yol) =>
        YorumsuzSatirlar(yol)
            .Select(s => s.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

    private static List<string> YorumsuzSatirlar(string yol) =>
        File.ReadAllLines(yol)
            .Where(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith('#'))
            .ToList();

    private static string DeployDosyasi(string ad) => DepoDosyasi(Path.Combine("deploy", ad));

    private static string DepoDosyasi(string goreliYol)
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx")))
                return Path.Combine(dizin.FullName, goreliYol);
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) test çıktısının üst dizinlerinde bulunamadı.");
    }
}
