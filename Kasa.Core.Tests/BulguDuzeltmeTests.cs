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
