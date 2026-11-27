namespace Kasa.App.Core.Tests;

public class RolTests
{
    [Fact]
    public void Rolu_coz_editor()
        => Assert.Equal(Rol.Editor, SekmeModeli.RolCoz("editor"));

    [Fact]
    public void Rolu_coz_izleyici_varsayilan()
    {
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz("viewer"));
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz(null));
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz("saçma"));
    }

    [Fact]
    public void Izleyici_ayarlari_gormez()
    {
        var bolumler = SekmeModeli.Bolumler(Rol.Izleyici);
        Assert.DoesNotContain(Bolum.Ayarlar, bolumler);
        Assert.Contains(Bolum.Panel, bolumler);
        Assert.Equal(new[] { Bolum.Panel, Bolum.KasaKontrolu, Bolum.Haftalik, Bolum.Aylik, Bolum.Islemler, Bolum.AylikGiderler, Bolum.Kartlar, Bolum.Krediler, Bolum.Cekler, Bolum.Cariler, Bolum.DisariAktar }, bolumler);
    }

    [Fact]
    public void Editor_ayarlari_gorur()
    {
        Assert.Equal(new[] { Bolum.Panel, Bolum.KasaKontrolu, Bolum.Haftalik, Bolum.Aylik, Bolum.Islemler, Bolum.AylikGiderler, Bolum.Kartlar, Bolum.Krediler, Bolum.Cekler, Bolum.Cariler, Bolum.DisariAktar, Bolum.Alislar, Bolum.Bildirimler, Bolum.EkstreAktar, Bolum.Ayarlar }, SekmeModeli.Bolumler(Rol.Editor));
    }

    [Fact]
    public void Cekleri_izleyici_ve_editor_gorur_alici_gormez()
    {
        Assert.Contains(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Contains(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Editor));
        Assert.DoesNotContain(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Alici));
    }

    /// <summary>Kasa kontrolü Kasalar'daki bölümün yerine geçer: Kasalar'ı gören iki rol de görür (geçmiş, alt limit uyarıları ve döküm
    /// sunucuda okuma yetkisiyle gelir; karşılaştırma ve açıklama sayfada yalnız editörde görünür). Alıcı görmez.</summary>
    [Fact]
    public void Kasa_kontrolunu_izleyici_ve_editor_gorur_alici_gormez()
    {
        Assert.Contains(Bolum.KasaKontrolu, SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Contains(Bolum.KasaKontrolu, SekmeModeli.Bolumler(Rol.Editor));
        Assert.DoesNotContain(Bolum.KasaKontrolu, SekmeModeli.Bolumler(Rol.Alici));
    }

    [Fact]
    public void Bolum_sirasi_panelle_baslar()
        => Assert.Equal(Bolum.Panel, SekmeModeli.Bolumler(Rol.Izleyici)[0]);
}
