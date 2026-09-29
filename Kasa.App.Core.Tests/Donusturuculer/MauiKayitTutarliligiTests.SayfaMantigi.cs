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
