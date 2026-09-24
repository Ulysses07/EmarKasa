using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

public class KartHatirlaticiTests
{
    // kesim 15, son ödeme 22
    private static KrediKartiGorunum Kart(decimal ekstre, params (DateOnly tarih, decimal tutar)[] odemeler)
    {
        var g = new KrediKartiGorunum(new KrediKartiDto(1, "A", new DateOnly(2026,7,15),
            new DateOnly(2026,7,22), 100000m, 1000m, EkstreBorc: ekstre));
        foreach (var o in odemeler) g.Odemeler.Add(new KartOdemeDto(0, 1, o.tarih, o.tutar, null));
        return g;
    }

    [Fact]
    public void Borc_yoksa_hicbir_hatirlatma_yok()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(0m) }, new DateOnly(2026,7,22));
        Assert.Empty(h);
    }

    [Fact]
    public void Kesim_gununde_ekstre_bildirimi()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,15));
        Assert.Equal(HatirlatmaTuru.Kesim, h.Single().Tur);
    }

    [Fact]
    public void Son_odemeye_3_gun_kala_bildirim()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,19));
        Assert.Equal(HatirlatmaTuru.SonOdeme3Gun, h.Single().Tur);
    }

    [Fact]
    public void Son_odeme_gununde_bildirim()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,22));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Ekstre_tam_odenince_hatirlatma_susar()
    {
        // API, kesim sonrası ödemeyi ekstre borcundan düşer: tam ödeme → ekstre 0
        var h = KartHatirlatici.VadesiGelenler(
            new[] { Kart(0m, (new DateOnly(2026,7,16), 1500m)) }, new DateOnly(2026,7,22));
        Assert.Empty(h);
    }

    [Fact]
    public void Kismi_odemede_kalan_tutarla_hatirlatma_surer()
    {
        // 1500 ekstreden 500 ödendi → kalan 1000 ile son ödeme günü hatırlatılır
        var h = KartHatirlatici.VadesiGelenler(
            new[] { Kart(1000m, (new DateOnly(2026,7,16), 500m)) }, new DateOnly(2026,7,22));
        var t = h.Single();
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, t.Tur);
        Assert.Equal(1000m, t.EkstreBorc);
    }

    [Fact]
    public void Bilgisayar_kapaliyken_kacirilan_hatirlatma_sonraki_acilista_gelir()
    {
        // 3-gün hatırlatması 19'unda; son kontrol 17'si, bugün 20'si → kaçan 19 gösterilir
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,20), new DateOnly(2026,7,17));
        Assert.Equal(HatirlatmaTuru.SonOdeme3Gun, h.Single().Tur);
    }

    [Fact]
    public void Kacirilan_gunlerden_yalniz_en_guncel_hatirlatma_gelir()
    {
        // 15 kesim, 19 üç gün, 22 son gün kaçırıldı; 23'ünde yalnız "son ödeme günü"
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,23), new DateOnly(2026,7,14));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Ayni_gun_ikinci_kontrol_tekrar_bildirim_uretmez()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,22), new DateOnly(2026,7,22));
        Assert.Empty(h);
    }

    [Fact]
    public void Kesim_oncesi_odeme_bu_donemi_susturmaz()
    {
        // ödeme kesimden ÖNCE (14'ü) → bu dönem ödemesi sayılmaz
        var h = KartHatirlatici.VadesiGelenler(
            new[] { Kart(1500m, (new DateOnly(2026,7,14), 1500m)) }, new DateOnly(2026,7,22));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    // Belirli kesim/son ödeme günleriyle kart (yalnız gün numaraları önemli).
    private static KrediKartiGorunum KartGun(DateOnly kesim, DateOnly sonOdeme, decimal ekstre)
        => new(new KrediKartiDto(1, "A", kesim, sonOdeme, 100000m, 1000m, EkstreBorc: ekstre));

    [Fact]
    public void Son_odeme_gunu_kesimden_kucukse_sonraki_aya_sarar()
    {
        // kesim 25, son ödeme 10 → son ödeme kesimden SONRAKİ aya düşer
        var kart = KartGun(new DateOnly(2026,7,25), new DateOnly(2026,8,10), 1500m);
        var h = KartHatirlatici.VadesiGelenler(new[] { kart }, new DateOnly(2026,8,10));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Yil_sarmasi_aralik_kesim_ocak_son_odeme()
    {
        // kesim 20 (Aralık), son ödeme 5 (Ocak) → yıl sınırını doğru geçer
        var kart = KartGun(new DateOnly(2025,12,20), new DateOnly(2026,1,5), 1500m);
        var h = KartHatirlatici.VadesiGelenler(new[] { kart }, new DateOnly(2026,1,5));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }
}

public class HatirlatmaDurumuTests
{
    [Fact]
    public void Son_kontrol_gunu_dosyada_saklanir()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "kasa-hd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = new HatirlatmaDurumu(klasor);
            Assert.Null(d.SonKontrol);
            d.SonKontrol = new DateOnly(2026, 7, 20);
            Assert.Equal(new DateOnly(2026, 7, 20), new HatirlatmaDurumu(klasor).SonKontrol);
        }
        finally { if (Directory.Exists(klasor)) Directory.Delete(klasor, true); }
    }
}
