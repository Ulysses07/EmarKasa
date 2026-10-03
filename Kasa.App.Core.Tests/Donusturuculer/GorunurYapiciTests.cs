using Kasa.App.Controls;

namespace Kasa.App.Core.Tests;

/// <summary>Hataya kaydırmanın hedefi: hataların konulduğu sırayla ilk hatası olan görünür form alanı.</summary>
public class GorunurYapiciTests
{
    [Fact]
    public void Ilk_hatali_alan_hata_sirasiyla_ve_yalniz_gorunur_alanlardan_secilir()
    {
        GorunumOrtami.Kur();
        var ad = new FormAlani { Alan = "Ad", Icerik = new Entry() };
        var tutar = new FormAlani { Alan = "Tutar", Icerik = new Entry() };
        var gizli = new FormAlani { Alan = "Taksit", Icerik = new Entry() };
        var gizliKap = new VerticalStackLayout { IsVisible = false, Children = { gizli } };
        var form = new VerticalStackLayout { Children = { ad, gizliKap, tutar } };
        var hatalar = new AlanHatalari();

        Assert.Null(GorunurYapici.IlkHataliAlan(form, hatalar));

        hatalar.Ayarla("Taksit", "Taksit sayısı 1 ile 60 arasında olmalı.");
        hatalar.Ayarla("Tutar", "Tutar sıfırdan büyük olmalı.");
        hatalar.Ayarla("Ad", "Ad boş olamaz.");
        Assert.Same(tutar, GorunurYapici.IlkHataliAlan(form, hatalar));

        hatalar.Temizle("Tutar");
        Assert.Same(ad, GorunurYapici.IlkHataliAlan(form, hatalar));
    }
}
