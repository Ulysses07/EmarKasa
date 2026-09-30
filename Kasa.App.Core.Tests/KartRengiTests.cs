namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusunun rengi kart adındaki bankadan gelir (tasarım 2026-09-30 §2 Renk); büyük/küçük harf ve Türkçe
/// karakter farkı gözetilmez, tanınmayan banka kart kimliğine göre sabit bir palet rengi alır.</summary>
public class KartRengiTests
{
    [Theory]
    [InlineData("Garanti Bonus", KartRenkAilesi.Yesil)]
    [InlineData("GARANTİ BBVA", KartRenkAilesi.Yesil)]
    [InlineData("Akbank Axess", KartRenkAilesi.Kirmizi)]
    [InlineData("İş Bankası Maximum", KartRenkAilesi.Mavi)]
    [InlineData("İş", KartRenkAilesi.Mavi)]
    [InlineData("is", KartRenkAilesi.Mavi)]
    [InlineData("ISBANK", KartRenkAilesi.Mavi)]
    [InlineData("QNB Card Finans", KartRenkAilesi.Mor)]
    [InlineData("Finansbank", KartRenkAilesi.Mor)]
    [InlineData("VakıfBank World", KartRenkAilesi.Sari)]
    [InlineData("VAKIFBANK", KartRenkAilesi.Sari)]
    [InlineData("Yapı Kredi World", KartRenkAilesi.Lacivert)]
    [InlineData("YAPI KREDİ", KartRenkAilesi.Lacivert)]
    [InlineData("Ziraat Bankkart", KartRenkAilesi.Kirmizi)]
    [InlineData("Halkbank Paraf", KartRenkAilesi.Mavi)]
    [InlineData("DenizBank", KartRenkAilesi.Mavi)]
    [InlineData("Enpara.com", KartRenkAilesi.Mor)]
    [InlineData("TEB Bonus", KartRenkAilesi.Yesil)]
    // Ayırt edici (uzun) banka anahtarı, kısa tam sözcük "is"ten önce kazanır: banka adının içinde "İş" geçmesi
    // kartı İş Bankası yapmaz.
    [InlineData("Ziraat Bankkart İş", KartRenkAilesi.Kirmizi)]
    [InlineData("Yapı Kredi İş", KartRenkAilesi.Lacivert)]
    [InlineData("QNB İş", KartRenkAilesi.Mor)]
    [InlineData("Vakıf İş", KartRenkAilesi.Sari)]
    [InlineData("YAPIKREDİ", KartRenkAilesi.Lacivert)]
    [InlineData("Is Bankasi", KartRenkAilesi.Mavi)]
    [InlineData("Türkiye Vakıflar Bankası", KartRenkAilesi.Sari)]
    public void Banka_adi_renk_ailesini_belirler(string ad, KartRenkAilesi beklenen)
        => Assert.Equal(beklenen, KartRengi.Sec(ad, 1));

    [Theory]
    [InlineData("Visa kartım")]   // "is" yalnız tam sözcükse İş Bankası'dır; "visa" içinde geçmesi sayılmaz
    [InlineData("Şirket kartı")]
    [InlineData("Paris Bank")]    // sözcük birleştirmesi "parisbank" sahte "isbank" alt dizesini içerir; eşleşmemeli
    [InlineData("")]
    [InlineData(null)]
    public void Taninmayan_banka_kimlige_gore_sabit_palet_rengi_alir(string? ad)
    {
        Assert.Contains(KartRengi.Sec(ad, 7), KartRengi.Palet);
        Assert.Equal(KartRengi.Sec(ad, 7), KartRengi.Sec(ad, 7));
        Assert.Equal(KartRengi.Palet[7 % KartRengi.Palet.Count], KartRengi.Sec(ad, 7));
        Assert.NotEqual(KartRengi.Sec(ad, 1), KartRengi.Sec(ad, 2));
    }

    /// <summary>Negatif kart kimliği de int → uint dönüşümüyle her zaman palet sınırları içinde, kararlı bir renk verir.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Negatif_kimlik_de_palet_icinde_kararli_renk_verir(int kimlik)
    {
        Assert.Contains(KartRengi.Sec(null, kimlik), KartRengi.Palet);
        Assert.Equal(KartRengi.Sec(null, kimlik), KartRengi.Sec(null, kimlik));
    }

    [Fact]
    public void Renk_anahtari_aile_ve_parca_adindan_olusur()
    {
        Assert.Equal("KartYesilZemin", KartRengi.Anahtar(KartRenkAilesi.Yesil, KartRenkParcasi.Zemin));
        Assert.Equal("KartLacivertYazi", KartRengi.Anahtar(KartRenkAilesi.Lacivert, KartRenkParcasi.Yazi));
    }
}
