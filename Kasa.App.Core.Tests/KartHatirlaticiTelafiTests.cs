using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Hatırlatıcı telafi ve vade kuralları: sonraki kesim kaçan son ödemeyi gizlemez, ileri tarihli
/// son kontrol geçersizdir, son ödeme her zaman kesimden SONRAKİ ilk son ödeme günüdür.
/// </summary>
public class KartHatirlaticiTelafiTests
{
    private static KrediKartiGorunum Kart(int kesimGunu, int sonOdemeGunu, decimal ekstre = 1500m)
        => new(new KrediKartiDto(1, "A", new DateOnly(2026, 1, kesimGunu), new DateOnly(2026, 1, sonOdemeGunu),
            100000m, 0m, EkstreBorc: ekstre));

    private static List<(DateOnly, HatirlatmaTuru)> GunGun(KrediKartiGorunum k, DateOnly bas, DateOnly bit)
    {
        var l = new List<(DateOnly, HatirlatmaTuru)>();
        for (var g = bas; g <= bit; g = g.AddDays(1))
            foreach (var h in KartHatirlatici.VadesiGelenler(new[] { k }, g, g.AddDays(-1)))
                l.Add((h.Tarih, h.Tur));
        return l;
    }

    // ---- Telafi ----

    [Fact]
    public void Sonraki_kesim_kacan_son_odeme_gununu_gizlemez()
    {
        // kesim 15 / son ödeme 12: 10-12 son gün kaçırıldı, 10-15 yeni kesim; bugün 10-17
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(15, 12) }, new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 10));

        Assert.Equal(2, h.Count);
        Assert.Contains(h, x => x.Tur == HatirlatmaTuru.SonOdemeGunu && x.Tarih == new DateOnly(2026, 10, 12));
        Assert.Contains(h, x => x.Tur == HatirlatmaTuru.Kesim && x.Tarih == new DateOnly(2026, 10, 15));
    }

    [Fact]
    public void Kesim_son_odemeden_onceyse_yalniz_son_odeme_doner()
    {
        // kesim 15 / son ödeme 22: 15 kesim, 19 üç gün, 22 son gün; hepsi kaçtı → yalnız son gün
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(15, 22) }, new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 14));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Ileri_tarihli_son_kontrol_gecersiz_sayilir()
    {
        // Saat ileri alınmıştı: son kontrol 2026-12-01 yazılmış. Bugün 07-22 son ödeme günü.
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(15, 22) }, new DateOnly(2026, 7, 22), new DateOnly(2026, 12, 1));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Son_kontrol_duzeltme_ileri_tarihi_dune_ceker()
    {
        var bugun = new DateOnly(2026, 7, 22);
        Assert.Equal(bugun.AddDays(-1), KartHatirlatici.SonKontrolDuzelt(new DateOnly(2027, 1, 1), bugun));
        Assert.Equal(bugun, KartHatirlatici.SonKontrolDuzelt(bugun, bugun));
        Assert.Equal(new DateOnly(2026, 7, 1), KartHatirlatici.SonKontrolDuzelt(new DateOnly(2026, 7, 1), bugun));
        Assert.Null(KartHatirlatici.SonKontrolDuzelt(null, bugun));
    }

    // ---- Vade kuralları (Core ile aynı) ----

    [Fact]
    public void Kesim_ve_son_odeme_ayni_gunse_vade_sonraki_ay_ve_her_iki_hatirlatma_gelir()
    {
        // kesim 15 / son ödeme 15: 15 Eylül ekstresinin vadesi 15 Ekim. 15 Ekim hem eski ekstrenin
        // son günü hem yeni kesim.
        var l = GunGun(Kart(15, 15), new DateOnly(2026, 9, 14), new DateOnly(2026, 10, 16));
        Assert.Equal(new (DateOnly, HatirlatmaTuru)[]
        {
            (new DateOnly(2026, 9, 15), HatirlatmaTuru.SonOdemeGunu),   // Ağustos ekstresinin vadesi
            (new DateOnly(2026, 9, 15), HatirlatmaTuru.Kesim),
            (new DateOnly(2026, 10, 12), HatirlatmaTuru.SonOdeme3Gun),
            (new DateOnly(2026, 10, 15), HatirlatmaTuru.SonOdemeGunu),
            (new DateOnly(2026, 10, 15), HatirlatmaTuru.Kesim),
        }, l);
    }

    [Fact]
    public void Kesim_ve_son_odeme_ayni_gunse_serit_her_gun_yanmaz()
    {
        var k = Kart(15, 15);
        var acik = new List<DateOnly>();
        for (var g = new DateOnly(2026, 9, 16); g <= new DateOnly(2026, 10, 16); g = g.AddDays(1))
            if (KartHatirlatici.OdemeBekliyor(k, g)) acik.Add(g);

        Assert.Equal(new[] { new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15) }, acik);
    }

    [Fact]
    public void Subat_kirpmasinda_vade_kesimle_cakismaz_mart_vadesi_hatirlatilir()
    {
        // kesim 30 / son ödeme 29, 2027 (artık yıl değil): Şubat kesimi 28'e kırpılır → vade 29 Mart
        var l = GunGun(Kart(30, 29), new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 31));
        Assert.Equal(new (DateOnly, HatirlatmaTuru)[]
        {
            (new DateOnly(2027, 2, 25), HatirlatmaTuru.SonOdeme3Gun),   // Ocak ekstresi (30 Oca) → vade 28 Şub
            (new DateOnly(2027, 2, 28), HatirlatmaTuru.SonOdemeGunu),
            (new DateOnly(2027, 2, 28), HatirlatmaTuru.Kesim),
            (new DateOnly(2027, 3, 26), HatirlatmaTuru.SonOdeme3Gun),
            (new DateOnly(2027, 3, 29), HatirlatmaTuru.SonOdemeGunu),
            (new DateOnly(2027, 3, 30), HatirlatmaTuru.Kesim),
        }, l);
    }

    [Fact]
    public void Kesim_31_son_odeme_30_artik_yilda_mart_30_hatirlatilir()
    {
        var l = GunGun(Kart(31, 30), new DateOnly(2028, 3, 1), new DateOnly(2028, 3, 31));
        Assert.Contains((new DateOnly(2028, 3, 27), HatirlatmaTuru.SonOdeme3Gun), l);
        Assert.Contains((new DateOnly(2028, 3, 30), HatirlatmaTuru.SonOdemeGunu), l);
        Assert.Contains((new DateOnly(2028, 3, 31), HatirlatmaTuru.Kesim), l);
    }

    [Fact]
    public void Kesim_1_son_odeme_2_uc_gun_noktasi_kesimden_once_dusunce_atlanir()
    {
        var l = GunGun(Kart(1, 2), new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 3));
        Assert.Equal(new (DateOnly, HatirlatmaTuru)[]
        {
            (new DateOnly(2026, 10, 1), HatirlatmaTuru.Kesim),
            (new DateOnly(2026, 10, 2), HatirlatmaTuru.SonOdemeGunu),
        }, l);
    }

    [Fact]
    public void Kesim_1_son_odeme_2_serit_yeni_ekstreye_kesim_gunu_baglanir()
    {
        var k = Kart(1, 2);
        // 30 Eylül'de açık ekstre hâlâ Eylül ekstresi (Ekim ekstresi henüz kesilmedi).
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 9, 30)));
        // "3 gün kala" (29 Eylül) kesimden önce: Ekim ekstresinin şeridi kesim günü başlar.
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 10, 1)));
        Assert.True(KartHatirlatici.OdemeBekliyor(k, new DateOnly(2026, 10, 1)));
        Assert.False(KartHatirlatici.OdemeBekliyor(Kart(1, 2, ekstre: 0m), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Acik_ekstre_onceki_ekstrenin_vadesi_gecmediyse_odur()
    {
        var k = Kart(15, 15);
        Assert.Equal((new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 15)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 10, 15)));
        Assert.Equal((new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 15)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public void Normal_kart_acik_ekstresi_son_kesimdir()
    {
        var k = Kart(15, 22);
        Assert.Equal((new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 22)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 7, 20)));
        Assert.Equal((new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 22)), KartHatirlatici.AcikEkstre(k, new DateOnly(2026, 7, 10)));
    }
}

public class KartTarihSiniriTests
{
    [Fact]
    public void SonrakiGun_esit_gunu_kabul_etmez_sonraki_aya_gecer()
        => Assert.Equal(new DateOnly(2026, 8, 15), KartTarih.SonrakiGun(15, new DateOnly(2026, 7, 15)));

    [Fact]
    public void SonrakiGun_subat_kirpilmis_referanstan_marta_gecer()
        => Assert.Equal(new DateOnly(2027, 3, 29), KartTarih.SonrakiGun(29, new DateOnly(2027, 2, 28)));

    [Fact]
    public void SonrakiGun_ay_sonu_referansindan_sonraki_ay_kirpilir()
        => Assert.Equal(new DateOnly(2027, 2, 28), KartTarih.SonrakiGun(30, new DateOnly(2027, 1, 31)));
}
