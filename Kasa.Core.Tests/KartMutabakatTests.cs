using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>Kart ekstresi mutabakatı (Paket D): kapanmış dönemler ve dönem dökümü; kart borcu hesabı değişmez.</summary>
public class KartMutabakatTests
{
    private static readonly KartHarcama[] Harcamalar =
    [
        new(new DateOnly(2026, 7, 20), 40m),
        new(new DateOnly(2026, 8, 10), 200m),
        new(new DateOnly(2026, 8, 20), 300m),
        new(new DateOnly(2026, 9, 10), 50m),
        new(new DateOnly(2026, 9, 15), 5m),    // kesim günü: döneme dahil
        new(new DateOnly(2026, 9, 20), 70m),   // kesimden sonra: sonraki dönem
    ];
    private static readonly KartOdeme[] Odemeler =
    [
        new(new DateOnly(2026, 8, 25), 100m),
        new(new DateOnly(2026, 9, 16), 999m),  // kesimden sonra
    ];

    [Fact]
    public void Kapanmis_donemler_en_yeni_once_ve_birbirini_izler()
    {
        var l = KartMutabakat.KapanmisDonemler(15, new DateOnly(2026, 9, 24), 3);
        Assert.Equal(
        [
            new KartEkstreDonemi(new DateOnly(2026, 8, 16), new DateOnly(2026, 9, 15)),
            new KartEkstreDonemi(new DateOnly(2026, 7, 16), new DateOnly(2026, 8, 15)),
            new KartEkstreDonemi(new DateOnly(2026, 6, 16), new DateOnly(2026, 7, 15)),
        ], l);
        Assert.Empty(KartMutabakat.KapanmisDonemler(15, new DateOnly(2026, 9, 24), 0));
    }

    [Fact]
    public void Kesim_gunu_bugunse_donem_kapanmis_sayilir_kisa_ayda_ay_sonuna_kirpilir()
    {
        Assert.Equal(new DateOnly(2026, 9, 15), KartMutabakat.KapanmisDonemler(15, new DateOnly(2026, 9, 15), 1)[0].Kesim);
        var l = KartMutabakat.KapanmisDonemler(31, new DateOnly(2026, 3, 10), 2);
        Assert.Equal(new KartEkstreDonemi(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), l[0]);
        Assert.Equal(new KartEkstreDonemi(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), l[1]);
    }

    [Fact]
    public void Donem_yalniz_kesim_gunune_denk_gelen_tarihte_bulunur()
    {
        Assert.Equal(new KartEkstreDonemi(new DateOnly(2026, 8, 16), new DateOnly(2026, 9, 15)),
            KartMutabakat.Donem(15, new DateOnly(2026, 9, 15)));
        Assert.Null(KartMutabakat.Donem(15, new DateOnly(2026, 9, 14)));
        Assert.Equal(new DateOnly(2026, 2, 28), KartMutabakat.Donem(30, new DateOnly(2026, 2, 28))!.Kesim);
    }

    [Fact]
    public void Donem_dokumu_devreden_arti_harcama_eksi_odeme_ve_kart_durumuyla_ayni()
    {
        var donem = KartMutabakat.Donem(15, new DateOnly(2026, 9, 15))!;
        var o = KartMutabakat.Ozet(100m, Harcamalar, Odemeler, 15, donem);
        Assert.Equal(100m + 40m + 200m, o.DevredenBorc);          // 15 Ağustos sonu
        Assert.Equal(300m + 50m + 5m, o.DonemHarcama);
        Assert.Equal(100m, o.DonemOdeme);
        Assert.Equal(o.DevredenBorc + o.DonemHarcama - o.DonemOdeme, o.DonemSonuBorc);
        // Kural değişmez: dönem sonu borcu, kesim gününde KartHesap.Durum'un güncel borcudur.
        Assert.Equal(KartHesap.Durum(100m, Harcamalar, Odemeler, 15, new DateOnly(2026, 9, 15)).GuncelBorc, o.DonemSonuBorc);
    }

    [Fact]
    public void Donem_dokumu_kurusa_yuvarlar()
    {
        var donem = KartMutabakat.Donem(15, new DateOnly(2026, 9, 15))!;
        var o = KartMutabakat.Ozet(0m, [new KartHarcama(new DateOnly(2026, 9, 1), 10.005m)], [], 15, donem);
        Assert.Equal(Para.Yuvarla(10.005m), o.DonemHarcama);
        Assert.Equal(o.DonemHarcama, o.DonemSonuBorc);
    }
}
