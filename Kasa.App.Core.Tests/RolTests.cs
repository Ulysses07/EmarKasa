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
        Assert.Equal(new[] { Bolum.Panel, Bolum.Haftalik, Bolum.Aylik, Bolum.Islemler, Bolum.AylikGiderler, Bolum.Kartlar, Bolum.Krediler, Bolum.DisariAktar }, bolumler);
    }

    [Fact]
    public void Editor_ayarlari_gorur()
    {
        Assert.Equal(new[] { Bolum.Panel, Bolum.Haftalik, Bolum.Aylik, Bolum.Islemler, Bolum.AylikGiderler, Bolum.Kartlar, Bolum.Krediler, Bolum.DisariAktar, Bolum.Alislar, Bolum.Bildirimler, Bolum.EkstreAktar, Bolum.Ayarlar }, SekmeModeli.Bolumler(Rol.Editor));
    }

    [Fact]
    public void Bolum_sirasi_panelle_baslar()
        => Assert.Equal(Bolum.Panel, SekmeModeli.Bolumler(Rol.Izleyici)[0]);
}
