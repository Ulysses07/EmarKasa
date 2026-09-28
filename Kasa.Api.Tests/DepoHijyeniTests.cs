using System.Text.RegularExpressions;

namespace Kasa.Api.Tests;

// Depo hijyeni (devops-12): Dockerfile'daki her temel imaj etiket + @sha256 özetiyle sabitlenir. Yalnız etiket
// (ör. aspnet:10.0) değişkendir: VPS'te önbellekte kalmış eski imajla derlenir, iki makinede farklı imaj üretir.
// Özet değişmez; güncellemesi bilinçli bir adımdır (docs/deploy/operasyon-runbook.md "Temel imajlar"). Özetin
// kayıtta gerçekten bulunduğunu ve güncel olup olmadığını CI'daki 'base-images' işi ağdan denetler; burada ağ
// olmadan biçim denetlenir. Etiket okunabilirlik içindir (hangi .NET yaması); Docker özet varken etiketi yok sayar.
public class DepoHijyeniTests
{
    // FROM <depo>:<etiket>@sha256:<64 küçük onaltılık> [AS <aşama>]
    private static readonly Regex SabitImaj = new(
        @"^(?i:FROM)\s+(?<depo>[a-z0-9]+(?:[._/-][a-z0-9]+)*):(?<etiket>[A-Za-z0-9][A-Za-z0-9._-]*)@sha256:(?<ozet>[0-9a-f]{64})(?:\s+(?i:AS)\s+(?<asama>[A-Za-z][A-Za-z0-9_.-]*))?$");
    private static readonly Regex AsamaAdi = new(@"\s(?i:AS)\s+(?<asama>[A-Za-z][A-Za-z0-9_.-]*)$");

    [Fact]
    public void Her_temel_imaj_etiket_ve_sha256_ozetiyle_sabitlenir()
    {
        var satirlar = FromSatirlari();
        Assert.NotEmpty(satirlar);

        var asamalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var satir in satirlar)
        {
            var kaynak = satir.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1];
            // Önceki aşamadan türeyen 'FROM build AS x' satırı dış imaj değildir; sabitlenecek bir şey yoktur.
            if (!asamalar.Contains(kaynak))
                Assert.True(SabitImaj.IsMatch(satir),
                    $"Dockerfile: '{satir}' temel imajı etiket + @sha256:<özet> ile sabitlemiyor. Güncel özeti ağdan meta veriyle alın "
                    + "(docker buildx imagetools inspect <imaj>:<etiket>); özet uydurmayın. Adımlar: docs/deploy/operasyon-runbook.md.");
            if (AsamaAdi.Match(satir) is { Success: true } m) asamalar.Add(m.Groups["asama"].Value);
        }
    }

    [Fact]
    public void Ozetler_yer_tutucu_degildir_ve_farkli_imajlar_ayni_ozeti_paylasmaz()
    {
        var imajlar = DisImajlar();
        Assert.NotEmpty(imajlar);
        Assert.All(imajlar, i => Assert.False(i.Ozet.Distinct().Count() == 1, $"'{i.Depo}' özeti yer tutucu görünüyor: {i.Ozet}"));
        foreach (var grup in imajlar.GroupBy(i => i.Ozet).Where(g => g.Select(i => i.Depo).Distinct().Count() > 1))
            Assert.Fail($"Farklı imajlar aynı özeti taşıyor ({string.Join(", ", grup.Select(i => i.Depo))}); özetler kopyalanmış olabilir.");
    }

    [Fact]
    public void Dotnet_imajlarinin_surumu_Kasa_Api_hedef_cercevesiyle_ayni()
    {
        // net10.0 hedefleyen API net11 çalışma zamanında (ya da tersi) açılmaz; imaj etiketi hedef çerçeveyle birlikte güncellenir.
        var cerceve = Regex.Match(File.ReadAllText(DepoDosyasi("Kasa.Api", "Kasa.Api.csproj")), @"<TargetFramework>net(?<surum>\d+\.\d+)</TargetFramework>");
        Assert.True(cerceve.Success, "Kasa.Api.csproj içinde <TargetFramework>netX.Y</TargetFramework> bulunamadı.");
        var dotnet = DisImajlar().Where(i => i.Depo.StartsWith("mcr.microsoft.com/dotnet/", StringComparison.Ordinal)).ToList();

        Assert.Contains(dotnet, i => i.Depo.EndsWith("/sdk", StringComparison.Ordinal));
        Assert.Contains(dotnet, i => i.Depo.EndsWith("/aspnet", StringComparison.Ordinal));
        Assert.All(dotnet, i => Assert.True(i.Etiket == cerceve.Groups["surum"].Value || i.Etiket.StartsWith(cerceve.Groups["surum"].Value + ".", StringComparison.Ordinal),
            $"{i.Depo}:{i.Etiket} Kasa.Api'nin hedef çerçevesiyle (net{cerceve.Groups["surum"].Value}) aynı ana sürümde değil."));
    }

    [Fact]
    public void Zamanlanmis_imaj_denetimi_push_kosusuyla_ayni_eszamanlilik_grubunu_paylasmaz()
    {
        // cancel-in-progress aynı gruptaki süren koşuyu iptal eder. Grup olay türünü ayırmazsa haftalık özet denetimi
        // o daldaki push CI'ını (45 dakikalık Windows derlemesi dahil) keser; o sırada gelen push da haftalık denetimi.
        var satirlar = File.ReadAllLines(DepoDosyasi(".github", "workflows", "ci.yml"));
        if (!satirlar.Any(s => s.Trim() == "schedule:")) return;

        var grup = satirlar.SkipWhile(s => s.TrimEnd() != "concurrency:").Skip(1).TakeWhile(s => s.StartsWith(' '))
            .Select(s => s.Trim()).Single(s => s.StartsWith("group:", StringComparison.Ordinal));
        Assert.Matches(@"github\.event_name\s*==\s*'schedule'", grup);
    }

    private sealed record Imaj(string Depo, string Etiket, string Ozet);

    private static List<Imaj> DisImajlar() =>
        FromSatirlari().Select(s => SabitImaj.Match(s)).Where(m => m.Success)
            .Select(m => new Imaj(m.Groups["depo"].Value, m.Groups["etiket"].Value, m.Groups["ozet"].Value)).ToList();

    private static List<string> FromSatirlari() =>
        File.ReadAllLines(DepoDosyasi("Dockerfile"))
            .Select(s => s.Trim())
            .Where(s => s.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string DepoDosyasi(params string[] parcalar)
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx")))
                return Path.Combine([dizin.FullName, .. parcalar]);
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) test çıktısının üst dizinlerinde bulunamadı.");
    }
}
