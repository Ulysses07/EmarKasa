using System.Globalization;
using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

public partial class MauiKayitTutarliligiTests
{
    /// <summary>WCAG 2.2 1.4.3: ikincil yazı renkleri gerçekte üzerinde durdukları zeminde en az 4,5:1 kontrast verir
    /// (yer tutucu metin de Muted'dır). Zemin Colors.xaml anahtarı ya da sayfadaki sabit renk (#RRGGBB) olabilir.</summary>
    [Theory]
    [InlineData("Muted", "Card")]           // satır ikincil metni, giriş yer tutucusu, seçici başlığı
    [InlineData("Muted", "AppBg")]          // aylık sayfadaki yıl
    [InlineData("Sub", "Card")]             // bölüm ve alan etiketleri
    [InlineData("Sub", "AppBg")]            // sayfa alt başlığı
    [InlineData("Sub", "DateChipBg")]       // tarih çipi, kanal sırası
    [InlineData("Sub", "#F8F8F3")]          // alış kalemi düzenleyicisi
    [InlineData("Sub", "#F1F5EE")]          // alış ödemesi kanal payları
    [InlineData("HeroSub", "Green")]
    [InlineData("HeroLabel", "Green")]
    [InlineData("SidebarMuted", "Sidebar")]
    [InlineData("Neg", "Card")]             // geçersiz tutarda yer tutucu (ParaGirisi) hata rengine döner
    [InlineData("Neg", "NegSoft")]          // kart kutusu "Son ödeme geçti" etiketi
    [InlineData("UyariMetin", "UyariZemin")] // kart kutusu "Geçiş farkını doğrulayın" etiketi
    [InlineData("Ink", "ChipBg")]           // kart kutusu "Eski takip" ve "Pasif" etiketleri
    public void Ikincil_yazi_renkleri_zemininde_en_az_4_5_kontrast_verir(string yazi, string zemin)
    {
        var renkler = RenkTanimi().Matches(Oku("Resources/Styles/Colors.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var oran = KontrastOrani(renkler[yazi], zemin.StartsWith('#') ? zemin : renkler[zemin]);
        Assert.True(oran >= 4.5, $"{yazi} / {zemin} kontrastı {oran:0.00}:1; en az 4,5:1 olmalı.");
    }

    /// <summary>Kart kutuları (KartKutusu): her renk ailesinde kutu yazısı zeminine karşı en az 4,5:1 (WCAG 1.4.3), doluluk
    /// çubuğunun dolgusu (Yazi) izine (Kenar) karşı en az 3:1 (WCAG 1.4.11 grafik nesne) verir. Anahtarlar KartRengi.Anahtar'dır.</summary>
    [Fact]
    public void Kart_renk_ailelerinde_yazi_zemininde_4_5_cubuk_izinde_3_kontrast_verir()
    {
        var renkler = RenkTanimi().Matches(Oku("Resources/Styles/Colors.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var hatalar = new List<string>();
        foreach (var aile in Enum.GetValues<KartRenkAilesi>())
        {
            var anahtarlar = Enum.GetValues<KartRenkParcasi>().ToDictionary(p => p, p => KartRengi.Anahtar(aile, p));
            var eksik = anahtarlar.Values.Where(a => !renkler.ContainsKey(a)).ToList();
            if (eksik.Count > 0)
            { hatalar.Add($"{aile}: Colors.xaml'da tanımsız {string.Join(", ", eksik)}"); continue; }
            var yazi = KontrastOrani(renkler[anahtarlar[KartRenkParcasi.Yazi]], renkler[anahtarlar[KartRenkParcasi.Zemin]]);
            var cubuk = KontrastOrani(renkler[anahtarlar[KartRenkParcasi.Yazi]], renkler[anahtarlar[KartRenkParcasi.Kenar]]);
            if (yazi < 4.5)
                hatalar.Add($"{aile}: yazı/zemin {yazi:0.00}:1 (en az 4,5)");
            if (cubuk < 3)
                hatalar.Add($"{aile}: çubuk dolgu/iz {cubuk:0.00}:1 (en az 3)");
        }
        Assert.True(hatalar.Count == 0, string.Join("\n", hatalar));
    }

    /// <summary>WCAG 2.2 1.4.11: form alanının (FieldBorder) tek görsel sınırı kenarlığıdır; alanın beyaz içine ve alanın durduğu
    /// zeminlere karşı en az 3:1 kontrast verir. Kart ve ayırıcı kenarlıkları (Border) süs olarak açık kalabilir.</summary>
    [Theory]
    [InlineData("Card")]      // alanın içi (FieldBorder zemini) ve form kartı
    [InlineData("AppBg")]     // sayfa zemini
    [InlineData("#F8F8F3")]   // alış kalemi düzenleyicisi
    public void Form_alani_kenarligi_zemininde_en_az_3_kontrast_verir(string zemin)
    {
        var renkler = RenkTanimi().Matches(Oku("Resources/Styles/Colors.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var oran = KontrastOrani(renkler["FieldStroke"], zemin.StartsWith('#') ? zemin : renkler[zemin]);
        Assert.True(oran >= 3, $"FieldStroke / {zemin} kontrastı {oran:0.00}:1; en az 3:1 olmalı.");
    }

    /// <summary>WCAG 2.2 1.4.11: Kartlar ekranında açık formun düğmesini gösteren tek işaret kenarıdır (birincil düğmede zemin de
    /// koyulaşır); kenar, düğmelerin durduğu kart zeminine (CardForm → BrushCard → Card) karşı en az 3:1 kontrast verir.</summary>
    [Fact]
    public void Acik_form_dugmesi_kenari_kart_zemininde_en_az_3_kontrast_verir()
    {
        var renkler = RenkTanimi().Matches(Oku("Resources/Styles/Colors.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        Assert.Contains("<SolidColorBrush x:Key=\"BrushCard\" Color=\"{StaticResource Card}\" />", Oku("Resources/Styles/Colors.xaml"));
        var oran = KontrastOrani(renkler[Kasa.App.Views.TakipUi.AcikFormKenari], renkler["Card"]);
        Assert.True(oran >= 3, $"{Kasa.App.Views.TakipUi.AcikFormKenari} / Card kontrastı {oran:0.00}:1; en az 3:1 olmalı.");
    }

    /// <summary>FieldBorder kenarlığı süs kenarlığından (BrushBorder) ayrı fırçadan gelir: alan sınırı koyulaşırken kartlar değişmez.</summary>
    [Fact]
    public void Form_alani_stili_ayri_kenarlik_fircasini_kullanir()
    {
        var alan = Regex.Match(Oku("Resources/Styles/Styles.xaml"), @"<Style x:Key=""FieldBorder"" TargetType=""Border"">(.*?)</Style>", RegexOptions.Singleline);
        Assert.True(alan.Success, "FieldBorder stili bulunamadı.");
        Assert.Contains("<Setter Property=\"Stroke\" Value=\"{StaticResource BrushFieldStroke}\" />", alan.Groups[1].Value);
        Assert.Contains("<SolidColorBrush x:Key=\"BrushFieldStroke\" Color=\"{StaticResource FieldStroke}\" />", Oku("Resources/Styles/Colors.xaml"));
    }

    /// <summary>Koyulaşan Muted pasif düğme zeminini değiştirmez: pasif bileşen 1.4.3 kapsamı dışındadır, eski açık görünüm korunur.</summary>
    [Fact]
    public void Pasif_dugme_zemini_ayri_anahtardan_gelir()
    {
        var stiller = Oku("Resources/Styles/Styles.xaml");
        Assert.DoesNotContain("<Setter Property=\"BackgroundColor\" Value=\"{StaticResource Muted}\" />", stiller);
        Assert.Contains("<Setter Property=\"BackgroundColor\" Value=\"{StaticResource DisabledBg}\" />", stiller);
        Assert.Contains("<Color x:Key=\"DisabledBg\">#9AA08F</Color>", Oku("Resources/Styles/Colors.xaml"));
    }

    /// <summary>WCAG 2.2 göreli parlaklık ve kontrast oranı: (L1 + 0,05) / (L2 + 0,05), sRGB kanalında 0,04045 eşiği.</summary>
    private static double KontrastOrani(string a, string b)
    {
        var (la, lb) = (Parlaklik(a), Parlaklik(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Parlaklik(string renk)
    {
        var hex = renk.Equals("White", StringComparison.OrdinalIgnoreCase) ? "FFFFFF" : renk.TrimStart('#');
        Assert.True(hex.Length == 6, $"Kontrast yalnız opak #RRGGBB renkte hesaplanır: {renk}");
        double Kanal(int i)
        {
            var c = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Kanal(0) + 0.7152 * Kanal(2) + 0.0722 * Kanal(4);
    }

    [GeneratedRegex(@"<Color x:Key=""(\w+)"">([^<]+)</Color>")]
    private static partial Regex RenkTanimi();
}
