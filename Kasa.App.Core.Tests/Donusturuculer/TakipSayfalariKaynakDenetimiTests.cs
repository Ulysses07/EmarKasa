namespace Kasa.App.Core.Tests;

/// <summary>Görev 14-19 incelemesinden kalan küçük düzeltmeler: MAUI'ye bağlı (Windows) sayfa kodu kaynak düzeyinde denetlenir
/// (<see cref="GorunurYapiciTests"/> ve <see cref="MauiKayitTutarliligiTests"/> ile aynı yaklaşım — bu dosyalar test projesinde
/// derlenir, ama "Forma dön" sonrası gerçek bir ScrollView kaydırmasını beklemek testi platform işleyicisi olmadan asılı
/// bırakabilir).</summary>
public class TakipSayfalariKaynakDenetimiTests
{
    /// <summary>Krediler'de listeden bir satıra tıklayınca "Forma dön" (onay penceresinde) seçilirse Secili değişmez; bu durumda
    /// özet kartına kaydırma yapılmamalı. Kaydırma yalnız seçim gerçekten bu satıra geçtiyse (vm.Secili?.Id == s.Veri.Id)
    /// yapılmalı.</summary>
    [Fact]
    public void Krediler_satir_seciminde_kaydirma_yalniz_secim_gercekten_degisirse_yapilir()
    {
        var kod = GorunumOrtami.Oku("Views/KrediTakipPage.cs");
        Assert.Matches(
            @"async s =>\s*\{\s*await vm\.SecCommand\.ExecuteAsync\(s\);\s*//[^\n]*\n\s*if \(vm\.Secili\?\.Id == s\.Veri\.Id\)\s*\n\s*await Kaydirici\.ScrollToAsync\(ozet, ScrollToPosition\.Start, true\);\s*\}",
            kod);
    }

    /// <summary>Aylık giderlerde ödeme onayı onay kutusunun alan adı boş olmamalı (ekran okuyucu hata varken alanın adını
    /// okuyabilsin); "Ödeme onayı" olmalı.</summary>
    [Fact]
    public void AylikGiderler_odeme_onayi_alan_adi_bos_degil()
    {
        var kod = GorunumOrtami.Oku("Views/AylikGiderPage.cs");
        Assert.DoesNotContain("Alan(\"\", Onay(", kod);
        Assert.Contains("Alan(\"Ödeme onayı\", Onay(\"Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.\", nameof(vm.OdemeOnay)), o, nameof(vm.OdemeOnay))", kod);
    }
}
