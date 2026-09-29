using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

public partial class MauiKayitTutarliligiTests
{
    /// <summary>maui-8: dosya seçimi (içerik türü, 10 MB sınırı, okuma) ve ay kilidi onayı mantığı App.Core'dadır ve orada
    /// sınanır; sayfalar yalnız MAUI API'lerini (FilePicker, DisplayAlertAsync/DisplayPromptAsync) çağırır. Sayfa kod-arkası
    /// kuralı yeniden yazarsa sınanmayan ikinci bir kopya oluşur; bu denetim onu yakalar.</summary>
    [Fact]
    public void Sayfalar_dosya_secimi_ve_kilit_onayi_kuralini_kendisi_uygulamaz()
    {
        foreach (var dosya in new[] { "Views/AlislarPage.xaml.cs", "Views/EkstreAktarmaPage.cs" })
        {
            var kaynak = Oku(dosya);
            Assert.DoesNotContain("1024 * 1024", kaynak);
            Assert.DoesNotContain(".ReadAsync(", kaynak);
            Assert.DoesNotContain("\"application/pdf\"", kaynak);
        }
        Assert.Contains("_vm.BelgeEkleAsync(", Oku("Views/AlislarPage.xaml.cs"));
        Assert.Contains("Vm.PdfSecVeYukleAsync(", Oku("Views/EkstreAktarmaPage.cs"));
        var kilit = Oku("Views/KasaKontrolAlanlari.cs");
        Assert.Contains("vm.OnayMetni(", kilit);
        Assert.Contains("vm.IstekHalaGecerli(", kilit);
        Assert.DoesNotContain("ayının sonuna kadar", kilit);
    }

    /// <summary>Gerekçe penceresi tek yoldan açılır (OturumluViewModel.GerekceyleAsync): oturum pencere açılmadan önce yakalanır,
    /// pencere açıkken oturum değişirse işlem gönderilmez (GerekceTests). Sayfa DisplayPromptAsync'i doğrudan bekleyip ardından
    /// işlemi çağırırsa oturum yakalanmaz; bu denetim onu yakalar.</summary>
    [Fact]
    public void Sayfalar_gerekce_penceresini_yalniz_GerekceyleAsync_ile_acar()
    {
        var toplam = 0;
        foreach (var dosya in Directory.GetFiles(Uygulama, "*.cs", SearchOption.AllDirectories).Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var kaynak = File.ReadAllText(dosya);
            var pencere = Regex.Matches(kaynak, @"DisplayPromptAsync\(").Count;
            var sarili = Regex.Matches(kaynak, @"GerekceyleAsync\(\(\) => (page\.)?DisplayPromptAsync\(").Count;
            Assert.True(pencere == sarili, $"{Path.GetFileName(dosya)}: {pencere - sarili} gerekçe penceresi GerekceyleAsync dışında açılıyor.");
            toplam += pencere;
        }
        Assert.True(toplam >= 3, $"Gerekçe pencereleri okunamadı ({toplam}).");
    }

    /// <summary>Sayfalar sunucunun durum metnini karşılaştırmaz (ör. "Odendi"); modeldeki tipli özelliği okur (AylikGiderSatiri.OdendiMi).</summary>
    [Fact]
    public void Sayfalar_durum_metnini_karsilastirmaz()
    {
        foreach (var dosya in Directory.GetFiles(Path.Combine(Uygulama, "Views"), "*.cs"))
            Assert.False(Regex.IsMatch(File.ReadAllText(dosya), @"Durum\s*[!=]=\s*"""), $"{Path.GetFileName(dosya)} durum metnini karşılaştırıyor.");
    }

    /// <summary>Rol ve oturum tek kaynaktan gelir (OturumluViewModel: EditorMu hesaplanır, oturum değişimini model kendisi alır).
    /// Sayfa kod-arkası rolü ekrana atar ya da AuthViewModel'den ayrıca okursa ikinci bir rol mekanizması oluşur.</summary>
    [Fact]
    public void Sayfalar_rolu_ekrana_atamaz_rolu_modelden_okur()
    {
        var dosyalar = Directory.GetFiles(Path.Combine(Uygulama, "Views"), "*.cs");
        Assert.True(dosyalar.Length > 10, $"Views dosyaları okunamadı ({dosyalar.Length}).");
        foreach (var dosya in dosyalar)
        {
            var kaynak = File.ReadAllText(dosya);
            Assert.False(Regex.IsMatch(kaynak, @"\.EditorMu\s*=[^=]"), $"{Path.GetFileName(dosya)} EditorMu atıyor.");
            Assert.DoesNotContain("AktifRol", kaynak);
            Assert.DoesNotContain("OturumuAyarla", kaynak);
        }
    }
}
