namespace Kasa.App.Core.Tests;

/// <summary>Form alan hataları: alan → ileti, genel hata, sıra, temizleme, sunucu eşlemesi ve değişiklik bildirimi.</summary>
public class AlanHatalariTests
{
    private static List<string> Bildirimleri(AlanHatalari h)
    {
        var adlar = new List<string>();
        h.PropertyChanged += (_, e) => adlar.Add(e.PropertyName!);
        return adlar;
    }

    [Fact]
    public void Ayarla_dizinleyiciyi_ve_Var_i_bildirir_sirayi_korur()
    {
        var h = new AlanHatalari();
        var adlar = Bildirimleri(h);

        h.Ayarla("DuzenTutar", "Tutar sıfırdan büyük olmalı.");
        h.Ayarla("DuzenCari", "Açıklama boş olamaz.");
        h.Ayarla("DuzenTutar", "Tutar sıfır olamaz.");

        Assert.Equal("Tutar sıfır olamaz.", h["DuzenTutar"]);
        Assert.Equal("Açıklama boş olamaz.", h["DuzenCari"]);
        Assert.Null(h["DuzenNot"]);
        Assert.True(h.Var);
        Assert.Equal("DuzenTutar", h.IlkAlan);
        Assert.Equal(["DuzenTutar", "DuzenCari"], h.Alanlar);
        Assert.Equal(["Item[DuzenTutar]", "Var", "Item[DuzenCari]", "Item[DuzenTutar]"], adlar);
    }

    [Fact]
    public void Alan_temizlenince_yalniz_o_alan_kalkar_tum_temizlik_genel_hatayi_da_kaldirir()
    {
        var h = new AlanHatalari();
        h.Ayarla("A", "a");
        h.Ayarla("B", "b");
        h.Genel = "Sunucuya ulaşılamadı.";
        var adlar = Bildirimleri(h);

        h.Temizle("A");
        h.Temizle("Yok");
        h.Temizle((string?)null);
        Assert.Null(h["A"]);
        Assert.Equal("b", h["B"]);
        Assert.Equal("B", h.IlkAlan);
        Assert.Equal(["Item[A]"], adlar);

        adlar.Clear();
        h.Temizle();
        Assert.False(h.Var);
        Assert.Null(h.Genel);
        Assert.Null(h["B"]);
        Assert.Null(h.IlkAlan);
        Assert.Equal(["Item[B]", "Item", "Genel", "Var"], adlar);

        adlar.Clear();
        h.Temizle();
        Assert.Empty(adlar);
    }

    [Fact]
    public void Genel_bosluk_ise_null_olur()
    {
        var h = new AlanHatalari { Genel = "  " };
        Assert.Null(h.Genel);
        Assert.False(h.Var);
        h.Genel = "Hata";
        Assert.True(h.Var);
    }

    [Fact]
    public void Sunucu_hatalari_eslenir_eslenmeyenler_bir_kez_doner()
    {
        var h = new AlanHatalari();
        var eslem = new Dictionary<string, string> { ["cari"] = "DuzenCari", ["tutartl"] = "DuzenTutar" };
        var sunucu = new Dictionary<string, string>
        {
            ["cari"] = "Bu alan boş olamaz.",
            ["istekid"] = "Geçerli bir istek kimliği gerekir.",
            ["kalemler"] = "Geçerli bir istek kimliği gerekir.",
            ["tutartl"] = "Tutar en fazla iki ondalık basamak içerebilir.",
        };

        var kalan = h.SunucuHatalariniYaz(sunucu, eslem);

        Assert.Equal("Bu alan boş olamaz.", h["DuzenCari"]);
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", h["DuzenTutar"]);
        Assert.Equal(["Geçerli bir istek kimliği gerekir."], kalan);
    }

    [Fact]
    public void Denetle_gecersizde_yazar_ilk_kural_kazanir()
    {
        var h = new AlanHatalari();
        Assert.True(h.Denetle(true, "A", "a"));
        Assert.False(h.Var);
        Assert.False(h.Denetle(false, "A", "ilk"));
        Assert.False(h.Denetle(false, "A", "ikinci"));
        Assert.Equal("ilk", h["A"]);
    }

    [Fact]
    public void Goster_istegi_yalniz_hata_varken_gelir()
    {
        var h = new AlanHatalari();
        var istek = 0;
        h.GosterIstendi += (_, _) => istek++;
        h.GosterIste();
        Assert.Equal(0, istek);
        h.Ayarla("A", "a");
        h.GosterIste();
        Assert.Equal(1, istek);
    }
}
