using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// K1 (kullanıcı kararı): takip başlangıcından önce tarihli mevcut giderler olduğu gibi kalır, rakamlar değişmez; raporda
/// uyarıyla işaretlenir. Böyle bir gider haftalık kasaya hiç girmez, aylık raporda gider ayında sayılır. Ertelemeli eski
/// K.K'nın etki ayı başlangıç ayı ya da sonrasıysa haftalık kasada da ay sonunda düştüğü için tutarlıdır, işaretlenmez.
/// </summary>
public class BaslangicOncesiTests
{
    private static readonly Kanal[] UcKanal = [new("MEZAT"), new("PERAKENDE"), new("TOPTAN")];
    // Takip başlangıcı ayın ortası: 17 Haziran 2026.
    private static readonly IReadOnlyList<Donem> Donemler = DonemUretici.Uret(new DateOnly(2026, 6, 17), new DateOnly(2026, 7, 31));

    private static readonly Islem[] Islemler =
    [
        new(new DateOnly(2026, 6, 10), "Mezat öncesi", 5_000m, "MEZAT", GiderTipi.Cari),       // başlangıç öncesi, Haziran
        new(new DateOnly(2026, 6, 12), "Maaş", 300m, Kanallar.Ortak, GiderTipi.SabitGider),    // başlangıç öncesi, Haziran
        new(new DateOnly(2026, 5, 20), "Eski K.K", 700m, "MEZAT", GiderTipi.KrediKarti),       // etki ayı Haziran: haftalıkta 30 Haziran'da düşer
        new(new DateOnly(2026, 4, 10), "Daha eski K.K", 90m, "TOPTAN", GiderTipi.KrediKarti),  // etki ayı Mayıs: hiçbir dönemde yok
        new(new DateOnly(2026, 6, 20), "Tedarik", 1_000m, "MEZAT", GiderTipi.Cari),            // olağan
    ];

    [Fact]
    public void Baslangic_oncesi_gider_aylikta_sayilir_haftalikta_yoktur_ve_ay_bazinda_isaretlenir()
    {
        var haziran = HesapMotoru.AylikHesapla(2026, 6, UcKanal, Islemler, [], Donemler);
        // Tutarlar değişmez: başlangıç öncesi gider Haziran sonucunda sayılır.
        Assert.Equal(5_000m + 1_000m, haziran.Kanallar.Single(k => k.Kanal == "MEZAT").CariGiden);
        Assert.Equal(700m, haziran.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
        Assert.Equal(100m, haziran.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay);
        // Haftalık kasa yalnız olağan gideri ve ay sonunda eski K.K'yı düşer.
        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, Islemler, [], Donemler);
        Assert.Equal(-(1_000m + 700m), haftalik.Where(h => h.Donem.Ay == 6).Sum(h => h.KasaSonucu));

        Assert.Equal((2, 5_300m), HesapMotoru.BaslangicOncesi(Islemler, Donemler, (2026, 6)));
        Assert.Equal((1, 90m), HesapMotoru.BaslangicOncesi(Islemler, Donemler, (2026, 5)));
        Assert.Equal((0, 0m), HesapMotoru.BaslangicOncesi(Islemler, Donemler, (2026, 7)));
        Assert.Equal((3, 5_390m), HesapMotoru.BaslangicOncesi(Islemler, Donemler));
    }

    [Fact]
    public void Donem_yoksa_ayin_butun_giderleri_baslangic_oncesidir()
    {
        // Haziran'ın aylık sonucuna giren dört satırın hepsi (ertelemeli Mayıs K.K'sı ve olağan gider dahil).
        Assert.Equal((4, 7_000m), HesapMotoru.BaslangicOncesi(Islemler, [], (2026, 6)));
    }

    [Fact]
    public void Uyari_metni_adet_toplam_ve_farki_turkce_soyler_bos_durumda_yoktur()
    {
        Assert.Null(HesapMotoru.BaslangicOncesiUyarisi((0, 0m), aylik: true));
        Assert.Equal("Takip başlangıcından önce tarihli 2 kayıt, toplam 5.300,00 ₺ — raporlarda farklı işlenir: bu ayın sonucunda sayılır, "
            + "haftalık kasaya ve kanal devrine girmez. Kayıtlar ve tutarlar olduğu gibi korunur.",
            HesapMotoru.BaslangicOncesiUyarisi((2, 5_300m), aylik: true));
        Assert.Equal("Takip başlangıcından önce tarihli 3 kayıt, toplam -1.234.567,50 ₺ — raporlarda farklı işlenir: haftalık kasaya ve "
            + "kanal devrine girmez, aylık raporda gider ayının sonucunda sayılır. Kayıtlar ve tutarlar olduğu gibi korunur.",
            HesapMotoru.BaslangicOncesiUyarisi((3, -1_234_567.5m), aylik: false));
    }
}
