using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>ERP12 – kasa eşleştirme kuralı: aynı tutar (kuruş), ±3 gün, Türkçe normalleştirilmiş cari benzerliği.</summary>
public class Erp12KarsilastiriciTests
{
    private static DateOnly G(int gun, int ay = 9) => new(2026, ay, gun);
    private static Erp12Satir E(int no, int gun, string cari, decimal tutar) => new(no, G(gun), cari, tutar);
    private static IslemDto K(int id, int gun, string cari, decimal tutar, int ay = 9)
        => new(id, G(gun, ay), cari, tutar, "MEZAT", GiderTipi.Cari, null);

    [Fact]
    public void Ayni_tutar_yakin_tarih_benzer_cari_eslesir()
    {
        var s = Erp12Karsilastirici.Karsilastir(
            [E(1, 12, "YILMAZ GIDA SAN. VE TİC. LTD. ŞTİ.", 1234.56m)],
            [K(10, 14, "Yılmaz Gıda", 1234.56m)]);

        var e = Assert.Single(s.Eslesenler);
        Assert.Equal(10, e.Islem.Id);
        Assert.Equal(2, e.GunFarki);
        Assert.Equal(1.0, e.Benzerlik);
        Assert.Empty(s.YalnizErp12);
        Assert.Empty(s.YalnizKasa);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(-3, true)]
    [InlineData(4, false)]
    [InlineData(-4, false)]
    public void Tarih_toleransi_uc_gun(int fark, bool eslesir)
    {
        var s = Erp12Karsilastirici.Karsilastir([E(1, 15, "Kaya Ambalaj", 500m)], [K(10, 15 + fark, "Kaya Ambalaj", 500m)]);
        Assert.Equal(eslesir, s.Eslesenler.Count == 1);
    }

    [Fact]
    public void Tutar_kurusu_kurusuna_ayni_olmali_isaret_onemsiz()
    {
        var s = Erp12Karsilastirici.Karsilastir(
            [E(1, 15, "Kaya", 500.01m), E(2, 15, "Demir", 750m)],
            [K(10, 15, "Kaya", 500m), K(11, 15, "Demir", -750m)]);

        Assert.Equal([2], s.Eslesenler.Select(e => e.Erp.SatirNo));
        Assert.Equal([1], s.YalnizErp12.Select(e => e.Kayit.SatirNo));
        Assert.Equal([10], s.YalnizKasa.Select(k => k.Kayit.Id));
    }

    [Fact]
    public void Tutar_ve_tarih_tutsa_da_farkli_cari_eslesmez_ama_ipucu_verir()
    {
        var s = Erp12Karsilastirici.Karsilastir([E(1, 15, "Kaya Ambalaj", 500m)], [K(10, 15, "Demir Nakliyat", 500m)]);

        Assert.Empty(s.Eslesenler);
        Assert.Equal("Olası: kasada Demir Nakliyat · 15.09.2026 · 500,00 ₺ (tutar ve tarih tutuyor, cari farklı yazılmış)",
            Assert.Single(s.YalnizErp12).Ipucu);
        Assert.Equal("Olası: ERP12'de Kaya Ambalaj · 15.09.2026 · 500,00 ₺ (satır 1) (tutar ve tarih tutuyor, cari farklı yazılmış)",
            Assert.Single(s.YalnizKasa).Ipucu);
    }

    [Fact]
    public void Tarihi_uzak_ayni_cari_ve_tutar_ipucu_gun_farkini_soyler()
    {
        var s = Erp12Karsilastirici.Karsilastir([E(1, 5, "Kaya Ambalaj", 500m)], [K(10, 20, "Kaya Ambalaj", 500m)]);
        Assert.Empty(s.Eslesenler);
        Assert.EndsWith("(tarih 15 gün farklı)", s.YalnizErp12[0].Ipucu);
    }

    [Fact]
    public void Tutari_yuzde_birden_az_farkli_ipucu_farki_soyler()
    {
        var s = Erp12Karsilastirici.Karsilastir([E(1, 5, "Kaya Ambalaj", 1000m)], [K(10, 6, "Kaya Ambalaj", 1005m)]);
        Assert.Empty(s.Eslesenler);
        Assert.EndsWith("(tutar 5,00 ₺ farklı)", s.YalnizErp12[0].Ipucu);

        var uzak = Erp12Karsilastirici.Karsilastir([E(1, 5, "Kaya Ambalaj", 1000m)], [K(10, 6, "Kaya Ambalaj", 1011m)]);
        Assert.Null(uzak.YalnizErp12[0].Ipucu);
    }

    [Fact]
    public void Her_kayit_en_fazla_bir_kez_eslesir_ayni_gunlu_tekrarlar_sirayla()
    {
        var s = Erp12Karsilastirici.Karsilastir(
            [E(1, 10, "Kaya", 100m), E(2, 10, "Kaya", 100m), E(3, 10, "Kaya", 100m)],
            [K(20, 10, "Kaya", 100m), K(21, 11, "Kaya", 100m)]);

        Assert.Equal(2, s.Eslesenler.Count);
        Assert.Equal([20, 21], s.Eslesenler.Select(e => e.Islem.Id).Order());
        Assert.Equal([3], s.YalnizErp12.Select(e => e.Kayit.SatirNo));
        Assert.Empty(s.YalnizKasa);
    }

    [Fact]
    public void Acgozlu_secim_once_benzerlik_sonra_yakin_tarih()
    {
        // ERP satırı hem "Kaya Ambalaj" (tam) hem "Kaya" (kısmi, daha yakın gün) adayına uyuyor: tam benzerlik kazanır.
        var s = Erp12Karsilastirici.Karsilastir(
            [E(1, 10, "Kaya Ambalaj", 100m)],
            [K(20, 10, "Kaya Nakliyat", 100m), K(21, 12, "Kaya Ambalaj", 100m)]);
        Assert.Equal(21, Assert.Single(s.Eslesenler).Islem.Id);

        // Benzerlik eşitse yakın gün kazanır.
        var s2 = Erp12Karsilastirici.Karsilastir(
            [E(1, 10, "Kaya Ambalaj", 100m)],
            [K(20, 13, "Kaya Ambalaj", 100m), K(21, 9, "Kaya Ambalaj", 100m)]);
        Assert.Equal(21, Assert.Single(s2.Eslesenler).Islem.Id);
        Assert.Equal(-1, s2.Eslesenler[0].GunFarki);
    }

    [Fact]
    public void Bos_listeler()
    {
        var s = Erp12Karsilastirici.Karsilastir([], []);
        Assert.Empty(s.Eslesenler);
        Assert.Empty(s.YalnizErp12);
        Assert.Empty(s.YalnizKasa);

        var yalniz = Erp12Karsilastirici.Karsilastir([E(1, 1, "A Firması", 1m)], []);
        Assert.Null(Assert.Single(yalniz.YalnizErp12).Ipucu);
    }

    [Fact]
    public void Sonuclar_tarihe_gore_sirali()
    {
        var s = Erp12Karsilastirici.Karsilastir(
            [E(2, 20, "B", 2m), E(1, 10, "A", 1m)],
            [K(30, 25, "C", 3m), K(31, 5, "D", 4m)]);
        Assert.Equal([1, 2], s.YalnizErp12.Select(e => e.Kayit.SatirNo));
        Assert.Equal([31, 30], s.YalnizKasa.Select(k => k.Kayit.Id));
    }

    [Theory]
    [InlineData("ŞAHİN İNŞAAT", "sahin")]
    [InlineData("Çiğdem Özgür Ünal", "cigdem ozgur unal")]
    [InlineData("  I-IŞIK  ", "i isik")]
    [InlineData("A.Ş.", "a s")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normal_turkce_kucuk_harf_ve_sade(string? s, string beklenen)
    {
        var n = Erp12Karsilastirici.Normal(s);
        // "insaat" dolgu kelimesi değil Normal'de kalır; Benzerlik'te atılır.
        Assert.StartsWith(beklenen, n);
    }

    [Theory]
    [InlineData("Yılmaz Gıda Ltd. Şti.", "YILMAZ GIDA", 1.0)]
    [InlineData("Yılmaz Gıda", "yilmaz gida san tic a.s.", 1.0)]
    [InlineData("YILMAZGIDA", "Yılmaz Gıda", 1.0)]
    [InlineData("Migros", "Migros Ticaret A.Ş.", 1.0)]
    [InlineData("Migr", "Migros", 1.0)]          // en az 3 harflik baş
    [InlineData("Mi", "Migros", 0.0)]
    [InlineData("Kaya Ambalaj", "Kaya Nakliyat", 0.5)]
    [InlineData("Kaya Ambalaj", "Demir Nakliyat", 0.0)]
    [InlineData("İstanbul Elektrik", "ISTANBUL ELEKTRIK", 1.0)]
    [InlineData("", "Kaya", 0.0)]
    [InlineData("Ltd. Şti.", "LTD STI", 1.0)]
    public void Cari_benzerligi(string a, string b, double beklenen)
    {
        Assert.Equal(beklenen, Erp12Karsilastirici.Benzerlik(a, b), 3);
        Assert.Equal(beklenen, Erp12Karsilastirici.Benzerlik(b, a), 3);   // simetrik
    }
}
