using System.Text.RegularExpressions;

namespace Kasa.Api.Tests;

/// <summary>
/// deploy/nginx site dosyaları uygulamanın ve masaüstü istemcisinin sınırlarıyla uyumlu olmalı: nginx'in varsayılan 60 sn'lik
/// okuma süresi yedek/dışa aktarma/yükleme sürerken 504 döndürür, varsayılan 1 MB gövde sınırı belge/PDF yüklemesini keser.
/// Dosyalar düz metin olarak okunur (nginx kurulu olmak zorunda değil); location seçimi nginx kuralıyla benzetilir:
/// tam eşleşme (=), sonra en uzun önek (^~ ise hemen), sonra sıradaki ilk düzenli ifade, yoksa en uzun önek.
/// </summary>
public class NginxAyarTests
{
    // Kasa.ApiClient.KasaZamanAsimlari.Varsayilanlar: yükleme 2 dk, indirme 5 dk, yedek 15 dk.
    private static readonly TimeSpan IstemciYukleme = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IstemciIndirme = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IstemciYedek = TimeSpan.FromMinutes(15);

    public static TheoryData<string> SiteDosyalari => new() { "kasa.emarglobal.com.conf", "kasa.emarglobal.com.readonly.conf" };

    [Theory]
    [MemberData(nameof(SiteDosyalari))]
    public void Govde_siniri_belge_ve_PDF_sinirinin_ustunde(string dosya)
    {
        var sunucu = HttpsSunucusu(dosya);
        var sinir = Boyut(Assert.Single(sunucu.Yonergeler("client_max_body_size")));
        // Uygulama 10 MB + 64 KB çok parçalı form payına kadar kabul eder ve aşanı kendi iletisiyle reddeder.
        Assert.True(sinir >= BelgeEndpoints.AzamiBoyut + 64 * 1024, $"{dosya}: client_max_body_size {sinir} bayt, belge sınırının altında.");
        Assert.True(sinir <= 2L * BelgeEndpoints.AzamiBoyut, $"{dosya}: client_max_body_size {sinir} bayt, gereğinden büyük.");
    }

    [Theory]
    [MemberData(nameof(SiteDosyalari))]
    public void Uzun_suren_uclarin_vekil_sureleri_istemci_sinirlariyla_uyumlu(string dosya)
    {
        var sunucu = HttpsSunucusu(dosya);
        foreach (var (yol, istemci) in new[]
        {
            ("/api/yedek", IstemciYedek),
            ("/api/disari-aktar", IstemciIndirme),
            ("/api/belgeler/12", IstemciIndirme),
            ("/api/ekstre-aktar/12/dosya", IstemciIndirme),
            ("/api/ekstre-aktar/yukle", IstemciYukleme),
            ("/api/alis/12/belgeler", IstemciYukleme),
        })
        {
            var konum = sunucu.Sec(yol);
            Assert.True(konum.Yonergeler("proxy_pass").Any(), $"{dosya}: {yol} uygulamaya iletilmiyor ({konum.Desen}).");
            foreach (var yonerge in new[] { "proxy_read_timeout", "proxy_send_timeout" })
            {
                var sure = Sure(konum.Yonergeler(yonerge).SingleOrDefault() ?? sunucu.Yonergeler(yonerge).SingleOrDefault() ?? "60s");
                Assert.True(sure >= istemci, $"{dosya}: {yol} için {yonerge} {sure}, istemcinin {istemci} sınırının altında ({konum.Desen}).");
            }
        }
        // Kısa istekler varsayılan sürede kalır: uzun süre yalnız ilgili uçlara verilir.
        Assert.Empty(sunucu.Sec("/api/islemler").Yonergeler("proxy_read_timeout"));
    }

    [Theory]
    [MemberData(nameof(SiteDosyalari))]
    public void Her_vekil_konumu_istemci_IP_basliklarini_devralir(string dosya)
    {
        // Bir location kendi proxy_set_header satırını tanımlarsa sunucu düzeyindekilerin hiçbirini devralmaz; X-Forwarded-For
        // eksik kalırsa hız sınırları bütün istemcileri nginx'in adresinden gelmiş sayar.
        var sunucu = HttpsSunucusu(dosya);
        Assert.Contains("X-Forwarded-For $proxy_add_x_forwarded_for", sunucu.Yonergeler("proxy_set_header"));
        foreach (var konum in sunucu.Konumlar.Where(k => k.Yonergeler("proxy_pass").Any()))
            Assert.Empty(konum.Yonergeler("proxy_set_header"));
    }

    private sealed record Konum(string Tur, string Desen, IReadOnlyList<(string Ad, string Deger)> Satirlar)
    {
        public IEnumerable<string> Yonergeler(string ad) => Satirlar.Where(s => s.Ad == ad).Select(s => s.Deger);
    }

    private sealed record Sunucu(IReadOnlyList<(string Ad, string Deger)> Satirlar, IReadOnlyList<Konum> Konumlar)
    {
        public IEnumerable<string> Yonergeler(string ad) => Satirlar.Where(s => s.Ad == ad).Select(s => s.Deger);

        public Konum Sec(string yol)
        {
            if (Konumlar.FirstOrDefault(k => k.Tur == "=" && k.Desen == yol) is { } tam) return tam;
            var onek = Konumlar.Where(k => k.Tur is "" or "^~" && yol.StartsWith(k.Desen, StringComparison.Ordinal))
                .OrderByDescending(k => k.Desen.Length).FirstOrDefault();
            if (onek?.Tur == "^~") return onek;
            return Konumlar.FirstOrDefault(k => k.Tur is "~" or "~*" && Regex.IsMatch(yol, k.Desen,
                    k.Tur == "~*" ? RegexOptions.IgnoreCase : RegexOptions.None))
                ?? onek ?? throw new Xunit.Sdk.XunitException($"{yol} için location yok.");
        }
    }

    /// <summary>443'ü dinleyen server bloğu: sunucu düzeyi yönergeleri ve (iç içe olmayan) location blokları.</summary>
    private static Sunucu HttpsSunucusu(string dosya)
    {
        var metin = string.Join('\n', File.ReadAllLines(Path.Combine(DepoKoku(), "deploy", "nginx", dosya))
            .Select(s => s.Split('#')[0]));
        foreach (var blok in Bloklar(metin, "server"))
        {
            if (!Regex.IsMatch(blok, @"(^|\n)\s*listen\s+443\b")) continue;
            var konumlar = new List<Konum>();
            var govde = blok;
            foreach (Match m in Regex.Matches(blok, @"location\s+(?<tur>=|\^~|~\*|~)?\s*(?<desen>\S+)\s*\{(?<ic>[^{}]*)\}"))
            {
                konumlar.Add(new(m.Groups["tur"].Value, m.Groups["desen"].Value, Satirlar(m.Groups["ic"].Value)));
                govde = govde.Replace(m.Value, "", StringComparison.Ordinal);
            }
            return new(Satirlar(govde), konumlar);
        }
        throw new Xunit.Sdk.XunitException($"{dosya}: 443 dinleyen server bloğu yok.");
    }

    private static IEnumerable<string> Bloklar(string metin, string ad)
    {
        foreach (Match m in Regex.Matches(metin, $@"(^|\n)\s*{ad}\s*\{{"))
        {
            var bas = m.Index + m.Length; var derinlik = 1; var i = bas;
            for (; i < metin.Length && derinlik > 0; i++) derinlik += metin[i] == '{' ? 1 : metin[i] == '}' ? -1 : 0;
            yield return metin[bas..(i - 1)];
        }
    }

    private static List<(string Ad, string Deger)> Satirlar(string govde)
        => govde.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0 && !s.Contains('{') && !s.Contains('}'))
            .Select(s => Regex.Match(s, @"^(?<ad>\S+)\s*(?<deger>.*)$", RegexOptions.Singleline))
            .Select(m => (m.Groups["ad"].Value, Regex.Replace(m.Groups["deger"].Value, @"\s+", " "))).ToList();

    private static long Boyut(string deger)
    {
        var m = Regex.Match(deger.Trim(), @"^(?<n>\d+)(?<b>[kKmMgG]?)$");
        Assert.True(m.Success, $"Tanınmayan boyut: {deger}");
        var n = long.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups["b"].Value.ToLowerInvariant() switch { "k" => n * 1024, "m" => n * 1024 * 1024, "g" => n * 1024 * 1024 * 1024, _ => n };
    }

    private static TimeSpan Sure(string deger)
    {
        var m = Regex.Match(deger.Trim(), @"^(?<n>\d+)(?<b>ms|s|m|h|d)?$");
        Assert.True(m.Success, $"Tanınmayan süre: {deger}");
        var n = int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups["b"].Value switch
        {
            "ms" => TimeSpan.FromMilliseconds(n), "m" => TimeSpan.FromMinutes(n), "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n), _ => TimeSpan.FromSeconds(n)
        };
    }

    private static string DepoKoku()
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx"))) return dizin.FullName;
        throw new DirectoryNotFoundException("Depo kökü (Kasa.slnx) bulunamadı.");
    }
}
