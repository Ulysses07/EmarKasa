using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>2026-09 hesap denetimi (ikinci tur) bulgularının kilit testleri — mevcut API ile.</summary>
public class HesapBulguTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };

    private static decimal OrtakPay(AylikRapor r, string kanal) => r.Kanallar.Single(k => k.Kanal == kanal).OrtakPay;

    // --- Takip başlangıcı öncesi işlem (bulgu 5) ---

    [Fact]
    public void Aylik_takip_oncesi_islemi_gelen_gibi_saymaz_ve_haftalikla_tutar()
    {
        var takip = new DateOnly(2026, 9, 15);
        var donemler = DonemUretici.Uret(takip, new DateOnly(2026, 9, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 9, 3), "Eski", 700m, "MEZAT", GiderTipi.Cari),    // takip öncesi
            new Islem(new DateOnly(2026, 9, 16), "Yeni", 100m, "MEZAT", GiderTipi.Cari),
        };
        var gelenler = new[]
        {
            new Gelen(new DateOnly(2026, 9, 1), "MEZAT", 1_000m),                          // takip öncesi
            new Gelen(new DateOnly(2026, 9, 15), "MEZAT", 400m),
        };

        var ay = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler);
        var mezat = ay.Kanallar.Single(k => k.Kanal == "MEZAT");
        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);

        Assert.Equal(400m, mezat.Gelen);
        Assert.Equal(100m, mezat.CariGiden);
        Assert.Equal(300m, mezat.AySonucu);
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu), ay.Kanallar.Sum(k => k.AySonucu));
    }

    [Fact]
    public void Takipten_once_biten_ay_bos_rapor_verir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 7, 10), "KK", 50m, "MEZAT", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 8, 10), "Cari", 70m, "MEZAT", GiderTipi.Cari),
        };
        var agustos = HesapMotoru.AylikHesapla(2026, 8, UcKanal, islemler, [], donemler);
        Assert.All(agustos.Kanallar, k => Assert.Equal(0m, k.AySonucu));
    }

    // --- Ortak dağıtımı (bulgu 6) ---

    [Fact]
    public void Kapanmis_ayin_ortak_dagitimi_aktif_bayragi_degisince_degismez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var gelenler = UcKanal.Select(k => new Gelen(donemler[0].Start, k.Ad, 500m)).ToArray();
        var islemler = new[] { new Islem(new DateOnly(2026, 8, 5), "Kira", 900m, Kanallar.Ortak, GiderTipi.SabitGider) };

        var once = HesapMotoru.AylikHesapla(2026, 8, UcKanal, islemler, gelenler, donemler);
        var kanallarSonra = new[] { new Kanal("MEZAT"), new Kanal("PERAKENDE"), new Kanal("TOPTAN", Aktif: false) };
        var sonra = HesapMotoru.AylikHesapla(2026, 8, kanallarSonra, islemler, gelenler, donemler);

        foreach (var k in UcKanal)
        {
            Assert.Equal(300m, OrtakPay(once, k.Ad));
            Assert.Equal(300m, OrtakPay(sonra, k.Ad));
        }
    }

    [Fact]
    public void Ortak_o_ay_hareketi_olan_kanallara_bolunur()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var gelenler = new[] { new Gelen(donemler[0].Start, "MEZAT", 500m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 8, 7), "Tedarik", 50m, "PERAKENDE", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 8, 5), "Kira", 900m, Kanallar.Ortak, GiderTipi.SabitGider),
        };
        var r = HesapMotoru.AylikHesapla(2026, 8, UcKanal, islemler, gelenler, donemler);

        Assert.Equal(450m, OrtakPay(r, "MEZAT"));
        Assert.Equal(450m, OrtakPay(r, "PERAKENDE"));
        Assert.Equal(0m, OrtakPay(r, "TOPTAN"));
    }

    [Fact]
    public void Aktif_kanal_kalmasa_da_ortak_gider_kaybolmaz_ve_haftalikla_tutar()
    {
        var kanallar = new[] { new Kanal("MEZAT", Aktif: false), new Kanal("PERAKENDE", Aktif: false) };
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var islemler = new[] { new Islem(new DateOnly(2026, 9, 5), "Kira", 1_000m, Kanallar.Ortak, GiderTipi.SabitGider) };

        var ay = HesapMotoru.AylikHesapla(2026, 9, kanallar, islemler, [], donemler);
        var haftalik = HesapMotoru.HaftalikHesapla(0m, kanallar, islemler, [], donemler);

        Assert.Equal(1_000m, ay.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(-1_000m, ay.Kanallar.Sum(k => k.AySonucu));
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu), ay.Kanallar.Sum(k => k.AySonucu));
    }

    // --- Kuruş aritmetiği (bulgu 9, 10, 11) ---

    [Fact]
    public void Cok_buyuk_ortak_tutar_tasma_hatasi_vermez_ve_mutabik_kalir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var tutar = 100_000_000_000_000_000.01m;   // 1e17 — eskiden (long) dönüşümü taşıyordu
        var islemler = new[] { new Islem(new DateOnly(2026, 9, 5), "Dev", tutar, Kanallar.Ortak, GiderTipi.SabitGider) };

        var ay = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, [], donemler);

        Assert.Equal(tutar, ay.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(33_333_333_333_333_333.34m, OrtakPay(ay, "MEZAT"));
        Assert.Equal(33_333_333_333_333_333.34m, OrtakPay(ay, "PERAKENDE"));
        Assert.Equal(33_333_333_333_333_333.33m, OrtakPay(ay, "TOPTAN"));
    }

    [Fact]
    public void Negatif_ortak_toplam_kurus_kaybetmeden_bolunur()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var islemler = new[] { new Islem(new DateOnly(2026, 9, 5), "Düzeltme", -0.07m, Kanallar.Ortak, GiderTipi.SabitGider) };

        var ay = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, [], donemler);

        Assert.Equal(-0.07m, ay.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(-0.03m, OrtakPay(ay, "MEZAT"));
        Assert.Equal(-0.02m, OrtakPay(ay, "PERAKENDE"));
        Assert.Equal(-0.02m, OrtakPay(ay, "TOPTAN"));
    }

    [Fact]
    public void Uc_ondalikli_tutar_haftalik_ve_aylikta_ayni_kurusa_yuvarlanir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 9, 5), "Ortak", 100.005m, Kanallar.Ortak, GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 9, 6), "Cari", 10.004m, "MEZAT", GiderTipi.Cari),
        };
        var gelenler = new[] { new Gelen(donemler[0].Start, "MEZAT", 0.005m) };

        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);
        var ay = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler);

        Assert.Equal(110.01m, haftalik.Sum(h => h.ToplamGiden));   // 100,01 + 10,00
        Assert.Equal(0.01m, haftalik.Sum(h => h.ToplamGelen));
        Assert.Equal(100.01m, ay.Kanallar.Sum(k => k.OrtakPay));
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu), ay.Kanallar.Sum(k => k.AySonucu));
    }

    // --- Karta bağlı ama Tip=Cari işlem (bulgu 14) ---

    [Fact]
    public void Karta_bagli_cari_tipli_islem_kasadan_iki_kez_dusmez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var islemler = new[] { new Islem(new DateOnly(2026, 6, 10), "Market", 100m, "MEZAT", GiderTipi.Cari, KrediKartiId: 1) };
        var odemeler = new[] { new KartOdeme(new DateOnly(2026, 7, 8), 100m) };

        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, [], donemler, odemeler);

        Assert.Equal(100m, ozet.Sum(o => o.ToplamGiden));
        Assert.Equal(0m, ozet.Sum(o => o.Kanallar.Single(k => k.Kanal == "MEZAT").Giden));
        // Aylıkta karta bağlı harcama gibi bir sonraki aya K.K olarak yazılır.
        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, islemler, [], donemler);
        var haziran = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, [], donemler);
        Assert.Equal(100m, temmuz.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
        Assert.Equal(0m, haziran.Kanallar.Single(k => k.Kanal == "MEZAT").CariGiden);
    }
}
