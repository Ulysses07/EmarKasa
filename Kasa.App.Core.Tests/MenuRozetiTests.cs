namespace Kasa.App.Core.Tests;

/// <summary>Menü rozeti (tasarım 2026-09-30 masaüstü bildirimleri §2): Bildirimler öğesinin yanında okunmamış sayısı; 0 iken görünmez,
/// 99'dan büyükse "99+"; ekran okuyucu öğeyi "Bildirimler, 3 okunmamış" diye okur. Menü yeniden kurulunca korunur, girişe dönüşte silinir.</summary>
public class MenuRozetiTests
{
    private static MenuOgesi Bildirimler(MenuModeli menu) => menu.Ogeler.Single(o => o.Bolum == Bolum.Bildirimler);

    [Fact]
    public void Rozet_sifirken_gorunmez_sayi_ve_erisim_adi_tasir()
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        var oge = Bildirimler(menu);
        Assert.False(oge.RozetVar);
        Assert.Equal("Bildirimler", oge.ErisimAdi);
        menu.RozetAyarla(Bolum.Bildirimler, 3);
        Assert.True(oge.RozetVar);
        Assert.Equal("3", oge.RozetMetni);
        Assert.Equal("Bildirimler, 3 okunmamış", oge.ErisimAdi);
        menu.RozetAyarla(Bolum.Bildirimler, 120);
        Assert.Equal("99+", oge.RozetMetni);
        menu.RozetAyarla(Bolum.Bildirimler, -1);
        Assert.False(oge.RozetVar);
        Assert.All(menu.Ogeler.Where(o => o.Bolum != Bolum.Bildirimler), o => Assert.False(o.RozetVar));
    }

    [Fact]
    public void Rozet_degisince_bagli_ozellikler_bildirilir()
    {
        var oge = new MenuOgesi(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler", _ => { });
        var degisen = new List<string?>();
        oge.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        oge.Rozet = 2;
        Assert.Equal(["ErisimAdi", "Rozet", "RozetMetni", "RozetVar"], degisen.Order());
    }

    [Fact]
    public void Rozet_menu_yeniden_kurulunca_korunur_giriste_silinir()
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        menu.RozetAyarla(Bolum.Bildirimler, 4);
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        Assert.Equal(4, Bildirimler(menu).Rozet);
        menu.Goster([]);
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        Assert.Equal(0, Bildirimler(menu).Rozet);
        // İzleyici menüsünde Bildirimler yok: rozet ayarı hata vermez, öğe eklenmez.
        menu.Goster(SekmeModeli.Bolumler(Rol.Izleyici));
        menu.RozetAyarla(Bolum.Bildirimler, 2);
        Assert.DoesNotContain(menu.Ogeler, o => o.Bolum == Bolum.Bildirimler);
    }
}
