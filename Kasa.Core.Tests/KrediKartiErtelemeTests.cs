using Kasa.Core;

namespace Kasa.Core.Tests;

public class KrediKartiErtelemeTests
{
    private static readonly Kanal[] UcKanal =
    {
        new("MEZAT"), new("PERAKENDE"), new("TOPTAN"),
    };

    // ---- AYLIK ----

    [Fact]
    public void Bu_ayin_kk_si_bu_ayin_sonucundan_dusulmez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 3, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(0m, mezat.KrediKarti);   // Mart K.K'sı Mart'a yansımaz
        Assert.Equal(0m, mezat.AySonucu);
    }

    [Fact]
    public void Onceki_ayin_kk_si_bu_ayin_sonucundan_dusulur()
    {
        // Mart'ta MEZAT ile 10.000 kart harcaması → Nisan sonucunda -10.000.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var nisan = HesapMotoru.AylikHesapla(2026, 4, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = nisan.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(10_000m, mezat.KrediKarti);
        Assert.Equal(-10_000m, mezat.AySonucu);
    }

    [Fact]
    public void Ocak_ayinin_kk_terimi_bir_onceki_yilin_aralik_ayindan_gelir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31));
        var islemler = new[]
        {
            new Islem(new DateOnly(2025, 12, 10), "K.K", 5_000m, "TOPTAN", GiderTipi.KrediKarti),
        };

        var ocak = HesapMotoru.AylikHesapla(2026, 1, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(5_000m, ocak.Kanallar.Single(k => k.Kanal == "TOPTAN").KrediKarti);
    }

    // ---- HAFTALIK (KASA) ----

    [Fact]
    public void Kk_kendi_haftasinda_kasadan_dusmez()
    {
        // Mart'ta tek dönem, sadece 10.000 kart harcaması. Kasa değişmemeli.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 3), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozet = HesapMotoru.HaftalikHesapla(100_000m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(0m, ozet[^1].ToplamGiden);       // KK bu hafta çıkmadı
        Assert.Equal(100_000m, ozet[^1].KasaDevir);   // kasa aynı
    }

    [Fact]
    public void Kk_bir_sonraki_ayin_son_doneminde_kasadan_duser()
    {
        // Mart K.K 10.000 → Nisan'ın SON döneminde kasadan çıkar.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(100_000m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);

        // Nisan'ın son dönemi = ay=4 olan dönemlerin en geç Start'lısı.
        var nisanSon = ozetler.Where(o => o.Donem.Ay == 4).OrderBy(o => o.Donem.Start).Last();
        Assert.Equal(10_000m, nisanSon.ToplamGiden);   // ertelenen KK burada çıktı

        // En sondaki kasa devri: 100.000 - 10.000 = 90.000
        Assert.Equal(90_000m, ozetler[^1].KasaDevir);
    }

    [Fact]
    public void Kk_kanal_haftalik_devrini_etkilemez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 3), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = ozet[^1].Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(0m, mezat.Giden);   // kanal devri yalnız Cari sayar
        Assert.Equal(0m, mezat.Devir);
    }

    [Fact]
    public void Iki_ay_boyunca_haftalik_kasa_toplami_aylik_ay_sonucu_toplamina_esittir()
    {
        // Haziran KK'sı Temmuz'a ertelenir; Σ haftalık (Haz+Tem) == Σ aylık (Haz+Tem).
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var ilk = donemler[0].Start;
        var gelenler = new[] { new Gelen(ilk, "MEZAT", 200_000m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 3), "MEZAT-cari", 50_000m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 10), "PER-kk", 45_500m, "PERAKENDE", GiderTipi.KrediKarti),
        };

        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);
        decimal haftalikToplam = haftalik.Sum(o => o.KasaSonucu);

        var haziran = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, donemler);
        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, islemler, gelenler, donemler);
        decimal aylikToplam = haziran.Kanallar.Sum(k => k.AySonucu) + temmuz.Kanallar.Sum(k => k.AySonucu);

        Assert.Equal(haftalikToplam, aylikToplam);
    }
}
