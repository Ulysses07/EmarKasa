namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü menüsü (tasarım 2026-09-30 §1): gruplar ve öğeler MenuModeli'nden gelir, görünürlük bugünkü rol kuralıyla
/// (SekmeModeli.Bolumler) süzülür, öğesi kalmayan grup gizlenir, Çıkış grupların altında ayrı durur. Seçili öğe Shell'in
/// konumundan (//rota?sorgu) belirlenir.
/// </summary>
public class MenuModeliTests
{
    private static MenuModeli Menu(Rol rol)
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(rol));
        return menu;
    }

    /// <summary>"Grup: öğe, öğe" satırları; başlıksız (Çıkış) grup "—".</summary>
    private static string[] Ozet(MenuModeli menu)
        => menu.Gruplar.Select(g => $"{g.Baslik ?? "—"}: {string.Join(", ", g.Ogeler.Select(o => o.Baslik))}").ToArray();

    [Fact]
    public void Editor_butun_gruplari_ve_ogeleri_sirayla_gorur()
        => Assert.Equal(new[]
        {
            "Özet: Kasalar, Haftalık, Aylık",
            "Kayıtlar: İşlemler, Alışlar, Aylık giderler, Ekstre içe aktar",
            "Kart ve kredi: Kartlar, Krediler",
            "Diğer: Bildirimler, Rapor dışa aktar, Ayarlar",
            "—: Çıkış",
        }, Ozet(Menu(Rol.Editor)));

    [Fact]
    public void Izleyici_bugunku_rol_kuralindaki_ogeleri_gorur()
        => Assert.Equal(new[]
        {
            "Özet: Kasalar, Haftalık, Aylık",
            "Kayıtlar: İşlemler, Aylık giderler",
            "Kart ve kredi: Kartlar, Krediler",
            "Diğer: Rapor dışa aktar",
            "—: Çıkış",
        }, Ozet(Menu(Rol.Izleyici)));

    [Fact]
    public void Alici_yalniz_alislari_ve_cikisi_gorur()
        => Assert.Equal(new[] { "Kayıtlar: Alışlar", "—: Çıkış" }, Ozet(Menu(Rol.Alici)));

    [Fact]
    public void Gorunur_ogesi_kalmayan_grubun_basligi_da_gizlenir()
    {
        var menu = new MenuModeli();
        menu.Goster(new[] { Bolum.Kartlar });
        Assert.Equal(new[] { "Kart ve kredi: Kartlar", "—: Çıkış" }, Ozet(menu));
        Assert.All(menu.Gruplar, g => Assert.NotEmpty(g.Ogeler));
        Assert.True(menu.Gruplar[0].BaslikVar);
        Assert.False(menu.Gruplar[0].Ayri);
        Assert.True(menu.Gruplar[^1].Ayri);
    }

    [Fact]
    public void Giristen_donuste_menu_bosalir()
    {
        var menu = Menu(Rol.Editor);
        menu.Goster(Array.Empty<Bolum>());
        Assert.Empty(menu.Gruplar);
        Assert.Empty(menu.Ogeler);
    }

    [Fact]
    public void Duzen_her_bolumu_bir_kez_ve_ayri_simgeyle_tasir()
    {
        var ogeler = MenuModeli.Duzen.SelectMany(g => g.Ogeler).ToList();
        Assert.Equal(Enum.GetValues<Bolum>().Order(), ogeler.Select(o => o.Bolum).Order());
        Assert.All(ogeler, o => Assert.False(string.IsNullOrWhiteSpace(o.Simge)));
        Assert.Equal(ogeler.Count + 1, ogeler.Select(o => o.Simge).Append(MenuSimgeleri.Cikis).Distinct().Count());
        Assert.Equal("kartlar", MenuModeli.Rota(Bolum.Kartlar));
    }

    [Theory]
    [InlineData("//kartlar", "kartlar")]
    [InlineData("//kartlar?KartId=3", "kartlar")]
    [InlineData("//panel/IMPL_panel", "panel")]
    [InlineData("aylik", "aylik")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Rota_adi_konumun_ilk_parcasidir(string? konum, string? beklenen)
        => Assert.Equal(beklenen, MenuModeli.RotaAdi(konum));

    [Fact]
    public void Secili_oge_rotaya_gore_belirlenir()
    {
        var menu = Menu(Rol.Editor);
        menu.RotaSecildi("//kartlar?KartId=3");
        Assert.Equal(new[] { "Kartlar" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
        menu.RotaSecildi("//panel");
        Assert.Equal(new[] { "Kasalar" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
        menu.RotaSecildi("//login");
        Assert.DoesNotContain(menu.Ogeler, o => o.Secili);
    }

    [Fact]
    public void Menu_kurulmadan_gelen_rota_menu_kurulunca_secili_olur()
    {
        var menu = new MenuModeli();
        menu.RotaSecildi("//aylik");
        menu.Goster(SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Equal(new[] { "Aylık" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
    }

    [Fact]
    public void Ogeye_tiklamak_rotaya_gitmeyi_cikis_oturumu_kapatmayi_ister()
    {
        var menu = Menu(Rol.Editor);
        string? gidilen = null;
        var cikis = 0;
        menu.GitIstendi += (_, rota) => gidilen = rota;
        menu.CikisIstendi += (_, _) => cikis++;
        menu.Ogeler.Single(o => o.Baslik == "Kartlar").SecCommand.Execute(null);
        Assert.Equal("kartlar", gidilen);
        gidilen = null;
        menu.Ogeler.Single(o => o.Baslik == "Çıkış").SecCommand.Execute(null);
        Assert.Null(gidilen);
        Assert.Equal(1, cikis);
    }

    [Fact]
    public void Uzerine_gelme_vurgusu_secili_ogede_gosterilmez()
    {
        var oge = Menu(Rol.Editor).Ogeler.First();
        var bildirilen = new List<string?>();
        oge.PropertyChanged += (_, e) => bildirilen.Add(e.PropertyName);
        oge.UzerineGelCommand.Execute(null);
        Assert.True(oge.UzerindeVurgu);
        oge.Secili = true;
        Assert.False(oge.UzerindeVurgu);
        oge.Secili = false;
        Assert.True(oge.UzerindeVurgu);
        oge.AyrilCommand.Execute(null);
        Assert.False(oge.UzerindeVurgu);
        Assert.Contains(nameof(MenuOgesi.UzerindeVurgu), bildirilen);
    }
}
