using System.Reflection;
using System.Xml.Linq;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Katman sınırları (Aşama 2a): Kasa.Core başka Kasa projesine, Kasa.App.Core MAUI'ye (Microsoft.Maui.*), Kasa.ApiClient
/// sunucuya (Kasa.Api), sunucu da istemci projelerine (Kasa.ApiClient, Kasa.App.Core, Kasa.App) bağımlı değildir.
/// İki düzeyde denetlenir; ek kütüphane gerekmez:
/// - Proje dosyası: ProjectReference zinciri (geçişli) ve PackageReference / UseMaui / platforma özgü hedef çerçeve.
///   Derleyici kullanılmayan başvuruyu IL'e yazmadığı için bildirilen bağımlılık burada yakalanır.
/// - Derleme: <see cref="Assembly.GetReferencedAssemblies"/> kapanışı (üçüncü taraf paketlerin başvuruları dahil); kodda
///   gerçekten kullanılan bağımlılık, başka bir paket üzerinden gelse de burada yakalanır.
/// Bu proje sunucu ve istemci katmanlarının dördüne de başvuran tek test projesidir (Kasa.App.Core başvurusu yalnız bu
/// testler içindir).
/// </summary>
public class MimariTests
{
    private static readonly Assembly Cekirdek = typeof(Kasa.Core.GiderTipi).Assembly;
    private static readonly Assembly Istemci = typeof(Kasa.ApiClient.KasaApiClient).Assembly;
    private static readonly Assembly UygulamaCekirdegi = typeof(Kasa.App.Core.AuthViewModel).Assembly;
    private static readonly Assembly Sunucu = typeof(Kasa.Api.PanelDto).Assembly;
    private static readonly string[] IstemciProjeleri = ["Kasa.ApiClient", "Kasa.App.Core", "Kasa.App"];

    [Fact]
    public void Denetim_bilinen_bagimliliklari_gorur()
    {
        // Denetimler boş küme yüzünden kendiliğinden geçmesin: bilinen bağımlılıklar her iki düzeyde de görülür.
        Assert.Equal(new[] { "Kasa.ApiClient", "Kasa.Core" }, ProjeKapanisi("Kasa.App.Core"));
        Assert.Equal(new[] { "Kasa.Core" }, ProjeKapanisi("Kasa.Api"));
        Assert.Contains("Kasa.App.Core", ProjeKapanisi("Kasa.App"));
        Assert.Contains("Kasa.ApiClient", DerlemeKapanisi(UygulamaCekirdegi));
        Assert.Contains("Kasa.Core", DerlemeKapanisi(UygulamaCekirdegi));
        Assert.Contains("CommunityToolkit.Mvvm", DerlemeKapanisi(UygulamaCekirdegi));
        Assert.Contains("Kasa.Core", DerlemeKapanisi(Sunucu));
        Assert.Contains("Microsoft.Maui.Controls", PaketBasvurulari("Kasa.App"));
    }

    [Fact]
    public void Kasa_Core_baska_Kasa_projesine_bagimli_degil()
    {
        Assert.Empty(ProjeKapanisi("Kasa.Core"));
        Assert.DoesNotContain(DerlemeKapanisi(Cekirdek), KasaDerlemesi);
    }

    [Fact]
    public void Kasa_App_Core_MAUIye_bagimli_degil()
    {
        foreach (var proje in ProjeKapanisi("Kasa.App.Core").Prepend("Kasa.App.Core"))
        {
            var belge = Proje(proje);
            Assert.DoesNotContain(PaketBasvurulari(proje), p => p.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase));
            Assert.False(Ozellik(belge, "UseMaui") is { } maui && maui.Equals("true", StringComparison.OrdinalIgnoreCase), $"{proje}: UseMaui açık.");
            // Platforma özgü hedef (net10.0-android, -windows...) MAUI iş yükünü ve platform API'lerini açar; çekirdek net10.0 kalır.
            var cerceveler = $"{Ozellik(belge, "TargetFramework")};{Ozellik(belge, "TargetFrameworks")}";
            Assert.False(cerceveler.Contains('-'), $"{proje}: platforma özgü hedef çerçeve ({cerceveler}).");
        }
        Assert.DoesNotContain(DerlemeKapanisi(UygulamaCekirdegi), d => d.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Kasa_ApiClient_sunucuya_bagimli_degil()
    {
        Assert.DoesNotContain("Kasa.Api", ProjeKapanisi("Kasa.ApiClient"));
        Assert.DoesNotContain("Kasa.Api", DerlemeKapanisi(Istemci));
    }

    [Fact]
    public void Kasa_Api_istemci_projelerine_bagimli_degil()
    {
        Assert.Empty(ProjeKapanisi("Kasa.Api").Intersect(IstemciProjeleri));
        Assert.Empty(DerlemeKapanisi(Sunucu).Intersect(IstemciProjeleri));
    }

    // ---- alan kodları (Kasa.Core.Kodlar, Aşama 4) ----

    private const string KodlarAdAlani = "Kasa.Core.Kodlar";

    /// <summary>Kasa.Core.Kodlar yalnız sabit dize taşıyan statik sınıflardır ve adları istemci katmanlarının türleriyle
    /// çakışmaz: Kasa.App.Core "using Kasa.Core.Kodlar;" ile "using Kasa.ApiClient;"ı birlikte kullanır. Kasa.Core'un kök ad
    /// alanını almak GiderTipi (Kasa.Core ve Kasa.ApiClient'ta ayrı ayrı var) yüzünden CS0104 verir; App.Core onu yalnız
    /// tür takma adıyla alır.</summary>
    [Fact]
    public void Kodlar_yalniz_sabit_dize_tasir_ve_istemci_turleriyle_cakismaz()
    {
        var kodlar = Cekirdek.GetTypes().Where(t => t.Namespace == KodlarAdAlani).ToList();
        Assert.Contains(kodlar, t => t.Name == "KanalEtiketleri");
        foreach (var tur in kodlar)
        {
            Assert.True(tur is { IsAbstract: true, IsSealed: true, IsNested: false }, $"{tur.Name} iç içe olmayan statik sınıf olmalı.");
            var uyeler = tur.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.All(uyeler, u => Assert.True(u is FieldInfo { IsLiteral: true, IsPublic: true } f && f.FieldType == typeof(string), $"{tur.Name}.{u.Name} genel sabit dize değil."));
        }
        var istemciTurleri = Istemci.GetTypes().Concat(UygulamaCekirdegi.GetTypes()).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(kodlar, t => istemciTurleri.Contains(t.Name));
        Assert.Contains(Istemci.GetTypes(), t => t.FullName == "Kasa.ApiClient.GiderTipi");
        var kokUsing = KaynakDosyalari("Kasa.App.Core").Where(d => File.ReadLines(d).Any(s => s.Trim() == "using Kasa.Core;")).ToList();
        Assert.True(kokUsing.Count == 0, "Kasa.App.Core'da 'using Kasa.Core;' (GiderTipi CS0104): " + string.Join(", ", kokUsing.Select(Path.GetFileName)));
    }

    /// <summary>Başka bir sözlükte ya da görünen metinde de aynen geçen kod değerleri; tarama bunlarda yanlış alarm verir, bu
    /// değerlerin sabite bağlanması incelemeyle yapılır. "Gider", "KartHarcama", "KartOdeme": benzer kayıt aramasının türü ve
    /// kaynağı (BenzerAramasi.Tur, BenzerKayitDto.Kaynak), istek kaydının işlem adı; "Gelir", "Gider", "KartOdemesi": kasa dökümü
    /// satır türü; "Kart", "Banka", "Gelir", "Gider": görünen metin (seçenek adı, ad yedeği, alan etiketi); "Iptal": varlık alan
    /// adı (kilit kuralı, denetim alanları).</summary>
    private static readonly HashSet<string> CokAnlamliDegerler = new(StringComparer.Ordinal)
    {
        "Gelir", "Gider", "KartHarcama", "KartOdeme", "KartOdemesi", "Kart", "Banka", "Iptal",
    };

    /// <summary>Kod değerleri kaynakta elle yazılmaz: Kasa.Core, Kasa.Api ve istemci projelerinde Kodlar'daki bir sabitin
    /// değeri dize olarak geçmez (yorum satırları, bir kodu görünen ada çeviren switch kolunun sağ tarafı ve
    /// <see cref="CokAnlamliDegerler"/> hariç).</summary>
    [Fact]
    public void Kod_degerleri_kaynakta_elle_yazilmaz()
    {
        var degerler = Cekirdek.GetTypes().Where(t => t.Namespace == KodlarAdAlani)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (Ad: $"{t.Name}.{f.Name}", Deger: (string)f.GetRawConstantValue()!)))
            .ToList();
        Assert.All(CokAnlamliDegerler, d => Assert.Contains(degerler, k => k.Deger == d));
        degerler.RemoveAll(k => CokAnlamliDegerler.Contains(k.Deger));
        Assert.True(degerler.Count >= 6, $"Kodlar sabitleri okunamadı ({degerler.Count}).");
        var bulunan = new List<string>();
        foreach (var dosya in new[] { "Kasa.Core", "Kasa.Api", "Kasa.ApiClient", "Kasa.App.Core", "Kasa.App" }.SelectMany(KaynakDosyalari))
        {
            if (Path.GetFileName(dosya) == "Kodlar.cs" && dosya.Contains($"{Path.DirectorySeparatorChar}Kasa.Core{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            var no = 0;
            foreach (var satir in File.ReadLines(dosya))
            {
                no++;
                if (satir.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                foreach (var (ad, deger) in degerler)
                    if (satir.Replace($"=> \"{deger}\"", "", StringComparison.Ordinal).Contains($"\"{deger}\"", StringComparison.Ordinal))
                        bulunan.Add($"{Path.GetRelativePath(DepoKoku(), dosya)}:{no}: \"{deger}\" yerine {ad}");
            }
        }
        Assert.True(bulunan.Count == 0, "Kod değerleri Kasa.Core.Kodlar sabitleriyle yazılmalı:\n" + string.Join("\n", bulunan));
    }

    /// <summary>Projenin bin/ ve obj/ dışındaki .cs dosyaları.</summary>
    private static IEnumerable<string> KaynakDosyalari(string proje) =>
        Directory.EnumerateFiles(Path.Combine(DepoKoku(), proje), "*.cs", SearchOption.AllDirectories)
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // ---- proje dosyası düzeyi ----

    private static string DepoKoku()
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx")))
                return dizin.FullName;
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) test çıktısının üst dizinlerinde bulunamadı.");
    }

    private static string ProjeYolu(string proje) => Path.Combine(DepoKoku(), proje, proje + ".csproj");

    private static XDocument Proje(string proje)
    {
        var yol = ProjeYolu(proje);
        Assert.True(File.Exists(yol), $"Proje dosyası yok: {yol}");
        return XDocument.Load(yol);
    }

    private static string? Ozellik(XDocument belge, string ad) =>
        belge.Descendants().Where(e => e.Name.LocalName == ad).Select(e => e.Value.Trim()).LastOrDefault();

    private static List<string> PaketBasvurulari(string proje) =>
        Proje(proje).Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").ToList();

    /// <summary>ProjectReference zincirinin (geçişli) proje adları, sıralı.</summary>
    private static List<string> ProjeKapanisi(string proje)
    {
        var gorulen = new SortedSet<string>(StringComparer.Ordinal);
        var bekleyen = new Stack<string>([ProjeYolu(proje)]);
        while (bekleyen.TryPop(out var yol))
        {
            foreach (var basvuru in XDocument.Load(yol).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var hedef = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(yol)!, ((string?)basvuru.Attribute("Include") ?? "").Replace('\\', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(hedef), $"{yol}: başvurulan proje yok: {hedef}");
                if (gorulen.Add(Path.GetFileNameWithoutExtension(hedef)))
                    bekleyen.Push(hedef);
            }
        }
        return [.. gorulen];
    }

    // ---- derleme düzeyi ----

    private static bool KasaDerlemesi(string ad) => ad.StartsWith("Kasa.", StringComparison.Ordinal);

    /// <summary>Çerçeve derlemesi (System.*, Microsoft.Extensions/AspNetCore/EntityFrameworkCore...) başvuruları izlenmez;
    /// adı yine listeye girer. Kasa ve üçüncü taraf derlemeleri yüklenip başvuruları eklenir.</summary>
    private static bool Izlenir(string ad) =>
        !(ad is "netstandard" or "mscorlib" || ad.StartsWith("System", StringComparison.Ordinal)
          || (ad.StartsWith("Microsoft.", StringComparison.Ordinal) && !ad.StartsWith("Microsoft.Maui", StringComparison.Ordinal)));

    /// <summary>Başvurulan derlemelerin geçişli kapanışı (adlar).</summary>
    private static HashSet<string> DerlemeKapanisi(Assembly kok)
    {
        var adlar = new HashSet<string>(StringComparer.Ordinal);
        var bekleyen = new Stack<Assembly>([kok]);
        while (bekleyen.TryPop(out var derleme))
        {
            foreach (var basvuru in derleme.GetReferencedAssemblies())
            {
                if (basvuru.Name is not { } ad || !adlar.Add(ad) || !Izlenir(ad))
                    continue;
                bekleyen.Push(Assembly.Load(basvuru));
            }
        }
        return adlar;
    }
}
