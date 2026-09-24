using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>2026-09 incelemesinde bulunan hesap hatalarının kilit testleri.</summary>
public class BulguDuzeltmeTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };

    [Fact]
    public void Ortak_kanal_kk_si_aylikta_da_bir_sonraki_aya_ertelenir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Banka", 300m, Kanallar.Ortak, GiderTipi.KrediKarti) };

        var haziran = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, [], donemler);
        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, islemler, [], donemler);

        Assert.Equal(0m, haziran.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(300m, temmuz.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(100m, temmuz.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay);
    }

    [Fact]
    public void Ortak_kk_ile_haftalik_ve_aylik_toplam_tutar()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 10), "Banka", 300m, Kanallar.Ortak, GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 6, 12), "Kira", 900m, Kanallar.Ortak, GiderTipi.SabitGider),
        };
        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], donemler).Sum(o => o.KasaSonucu);
        var aylik = new[] { 6, 7 }.Sum(ay => HesapMotoru.AylikHesapla(2026, ay, UcKanal, islemler, [], donemler).Kanallar.Sum(k => k.AySonucu));
        Assert.Equal(haftalik, aylik);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(17)]
    [InlineData(29)]
    public void Onceki_ayin_kk_si_icinde_bulunulan_ayda_haftadan_haftaya_kaymaz(int gun)
    {
        var bugun = new DateOnly(2026, 7, gun);
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), bugun);
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "X", 500m, "MEZAT", GiderTipi.KrediKarti) };

        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], donemler);
        var dusulen = ozet.Where(o => o.ToplamGiden > 0).Select(o => o.Donem.Start).ToList();

        if (gun < 27) Assert.Empty(dusulen);                          // son haftaya gelinmedi → bekler
        else Assert.Equal([new DateOnly(2026, 7, 27)], dusulen);      // ayın son haftası (27–31)
    }

    [Fact]
    public void Donem_baslangicina_denk_gelmeyen_gelen_icerdigi_doneme_sayilir()
    {
        // Takip başlangıcı 3 Haziran iken girilmiş gelen; başlangıç sonradan 1 Haziran yapıldı.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var gelenler = new[] { new Gelen(new DateOnly(2026, 6, 3), "MEZAT", 1_000m) };

        var hafta = HesapMotoru.HaftalikHesapla(0m, UcKanal, [], gelenler, donemler)[0];
        var ay = HesapMotoru.AylikHesapla(2026, 6, UcKanal, [], gelenler, donemler);

        Assert.Equal(1_000m, hafta.Kanallar.Single(k => k.Kanal == "MEZAT").Gelen);
        Assert.Equal(1_000m, ay.Kanallar.Single(k => k.Kanal == "MEZAT").Gelen);
    }

    [Fact]
    public void Ayin_son_donemi_ay_sonunu_iceren_donemdir()
    {
        Assert.True(DonemUretici.AyinSonDonemiMi(new Donem(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 28))));
        Assert.False(DonemUretici.AyinSonDonemiMi(new Donem(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 26))));
    }
}

/// <summary>Kasa kart borcunu gerçek ödeme tarihinde düşer (karta bağlı harcamalar).</summary>
public class GercekKartOdemeTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };

    [Fact]
    public void Karta_bagli_harcama_kasadan_dusmez_odeme_kendi_haftasinda_duser()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Market", 1_000m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: 1) };
        var odemeler = new[] { new KartOdeme(new DateOnly(2026, 7, 8), 400m), new KartOdeme(new DateOnly(2026, 7, 21), 600m) };

        var ozet = HesapMotoru.HaftalikHesapla(5_000m, UcKanal, islemler, [], donemler, odemeler);
        var dusulen = ozet.Where(o => o.ToplamGiden > 0).Select(o => (o.Donem.Start, o.ToplamGiden)).ToList();

        Assert.Equal([(new DateOnly(2026, 7, 6), 400m), (new DateOnly(2026, 7, 20), 600m)], dusulen);
        Assert.Equal(4_000m, ozet[^1].KasaDevir);
    }

    [Fact]
    public void Kismi_odemede_kasadan_yalniz_odenen_cikar()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Market", 1_000m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: 1) };
        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], donemler, [new KartOdeme(new DateOnly(2026, 7, 8), 250m)]);
        Assert.Equal(-250m, ozet[^1].KasaDevir);
    }

    [Fact]
    public void Karta_bagli_olmayan_kk_eski_kuralla_ertelenir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Eski K.K", 300m, "MEZAT", GiderTipi.KrediKarti) };
        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], donemler, []);
        Assert.Equal(300m, ozet.Single(o => o.Donem.Start == new DateOnly(2026, 7, 27)).ToplamGiden);
    }

    [Fact]
    public void Aylik_kar_karta_bagli_harcamayi_kanalina_bir_sonraki_ay_yazar()
    {
        // Aylık rapor kârlılıktır: ödeme zamanlamasından bağımsız, harcamanın kanalına düşer.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Market", 1_000m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: 1) };
        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, islemler, [], donemler);
        Assert.Equal(1_000m, temmuz.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
    }
}
