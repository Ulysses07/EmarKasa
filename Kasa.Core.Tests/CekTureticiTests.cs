using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Çek hareketlerinden türetilen gelir/gider satırları ve hesap motorundaki etkileri (tasarım "Rapora etkisi"):
/// negatif Gelen ve negatif Cari gider motorda toplamlardan doğru düşer; Ortak kasalı ödeme aylık Ortak bölüşümüne girer.</summary>
public class CekTureticiTests
{
    private static readonly Kanal[] Kanallar = [new("MEZAT"), new("PERAKENDE")];
    private static readonly IReadOnlyList<Donem> Donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31));
    private static readonly CekBilgisi Alinan = new(7, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ahmet Yılmaz", 50_000m);
    private static readonly CekBilgisi Verilen = new(8, CekTurleri.Cek, CekYonleri.Verilen, "777", "Mehmet Ticaret", 30_000m);

    private static (IReadOnlyList<Gelen> G, IReadOnlyList<Islem> I) Turet(CekBilgisi cek, params CekHareketi[] h) => CekTuretici.Satirlar(cek, h, Donemler);

    private static decimal Kasa(IReadOnlyList<Gelen> g, IReadOnlyList<Islem> i) =>
        HesapMotoru.HaftalikHesapla(0m, Kanallar, i, g, Donemler)[^1].KasaDevir;

    [Fact]
    public void Tahsilat_tarihli_kanal_geliri_olur_anahtari_ve_aciklamasi_hareketi_gosterir()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(31, 1, CekHareketTurleri.Tahsilat, new(2026, 9, 17), 20_000m, Kanal: "MEZAT"));
        Assert.Empty(i);
        var gelen = Assert.Single(g);
        Assert.Equal((new DateOnly(2026, 9, 14), "MEZAT", 20_000m, false), (gelen.DonemStart, gelen.Kanal, gelen.TutarTl, gelen.GenelGelir));
        Assert.Equal((new DateOnly(2026, 9, 17), "Cek:31", "Çek tahsili: Ahmet Yılmaz / 12345"), (gelen.Tarih, gelen.KaynakAnahtari, gelen.Aciklama));
        Assert.Equal(20_000m, Kasa(g, i));
    }

    [Fact]
    public void Odeme_cari_gider_olur_ortak_kasada_aylik_ortak_paya_bolunur()
    {
        var (g, i) = Turet(Verilen, new CekHareketi(40, 1, CekHareketTurleri.Odeme, new(2026, 9, 10), 30_000.01m, Kanal: KanalEtiketleri.Ortak));
        Assert.Empty(g);
        var gider = Assert.Single(i);
        Assert.Equal(("Mehmet Ticaret", KanalEtiketleri.Ortak, GiderTipi.Cari, "Çek ödemesi / 777", "Cek:40", "Cek:40"),
            (gider.Cari, gider.Kanal, gider.Tip, gider.Not, gider.Kaynak, gider.KaynakAnahtari));
        var ay = HesapMotoru.AylikHesapla(2026, 9, Kanallar, i, g, Donemler);
        Assert.Equal(new[] { 15_000.01m, 15_000m }, ay.Kanallar.Select(k => k.OrtakPay));
        Assert.Equal(-30_000.01m, Kasa(g, i));
    }

    [Fact]
    public void Ciro_ayni_kasada_gelir_ve_gider_olur_kasa_ve_ay_sonucu_degismez()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(50, 1, CekHareketTurleri.Ciro, new(2026, 9, 21), 50_000m, Kanal: "PERAKENDE", Karsi: "Veli Toptan"));
        Assert.Equal(("PERAKENDE", 50_000m, "Cek:50", "Çek cirosu: Ahmet Yılmaz / 12345"), (g[0].Kanal, g[0].TutarTl, g[0].KaynakAnahtari, g[0].Aciklama));
        Assert.Equal(("Veli Toptan", "PERAKENDE", 50_000m, "Cek:50:gider", "Cek:50"), (i[0].Cari, i[0].Kanal, i[0].TutarTl, i[0].KaynakAnahtari, i[0].Kaynak));
        Assert.Equal(0m, Kasa(g, i));
        var ay = HesapMotoru.AylikHesapla(2026, 9, Kanallar, i, g, Donemler).Kanallar.Single(k => k.Kanal == "PERAKENDE");
        Assert.Equal((50_000m, 50_000m, 0m), (ay.Gelen, ay.CariGiden, ay.AySonucu));
    }

    [Fact]
    public void Kirdirma_cek_tutari_gelir_masraf_cari_gider_kasaya_net_girer()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(60, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 48_750m, "MEZAT", "Faktoring A.Ş."));
        Assert.Equal(50_000m, Assert.Single(g).TutarTl);
        var masraf = Assert.Single(i);
        Assert.Equal(("Faktoring A.Ş.", 1_250m, "Çek kırdırma masrafı / 12345", "Cek:60:gider"), (masraf.Cari, masraf.TutarTl, masraf.Not, masraf.KaynakAnahtari));
        Assert.Equal(48_750m, Kasa(g, i));
        // Masrafsız kırdırma gider üretmez.
        Assert.Empty(Turet(Alinan, new CekHareketi(61, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 50_000m, "MEZAT", "Banka")).I);
    }

    [Fact]
    public void Cirodan_donus_negatif_gelen_ve_negatif_cari_gider_uretir_motor_toplamlardan_duser()
    {
        var ciro = new CekHareketi(70, 1, CekHareketTurleri.Ciro, new(2026, 9, 21), 50_000m, Kanal: "MEZAT", Karsi: "Veli Toptan");
        var donus = new CekHareketi(71, 2, CekHareketTurleri.Donus, new(2026, 10, 5), 50_000m, Kanal: "MEZAT");
        var (g, i) = Turet(Alinan, ciro, donus);
        Assert.Equal(new[] { 50_000m, -50_000m }, g.Select(x => x.TutarTl));
        Assert.Equal(new[] { 50_000m, -50_000m }, i.Select(x => x.TutarTl));
        Assert.Equal(("Veli Toptan", "Cek:71:gider", "Çek dönüşü / 12345"), (i[1].Cari, i[1].KaynakAnahtari, i[1].Not));
        Assert.Equal("Çek dönüşü: Ahmet Yılmaz / 12345", g[1].Aciklama);
        Assert.Equal(0m, Kasa(g, i));
        // Ekim: Gelen ve Cari gider eksi, ay sonucu sıfır.
        var ekim = HesapMotoru.AylikHesapla(2026, 10, Kanallar, i, g, Donemler).Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal((-50_000m, -50_000m, 0m), (ekim.Gelen, ekim.CariGiden, ekim.AySonucu));
        var hafta = HesapMotoru.HaftalikHesapla(0m, Kanallar, i, g, Donemler).Single(h => h.Donem.Icerir(new(2026, 10, 5)));
        Assert.Equal((-50_000m, -50_000m, 0m), (hafta.ToplamGelen, hafta.ToplamGiden, hafta.KasaSonucu));
    }

    [Fact]
    public void Kirdirmadan_donus_yalniz_negatif_gelen_uretir_masraf_geri_alinmaz()
    {
        var (g, i) = Turet(Alinan,
            new CekHareketi(80, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 48_750m, "MEZAT", "Banka"),
            new CekHareketi(81, 2, CekHareketTurleri.Donus, new(2026, 10, 2), 50_000m, Kanal: "MEZAT"));
        Assert.Equal(new[] { 50_000m, -50_000m }, g.Select(x => x.TutarTl));
        Assert.Equal(1_250m, Assert.Single(i).TutarTl);
        Assert.Equal(-1_250m, Kasa(g, i));
        var dokum = HesapMotoru.KasaHareketleri(Kanallar, i, g, Donemler);
        Assert.Equal((-50_000m, -50_000m), (dokum.Single(h => h.KaynakAnahtari == "Cek:81").GenelKasaEtkisi, dokum.Single(h => h.KaynakAnahtari == "Cek:81").KanalEtkisi));
    }

    [Fact]
    public void Karsiliksiz_ve_iade_satir_uretmez_senet_ve_kasasiz_hareket_ayri_yazilir()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(90, 1, CekHareketTurleri.Karsiliksiz, new(2026, 9, 2), 0m), new CekHareketi(91, 2, CekHareketTurleri.Iade, new(2026, 9, 3), 0m));
        Assert.Empty(g);
        Assert.Empty(i);
        var senet = Alinan with { Tur = CekTurleri.Senet };
        Assert.Equal("Senet tahsili: Ahmet Yılmaz / 12345",
            Assert.Single(Turet(senet, new CekHareketi(92, 1, CekHareketTurleri.Tahsilat, new(2026, 9, 2), 5m, Kanal: "MEZAT")).G).Aciklama);
        var kasasiz = Turet(Alinan, new CekHareketi(93, 1, CekHareketTurleri.Ciro, new(2026, 9, 2), 50_000m, Karsi: "X"));
        Assert.Equal((KanalEtiketleri.GenelKasa, true), (kasasiz.G[0].Kanal, kasasiz.G[0].GenelGelir));
        Assert.Equal((KanalEtiketleri.DagilimBekliyor, true), (kasasiz.I[0].Kanal, kasasiz.I[0].DagilimBekliyor));
        // Dönem dışı (takip başlangıcından önce) gelir üretilmez.
        Assert.Empty(Turet(Alinan, new CekHareketi(94, 1, CekHareketTurleri.Tahsilat, new(2026, 8, 31), 5m, Kanal: "MEZAT")).G);
    }
}
