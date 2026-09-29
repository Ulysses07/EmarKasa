using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Kasa.App.Core.Tests;

/// <summary>
/// XAML bağlama yolları (tests-9): Kasa.App XAML'inde x:DataType yok, bağlamalar çalışma anında yansımayla çözülür ve
/// yanlış özellik adı derlemeyi geçip ekranda sessizce boş kalır. Burada her {Binding} yolu, bağlamın gerçek türüne karşı
/// Kasa.App.Core (ve MAUI) türleri üzerinde yansımayla çözülür:
/// <list type="bullet">
/// <item>Sayfa kökü: kod-arkasında <c>BindingContext = ... vm</c> ile atanan kurucu parametresinin ViewModel türü.</item>
/// <item>ItemsSource / BindableLayout.ItemsSource yolunun öğe türü, o öğenin DataTemplate'ine ve ItemDisplayBinding'e geçer.</item>
/// <item><c>Source={x:Reference Sayfa}</c> ile <c>BindingContext.X</c>: sayfanın ViewModel'i.</item>
/// <item>Kendi bağlamı olmayan görünüm (RaporDurumu) kullanıldığı her yerde oradaki bağlamla; Styles.xaml'deki
/// DataTrigger bağlamaları stili kullanan her öğenin bağlamıyla; kabuk şablonları MAUI'nin öğe türleriyle denetlenir.</item>
/// </list>
/// Her XAML dosyasındaki her bağlama en az bir kez değerlendirilmelidir: çözücünün sessizce atladığı bağlama kalmaz.
/// </summary>
public partial class MauiKayitTutarliligiTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private const string GorunumAdAlani = "clr-namespace:Kasa.App.Views";

    /// <summary>Kabuk şablonlarının bağlamı MAUI'nin kendi öğesidir (menü öğesi ve menü komutu).</summary>
    private static readonly Dictionary<string, Type> KabukSablonlari = new()
    {
        ["Shell.ItemTemplate"] = typeof(BaseShellItem),
        ["Shell.MenuItemTemplate"] = typeof(MenuItem),
    };

    [Fact]
    public void Xaml_baglama_yollari_baglamin_gercek_turunde_var()
    {
        var denetim = new XamlBaglamaDenetimi(Uygulama);
        denetim.HepsiniDenetle();
        Assert.True(denetim.Hatalar.Count == 0, "Çözülemeyen XAML bağlamaları:\n" + string.Join("\n", denetim.Hatalar));
        var atlanan = denetim.TumBaglamalar.Except(denetim.Degerlendirilen).Order().ToList();
        Assert.True(atlanan.Count == 0, "Hiç değerlendirilmeyen bağlamalar (sayfa/görünüm/stil kapsamı dışında):\n" + string.Join("\n", atlanan));
        Assert.True(denetim.TumBaglamalar.Count > 240, $"XAML bağlamaları okunamadı ({denetim.TumBaglamalar.Count}).");
    }

    /// <summary>Çözücünün kendisi: yanlış ad, şablon içinde yanlış öğe alanı, sayfa referansıyla yanlış komut ve yanlış iç
    /// içe yol bildirilir; doğruları bildirilmez.</summary>
    [Fact]
    public void Yanlis_baglama_yolu_bildirilir()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui" xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml" x:Name="Sayfa">
              <VerticalStackLayout IsVisible="{Binding VeriVar}">
                <Label Text="{Binding VeriVarr}" />
                <CollectionView ItemsSource="{Binding Donemler}">
                  <CollectionView.ItemTemplate><DataTemplate><VerticalStackLayout>
                    <Label Text="{Binding KasaSonucu, StringFormat='{0:N2} ₺'}" />
                    <Label Text="{Binding Donem.Start, StringFormat='{0:dd MMM}'}" />
                    <Label Text="{Binding Donem.Baslangic}" />
                    <Label Text="{Binding KasaSonucux}" />
                    <Button Command="{Binding BindingContext.YenileCommand, Source={x:Reference Sayfa}}" CommandParameter="{Binding .}" />
                    <Button Command="{Binding BindingContext.YenileKomutu, Source={x:Reference Sayfa}}" />
                  </VerticalStackLayout></DataTemplate></CollectionView.ItemTemplate>
                </CollectionView>
              </VerticalStackLayout>
            </ContentPage>
            """;
        var denetim = new XamlBaglamaDenetimi(Uygulama);
        denetim.Denetle(XDocument.Parse(xaml, LoadOptions.SetLineInfo), "Ornek.xaml", typeof(HaftalikViewModel));
        Assert.Equal(4, denetim.Hatalar.Count);
        Assert.Contains(denetim.Hatalar, h => h.Contains("VeriVarr") && h.Contains(nameof(HaftalikViewModel)));
        Assert.Contains(denetim.Hatalar, h => h.Contains("Donem.Baslangic") && h.Contains("DonemDto"));
        Assert.Contains(denetim.Hatalar, h => h.Contains("KasaSonucux") && h.Contains(nameof(HaftalikSatir)));
        Assert.Contains(denetim.Hatalar, h => h.Contains("YenileKomutu"));
    }

    /// <summary>Haftalık raporda "Dağılım bekleyen" etiketi her dönem satırında koşulsuz "0,00 ₺" gösteriyordu: yalnız tutar
    /// sıfırdan farklıyken görünür ve metni satır modelinden (HaftalikSatir, Bicim.Tl) gelir; görünüm sayı biçimi yazmaz.</summary>
    [Fact]
    public void Haftalik_dagilim_bekleyen_etiketi_satir_modeline_baglidir()
    {
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";
        var etiket = Assert.Single(XDocument.Parse(Oku("Views/HaftalikPage.xaml")).Descendants(maui + "Label"),
            l => (string?)l.Attribute("Text") is { } metin && metin.Contains("DagilimBekleyen"));
        Assert.Equal("{Binding DagilimBekleyenMetni}", (string?)etiket.Attribute("Text"));
        Assert.Equal("{Binding DagilimBekliyor}", (string?)etiket.Attribute("IsVisible"));
    }

    /// <summary>Kod ile kurulan sayfalardaki genel liste şablonu (TakipUi.Liste&lt;T&gt;) öğe türünde "Baslik" ve "Ozet"
    /// yollarına bağlanır; dizge yol derlemede denetlenmez.</summary>
    [Fact]
    public void Kodla_kurulan_liste_sablonlarinin_oge_turleri_baslik_ve_ozet_tasir()
    {
        Assert.Contains("Bagli(\"Baslik\")", Oku(Path.Combine("Views", "TakipUi.cs")));
        Assert.Contains("Bagli(\"Ozet\"", Oku(Path.Combine("Views", "TakipUi.cs")));
        var derleme = typeof(AuthViewModel).Assembly;
        var kullanimlar = Directory.GetFiles(Uygulama, "*.cs", SearchOption.AllDirectories)
            .SelectMany(d => ListeSablonu().Matches(File.ReadAllText(d)).Select(m => (Dosya: Path.GetFileName(d), Tur: m.Groups[1].Value))).ToList();
        Assert.True(kullanimlar.Count >= 5, $"TakipUi.Liste<T> kullanımları okunamadı ({kullanimlar.Count}).");
        var eksik = kullanimlar.Where(k => derleme.GetTypes().SingleOrDefault(t => t.Name == k.Tur) is not { } tur
                || tur.GetProperty("Baslik") is null || tur.GetProperty("Ozet") is null)
            .Select(k => $"{k.Dosya}: Liste<{k.Tur}>").Distinct().ToList();
        Assert.True(eksik.Count == 0, "Baslik/Ozet özelliği olmayan liste öğe türleri: " + string.Join(", ", eksik));
    }

    [GeneratedRegex(@"(?<!View )\bListe<(\w+)>\(")]
    private static partial Regex ListeSablonu();

    /// <summary>XAML'i gezip her bağlama yolunu bağlamın türünde çözer.</summary>
    private sealed partial class XamlBaglamaDenetimi
    {
        private readonly string uygulama;
        private readonly Assembly _cekirdek = typeof(AuthViewModel).Assembly;
        private readonly Dictionary<string, List<(string Yol, string Anahtar)>> _stilBaglamalari;
        public List<string> Hatalar { get; }

        public XamlBaglamaDenetimi(string uygulama)
        {
            this.uygulama = uygulama;
            _stilBaglamalari = StilBaglamalari(uygulama, out var stilHatalari);
            Hatalar = stilHatalari;
        }
        /// <summary>Kaynaktaki bütün bağlama öznitelikleri ("dosya:satır öznitelik=değer").</summary>
        public HashSet<string> TumBaglamalar { get; } = [];
        public HashSet<string> Degerlendirilen { get; } = [];

        public void HepsiniDenetle()
        {
            foreach (var dosya in Directory.GetFiles(uygulama, "*.xaml", SearchOption.AllDirectories))
                foreach (var a in Yukle(dosya).Descendants().Attributes().Where(a => Baglama(a.Value)))
                    TumBaglamalar.Add(Anahtar(Path.GetFileName(dosya), a));
            foreach (var dosya in Directory.GetFiles(Path.Combine(uygulama, "Views"), "*.xaml"))
            {
                var ad = Path.GetFileNameWithoutExtension(dosya);
                if (SayfaTuru(ad) is not { } kok)
                {
                    if (!File.ReadAllText(dosya + ".cs").Contains("BindingContext")) continue; // bağlamını kullanıldığı yerden alan görünüm
                    Hatalar.Add($"{ad}: kod-arkasındaki BindingContext ataması çözülemedi.");
                    continue;
                }
                Denetle(Yukle(dosya), Path.GetFileName(dosya), kok);
            }
            Denetle(Yukle(Path.Combine(uygulama, "AppShell.xaml")), "AppShell.xaml", null);
        }

        public void Denetle(XDocument belge, string dosya, Type? kok)
        {
            var adlar = new Dictionary<string, Type?>();
            if (belge.Root!.Attribute(Xaml + "Name")?.Value is { } kokAdi) adlar[kokAdi] = kok;
            Yuru(belge.Root, dosya, kok, null, adlar);
        }

        private void Yuru(XElement el, string dosya, Type? baglam, Type? sablon, Dictionary<string, Type?> adlar)
        {
            var ad = el.Name.LocalName;
            if (ad == "DataTemplate") baglam = sablon;
            if (ad.Contains('.'))
            {
                // Özellik öğesi (CollectionView.ItemTemplate, Label.FormattedText ...): bağlam değişmez; kabuk şablonları hariç.
                if (KabukSablonlari.TryGetValue(ad, out var kabuk)) sablon = kabuk;
                foreach (var c in el.Elements()) Yuru(c, dosya, baglam, sablon, adlar);
                return;
            }
            if (el.Attribute("BindingContext") is { } bc && Baglama(bc.Value)) baglam = Degerlendir(bc, dosya, baglam, adlar);
            Type? oge = null;
            foreach (var kaynak in el.Attributes().Where(a => a.Name.LocalName is "ItemsSource" or "BindableLayout.ItemsSource" && Baglama(a.Value)))
                oge = Eleman(dosya, kaynak, Degerlendir(kaynak, dosya, baglam, adlar));
            foreach (var a in el.Attributes().Where(a => Baglama(a.Value) && a.Name.LocalName is not ("BindingContext" or "ItemsSource" or "BindableLayout.ItemsSource")))
                Degerlendir(a, dosya, a.Name.LocalName == "ItemDisplayBinding" ? oge : baglam, adlar, oge is null && a.Name.LocalName == "ItemDisplayBinding" ? "ItemsSource öğe türü bilinmiyor" : null);
            if (el.Attribute(Xaml + "Name")?.Value is { } isim) adlar.TryAdd(isim, baglam);
            if (el.Attribute("Style")?.Value is { } stil && StaticResource().Match(stil) is { Success: true } s && _stilBaglamalari.TryGetValue(s.Groups[1].Value, out var stilBaglamalari))
                foreach (var (yol, anahtar) in stilBaglamalari)
                {
                    Degerlendirilen.Add(anahtar);
                    Coz(yol, baglam, $"{dosya}:{Satir(el)} Style={s.Groups[1].Value} → {anahtar}");
                }
            if (el.Name.NamespaceName == GorunumAdAlani && File.Exists(Path.Combine(uygulama, "Views", ad + ".xaml")) && SayfaTuru(ad) is null)
                foreach (var c in Yukle(Path.Combine(uygulama, "Views", ad + ".xaml")).Root!.Elements())
                    Yuru(c, ad + ".xaml", baglam, null, new Dictionary<string, Type?>());
            foreach (var c in el.Elements()) Yuru(c, dosya, baglam, oge ?? sablon, adlar);
        }

        /// <summary>Bağlamayı çözer, sonucun türünü döndürür (çözülemezse null ve hata).</summary>
        private Type? Degerlendir(XAttribute a, string dosya, Type? baglam, Dictionary<string, Type?> adlar, string? baglamYokNedeni = null)
        {
            Degerlendirilen.Add(Anahtar(dosya, a));
            var yer = $"{dosya}:{Satir(a)} {a.Name.LocalName}=\"{a.Value}\"";
            var (yol, kaynak, desteksiz) = Ayristir(a.Value);
            if (desteksiz is not null) { Hatalar.Add($"{yer}: desteklenmeyen bağlama ({desteksiz})."); return null; }
            if (kaynak is not null)
            {
                if (!adlar.TryGetValue(kaynak, out var hedef)) { Hatalar.Add($"{yer}: x:Reference '{kaynak}' bulunamadı."); return null; }
                if (!yol.StartsWith("BindingContext.")) { Hatalar.Add($"{yer}: x:Reference ile yalnız BindingContext.X yolu denetlenebilir."); return null; }
                return Coz(yol["BindingContext.".Length..], hedef, yer);
            }
            if (baglam is null) { Hatalar.Add($"{yer}: bağlam türü bilinmiyor ({baglamYokNedeni ?? "sayfa kökü ya da DataTemplate öğe türü çözülemedi"})."); return null; }
            return Coz(yol, baglam, yer);
        }

        private Type? Coz(string yol, Type? tur, string yer)
        {
            if (tur is null) { Hatalar.Add($"{yer}: bağlam türü bilinmiyor."); return null; }
            if (yol == ".") return tur;
            foreach (var parca in yol.Split('.'))
            {
                if (parca.Contains('[')) { Hatalar.Add($"{yer}: dizinli yol desteklenmiyor ({yol})."); return null; }
                var t = Nullable.GetUnderlyingType(tur!) ?? tur!;
                var ozellik = Ozellik(t, parca);
                if (ozellik is null) { Hatalar.Add($"{yer}: {t.Name} türünde '{parca}' özelliği yok ({yol})."); return null; }
                tur = ozellik.PropertyType;
            }
            return tur;
        }

        private static PropertyInfo? Ozellik(Type t, string ad)
        {
            var turler = t.IsInterface ? t.GetInterfaces().Prepend(t) : [t];
            return turler.SelectMany(x => x.GetProperties(BindingFlags.Public | BindingFlags.Instance)).FirstOrDefault(p => p.Name == ad && p.GetIndexParameters().Length == 0);
        }

        private Type? Eleman(string dosya, XAttribute kaynak, Type? tur)
        {
            if (tur is null) return null;
            var eleman = tur.IsArray ? tur.GetElementType()
                : (tur.IsGenericType && tur.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? tur : tur.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))?.GetGenericArguments()[0];
            if (eleman is null || tur == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(tur))
                Hatalar.Add($"{dosya}:{Satir(kaynak)} {kaynak.Name.LocalName}=\"{kaynak.Value}\": {tur.Name} öğe türü bilinen bir koleksiyon değil.");
            return eleman;
        }

        private Type? SayfaTuru(string sayfa)
        {
            var kod = Path.Combine(uygulama, "Views", sayfa + ".xaml.cs");
            if (!File.Exists(kod)) return null;
            var metin = File.ReadAllText(kod);
            if (SayfaBaglami().Match(metin) is not { Success: true } atama) return null;
            if (new Regex($@"public {sayfa}\(([^)]*)\)").Match(metin) is not { Success: true } kurucu) return null;
            var parametre = kurucu.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(p => p.Length == 2 && p[1] == atama.Groups[1].Value);
            return parametre is null ? null : _cekirdek.GetTypes().SingleOrDefault(t => t.Name == parametre[0]);
        }

        /// <summary>Styles.xaml'de anahtarlı stillerin (BasedOn zinciriyle) bağlamaları; anahtarsız (örtük) stilde bağlama
        /// her hedef öğeye uygulanır ve burada denetlenemez: hata sayılır.</summary>
        private static Dictionary<string, List<(string Yol, string Anahtar)>> StilBaglamalari(string uygulama, out List<string> hatalar)
        {
            hatalar = [];
            var stiller = new Dictionary<string, (string? Temel, List<(string, string)> Baglamalar)>();
            foreach (var dosya in Directory.GetFiles(Path.Combine(uygulama, "Resources", "Styles"), "*.xaml"))
                foreach (var stil in Yukle(dosya).Descendants().Where(e => e.Name.LocalName == "Style"))
                {
                    var baglamalar = stil.Descendants().Attributes().Where(a => Baglama(a.Value))
                        .Select(a => (Ayristir(a.Value).Yol, Anahtar(Path.GetFileName(dosya), a))).ToList();
                    if (stil.Attribute(Xaml + "Key")?.Value is not { } anahtar)
                    {
                        if (baglamalar.Count > 0) hatalar.Add($"{Path.GetFileName(dosya)}:{Satir(stil)}: anahtarsız stilde bağlama denetlenemez.");
                        continue;
                    }
                    var temel = stil.Attribute("BasedOn")?.Value is { } b && StaticResource().Match(b) is { Success: true } m ? m.Groups[1].Value : null;
                    stiller[anahtar] = (temel, baglamalar);
                }
            List<(string, string)> Topla(string anahtar) => stiller.TryGetValue(anahtar, out var s)
                ? [.. s.Baglamalar, .. s.Temel is null ? [] : Topla(s.Temel)] : [];
            return stiller.Keys.Select(k => (k, Topla(k))).Where(x => x.Item2.Count > 0).ToDictionary(x => x.k, x => x.Item2);
        }

        /// <summary>{Binding yol, Source={x:Reference Ad}, Converter=..., StringFormat='...'} → yol ve kaynak adı.</summary>
        private static (string Yol, string? Kaynak, string? Desteksiz) Ayristir(string deger)
        {
            var ic = deger.Trim()["{Binding".Length..^1];
            string yol = "."; string? kaynak = null; string? desteksiz = null;
            var ilk = true;
            foreach (var parca in UstDuzeyBol(ic).Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                var esit = UstDuzeyEsittir(parca);
                if (esit < 0) { if (ilk) yol = parca; ilk = false; continue; }
                ilk = false;
                var anahtar = parca[..esit].Trim(); var d = parca[(esit + 1)..].Trim();
                switch (anahtar)
                {
                    case "Path": yol = d; break;
                    case "Source":
                        if (ReferansKaynagi().Match(d) is { Success: true } r) kaynak = r.Groups[1].Value; else desteksiz = parca;
                        break;
                    case "RelativeSource": desteksiz = parca; break;
                }
            }
            return (yol, kaynak, desteksiz);
        }

        private static IEnumerable<string> UstDuzeyBol(string s)
        {
            int derinlik = 0, bas = 0; var tirnak = false;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\'') tirnak = !tirnak;
                else if (!tirnak && s[i] == '{') derinlik++;
                else if (!tirnak && s[i] == '}') derinlik--;
                else if (!tirnak && derinlik == 0 && s[i] == ',') { yield return s[bas..i]; bas = i + 1; }
            }
            yield return s[bas..];
        }

        private static int UstDuzeyEsittir(string parca)
        {
            for (var i = 0; i < parca.Length; i++)
            {
                if (parca[i] is '{' or '\'') return -1;
                if (parca[i] == '=') return i;
            }
            return -1;
        }

        private static bool Baglama(string deger) => deger.TrimStart().StartsWith("{Binding", StringComparison.Ordinal);
        private static XDocument Yukle(string dosya) => XDocument.Load(dosya, LoadOptions.SetLineInfo);
        private static int Satir(IXmlLineInfo x) => x.LineNumber;
        private static string Anahtar(string dosya, XAttribute a) => $"{dosya}:{Satir(a)} {a.Name.LocalName}=\"{a.Value}\"";

        [GeneratedRegex(@"BindingContext = (?:_\w+ = )?(\w+);")]
        private static partial Regex SayfaBaglami();
        [GeneratedRegex(@"^\{StaticResource (\w+)\}$")]
        private static partial Regex StaticResource();
        [GeneratedRegex(@"^\{x:Reference (\w+)\}$")]
        private static partial Regex ReferansKaynagi();
    }
}
