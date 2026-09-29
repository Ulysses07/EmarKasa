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

    /// <summary>R3 notu: adet motor satırı değil kaynak kayıttır. Kanallara bölünmüş bir gider ve eski kredinin türetilmiş
    /// taksitleri kullanıcının girdiği kayıt sayısını şişirmez; tutar bütün satırların toplamı olarak kalır.</summary>
    [Fact]
    public void Adet_kaynak_kayit_duzeyindedir_bolunmus_gider_ve_turetilmis_taksitler_bir_kez_sayilir()
    {
        // Eski kredi 20 Mart'ta çekilmiş: taksitleri 5 Nisan, 5 Mayıs, 5 Haziran, 5 Temmuz; ilk üçü başlangıçtan (17 Haziran) önce.
        var taksitler = KrediTuretici.TaksitGiderleri(new Kredi("Eski kredi", 0m, new DateOnly(2026, 3, 20), 4, 500m, 5, "MEZAT"))
            .Select(t => t with { Kaynak = "Kredi:3" });
        Islem[] islemler =
        [
            // Üç kanala bölünmüş tek gider: aynı kaynak.
            new(new DateOnly(2026, 6, 10), "Bölünen gider", 100m, "MEZAT", GiderTipi.Cari) { Kaynak = "Islem:7" },
            new(new DateOnly(2026, 6, 10), "Bölünen gider", 100m, "PERAKENDE", GiderTipi.Cari) { Kaynak = "Islem:7" },
            new(new DateOnly(2026, 6, 10), "Bölünen gider", 100m, "TOPTAN", GiderTipi.Cari) { Kaynak = "Islem:7" },
            .. taksitler,
            // Anahtarsız satır tek başına bir kayıttır (eski davranış).
            new(new DateOnly(2026, 6, 12), "Maaş", 300m, Kanallar.Ortak, GiderTipi.SabitGider),
            new(new DateOnly(2026, 6, 12), "Maaş", 300m, Kanallar.Ortak, GiderTipi.SabitGider),
        ];
        // Kaynaklar: bölünen gider, eski kredi ve iki anahtarsız satır.
        Assert.Equal((4, 300m + 1_500m + 600m), HesapMotoru.BaslangicOncesi(islemler, Donemler));
        Assert.Equal((4, 300m + 500m + 600m), HesapMotoru.BaslangicOncesi(islemler, Donemler, (2026, 6)));
        Assert.Equal((1, 500m), HesapMotoru.BaslangicOncesi(islemler, Donemler, (2026, 4)));
        // Kaynak anahtarı hesaba girmez: rapor anahtarsız satırlarla birebir aynıdır.
        var anahtarsiz = islemler.Select(i => i with { Kaynak = null }).ToList();
        Assert.Equal(HesapMotoru.AylikHesapla(2026, 6, UcKanal, anahtarsiz, [], Donemler).Kanallar,
            HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, [], Donemler).Kanallar);
        Assert.Equal(HesapMotoru.HaftalikHesapla(0m, UcKanal, anahtarsiz, [], Donemler).Select(h => (h.KasaDevir, h.DagilimBekleyenTutar)),
            HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], Donemler).Select(h => (h.KasaDevir, h.DagilimBekleyenTutar)));
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
