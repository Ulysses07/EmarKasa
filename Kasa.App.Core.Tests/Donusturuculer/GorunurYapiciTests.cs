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

    /// <summary>Ö-2: odaklanamayan alanın (ör. çip grubu; test ortamında gerçek platform işleyicisi olmadığından her alan
    /// odaklanamaz) hatası ekran okuyucuya duyurulur.</summary>
    [Fact]
    public void Odaklanamayan_alanin_hatasi_ekran_okuyucuya_duyurulur()
    {
        GorunumOrtami.Kur();
        var alan = new FormAlani { Alan = "Kanal", Icerik = new HorizontalStackLayout(), Hata = "Kanal seçin." };
        var duyurular = new List<string>();

        GorunurYapici.OdaklanVeyaDuyur(alan, duyurular.Add);

        Assert.Equal(["Kanal seçin."], duyurular);
    }

    /// <summary>Ö-2: form hiçbir alan hatası taşımıyor ama genel hatası varsa (ör. bağlantı hatası) bu da ekran okuyucuya
    /// duyurulur; duyuru kaydırmanın (Yap) sonucunu beklemeden hemen yapılır.</summary>
    [Fact]
    public void Genel_hata_varken_hatayagit_duyuruyu_hemen_yapar()
    {
        GorunumOrtami.Kur();
        var yapici = new GorunurYapici(new ScrollView { Content = new VerticalStackLayout() });
        var form = new VerticalStackLayout();
        var genelKutu = new Border();
        var hatalar = new AlanHatalari { Genel = "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin." };
        var duyurular = new List<string>();

        yapici.HatayaGit(form, hatalar, genelKutu, duyurular.Add);

        Assert.Equal([hatalar.Genel], duyurular);
    }
}
