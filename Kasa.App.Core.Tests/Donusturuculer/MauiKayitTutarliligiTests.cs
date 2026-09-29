using System.Reflection;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kasa.App kabuğunun kaynak tutarlılığı (tests-9): Windows derlemesi yeşilken çalışma anında çöken ya da boş kalan
/// ekranlara yol açan kayıt eksikleri. Kasa.App'e başvurmadan (Linux CI) uygulamanın kendi dosyaları okunur:
/// kabuktaki her sayfa DI'da kayıtlı, sayfaların ve kayıtlı ViewModel'lerin bağımlılıkları kayıtlı, her rol bölümü
/// bir menü öğesine bağlı, XAML'deki dönüştürücü anahtarları uygulama kaynaklarında tanımlı.
/// </summary>
public partial class MauiKayitTutarliligiTests
{
    private static readonly string Uygulama = Path.Combine(DepoKoku(), "Kasa.App");

    private static string DepoKoku()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Kasa.slnx")))
                return d.FullName;
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) bulunamadı.");
    }

    private static string Oku(string yol) => File.ReadAllText(Path.Combine(Uygulama, yol));

    /// <summary>MauiProgram'daki servis kayıtları (Views. öneki atılmış tür adları). Tür argümansız fabrika kaydı
    /// (AddSingleton(sp => ... return new X(...))) döndürdüğü türle sayılır.</summary>
    private static HashSet<string> Kayitlar()
    {
        var kaynak = Oku("MauiProgram.cs");
        var kayitlar = GenelKayit().Matches(kaynak).Select(m => m.Groups[1].Value).ToHashSet();
        kayitlar.UnionWith(FabrikaKaydi().Matches(kaynak).Select(m => m.Groups[1].Value));
        Assert.True(kayitlar.Count > 20, $"MauiProgram kayıtları okunamadı ({kayitlar.Count}).");
        return kayitlar;
    }

    /// <summary>Kabuk ve sayfa kurucularının parametre türleri (sınıf adı → türler).</summary>
    private static Dictionary<string, string[]> SayfaKuruculari()
    {
        var dosyalar = Directory.GetFiles(Path.Combine(Uygulama, "Views"), "*.cs").Append(Path.Combine(Uygulama, "AppShell.xaml.cs")).Append(Path.Combine(Uygulama, "App.xaml.cs"));
        var kurucular = new Dictionary<string, string[]>();
        foreach (var dosya in dosyalar)
            foreach (Match m in Kurucu().Matches(File.ReadAllText(dosya)))
                kurucular[m.Groups[1].Value] = Parametreler(m.Groups[2].Value);
        return kurucular;
    }

    private static string[] Parametreler(string liste) => GenelArguman().Replace(liste, "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('?')).ToArray();

    [Fact]
    public void Kabuktaki_her_sayfa_DI_ile_kayitli()
    {
        var sayfalar = KabukSayfasi().Matches(Oku("AppShell.xaml")).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(sayfalar.Count >= 12, $"AppShell.xaml sayfaları okunamadı ({sayfalar.Count}).");
        var kayitlar = Kayitlar();
        var eksik = sayfalar.Where(s => !kayitlar.Contains(s)).ToList();
        Assert.True(eksik.Count == 0, "AppShell.xaml'da olup MauiProgram'da AddTransient ile kayıtlı olmayan sayfalar: " + string.Join(", ", eksik));
        Assert.Contains("AppShell", kayitlar);
        Assert.Contains("App", kayitlar);
    }

    [Fact]
    public void Kayitli_sayfa_ve_kabuk_bagimliliklari_kayitli()
    {
        var kayitlar = Kayitlar();
        var kurucular = SayfaKuruculari();
        var eksik = new List<string>();
        foreach (var tur in kayitlar.Where(t => t.EndsWith("Page") || t is "AppShell" or "App"))
        {
            Assert.True(kurucular.ContainsKey(tur), $"{tur} için public kurucu bulunamadı.");
            eksik.AddRange(kurucular[tur].Where(p => !kayitlar.Contains(p)).Select(p => $"{tur}({p})"));
        }
        Assert.True(eksik.Count == 0, "DI'da kayıtlı olmayan sayfa bağımlılıkları: " + string.Join(", ", eksik));
    }

    /// <summary>DI, çözebildiği en geniş kurucuyu seçer; isteğe bağlı parametre kayıtlı değilse sessizce varsayılana düşer.
    /// Kasa türleri (API arayüzleri, ViewModel'ler) isteğe bağlı olsa da kayıtlı olmalı: aksi halde özellik uygulamada
    /// görünmeden kapanır. Yalnız çerçeve türleri (ör. TimeProvider) varsayılana bırakılabilir.</summary>
    [Fact]
    public void Kayitli_viewmodel_bagimliliklari_kayitli()
    {
        var kayitlar = Kayitlar();
        var derleme = typeof(AuthViewModel).Assembly;
        var eksik = new List<string>();
        foreach (var ad in kayitlar.Where(k => k.EndsWith("ViewModel")))
        {
            var tur = derleme.GetTypes().SingleOrDefault(t => t.Name == ad);
            Assert.True(tur is not null && typeof(ObservableObject).IsAssignableFrom(tur) && !tur.IsAbstract, $"{ad} Kasa.App.Core'da somut ViewModel değil.");
            var kurucu = Assert.Single(tur!.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            eksik.AddRange(kurucu.GetParameters()
                .Where(p => !kayitlar.Contains(p.ParameterType.Name) && (!p.HasDefaultValue || (p.ParameterType.Namespace ?? "").StartsWith("Kasa.")))
                .Select(p => $"{ad}({p.ParameterType.Name} {p.Name})"));
        }
        Assert.True(eksik.Count == 0, "DI'da kayıtlı olmayan ViewModel bağımlılıkları: " + string.Join(", ", eksik));
    }

    [Fact]
    public void Her_rol_bolumu_kabukta_menu_ogesine_bagli_ve_giriste_gizlenir()
    {
        var bolumler = Enum.GetNames<Bolum>().Order().ToList();
        var ogeler = MenuOgesi().Matches(Oku("AppShell.xaml")).Select(m => m.Groups[1].Value).Order().ToList();
        Assert.True(bolumler.SequenceEqual(ogeler), $"Bolum değerleri ile AppShell.xaml menü öğeleri ('<Bolum>Item') aynı olmalı. Bolum: {string.Join(",", bolumler)}; menü: {string.Join(",", ogeler)}");
        var kod = Oku("AppShell.xaml.cs");
        var acilan = MenuBaglama().Matches(kod).Select(m => (Oge: m.Groups[1].Value, Bolum: m.Groups[2].Value)).ToList();
        Assert.All(acilan, b => Assert.Equal(b.Oge, b.Bolum));
        Assert.Equal(bolumler, acilan.Select(b => b.Bolum).Order().ToList());
        Assert.Equal(bolumler, MenuGizleme().Matches(kod).Select(m => m.Groups[1].Value).Order().ToList());
    }

    [Fact]
    public void Xaml_donusturucu_anahtarlari_uygulama_kaynaklarinda_tanimli()
    {
        var kaynaklar = DonusturucuKaynagi().Matches(Oku("App.xaml")).ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
        var derlenen = typeof(Kasa.App.Converters.TersIseConverter).Assembly.GetTypes().Where(t => t.Namespace == "Kasa.App.Converters").Select(t => t.Name).ToHashSet();
        Assert.All(kaynaklar.Values, tur => Assert.Contains(tur, derlenen));
        var kullanilan = Directory.GetFiles(Uygulama, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(d => DonusturucuKullanimi().Matches(File.ReadAllText(d)).Select(m => (Dosya: Path.GetFileName(d), Anahtar: m.Groups[1].Value))).ToList();
        Assert.NotEmpty(kullanilan);
        var tanimsiz = kullanilan.Where(k => !kaynaklar.ContainsKey(k.Anahtar)).Select(k => $"{k.Dosya}: {k.Anahtar}").ToList();
        Assert.True(tanimsiz.Count == 0, "App.xaml'da tanımlı olmayan dönüştürücü anahtarları: " + string.Join(", ", tanimsiz));
    }

    [GeneratedRegex(@"\.Add(?:Singleton|Transient|Scoped)<(?:Views\.)?(\w+)(?:\s*,\s*[\w.]+)?>\(")]
    private static partial Regex GenelKayit();
    [GeneratedRegex(@"\.Add(?:Singleton|Transient|Scoped)\(sp\s*=>[\s\S]*?return new (\w+)\(")]
    private static partial Regex FabrikaKaydi();
    [GeneratedRegex(@"public (\w+)\(([^)]*)\)")]
    private static partial Regex Kurucu();
    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex GenelArguman();
    [GeneratedRegex(@"\{DataTemplate v:(\w+)\}")]
    private static partial Regex KabukSayfasi();
    [GeneratedRegex(@"<FlyoutItem x:Name=""(\w+)Item""")]
    private static partial Regex MenuOgesi();
    [GeneratedRegex(@"(\w+)Item\.IsVisible = bolumler\.Contains\(Bolum\.(\w+)\);")]
    private static partial Regex MenuBaglama();
    [GeneratedRegex(@"(\w+)Item\.IsVisible = false;")]
    private static partial Regex MenuGizleme();
    [GeneratedRegex(@"<conv:(\w+) x:Key=""(\w+)""")]
    private static partial Regex DonusturucuKaynagi();
    [GeneratedRegex(@"Converter=\{StaticResource (\w+)\}")]
    private static partial Regex DonusturucuKullanimi();
}
