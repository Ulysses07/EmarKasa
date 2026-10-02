using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Çek durumu, kalan, geçiş tablosu ve hareket kuralları (docs/specs/2026-10-01-cekler.md "Durum").</summary>
public class CekKurallariTests
{
    private static readonly DateOnly Gun = new(2026, 10, 1);
    private const string A = CekYonleri.Alinan;
    private const string V = CekYonleri.Verilen;

    private static CekHareketi H(int sira, string tur, decimal tutar = 0, decimal? net = null, string? karsi = null, int gun = 0)
        => new(sira, sira, tur, Gun.AddDays(gun), tutar, net, "MEZAT", karsi);

    [Fact]
    public void Hareketsiz_cek_portfoyde_kalan_tutarin_tamami()
    {
        var d = CekKurallari.Durum(A, 50_000m, []);
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m, 0m, true, false), (d.Durum, d.Kalan, d.Odenen, d.Acik, d.Kapali));
    }

    [Fact]
    public void Kismi_tahsil_kismen_tahsil_edildi_kalan_sifirlaninca_tahsil_edildi()
    {
        var kismi = CekKurallari.Durum(A, 50_000m, [H(1, CekHareketTurleri.Tahsilat, 20_000m)]);
        Assert.Equal((CekDurumlari.KismenTahsilEdildi, 30_000m, true), (kismi.Durum, kismi.Kalan, kismi.Acik));
        var tam = CekKurallari.Durum(A, 50_000m, [H(1, CekHareketTurleri.Tahsilat, 20_000m), H(2, CekHareketTurleri.Tahsilat, 30_000m)]);
        Assert.Equal((CekDurumlari.TahsilEdildi, 0m, true), (tam.Durum, tam.Kalan, tam.Kapali));
        var verilen = CekKurallari.Durum(V, 10m, [H(1, CekHareketTurleri.Odeme, 4m)]);
        Assert.Equal((CekDurumlari.KismenOdendi, 6m), (verilen.Durum, verilen.Kalan));
        Assert.Equal(CekDurumlari.Odendi, CekKurallari.Durum(V, 10m, [H(1, CekHareketTurleri.Odeme, 10m)]).Durum);
    }

    [Fact]
    public void Ciro_kirdirma_donus_karsiliksiz_ve_iade_durumlari()
    {
        var ciro = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Mehmet")]);
        Assert.Equal((CekDurumlari.CiroEdildi, 0m, true), (ciro.Durum, ciro.Kalan, ciro.Kapali));
        Assert.Equal(1, ciro.AcikDevir!.Id);
        var kirdirma = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Kirdirma, 100m, 95m, "Banka")]);
        Assert.Equal(CekDurumlari.Kirdirildi, kirdirma.Durum);
        var donus = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Mehmet"), H(2, CekHareketTurleri.Donus, 100m)]);
        Assert.Equal((CekDurumlari.Karsiliksiz, 100m, (CekHareketi?)null, false, false), (donus.Durum, donus.Kalan, donus.AcikDevir, donus.Acik, donus.Kapali));
        var gecTahsil = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz), H(2, CekHareketTurleri.Tahsilat, 40m)]);
        Assert.Equal((CekDurumlari.Karsiliksiz, 60m), (gecTahsil.Durum, gecTahsil.Kalan));
        Assert.Equal(CekDurumlari.TahsilEdildi, CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz), H(2, CekHareketTurleri.Tahsilat, 100m)]).Durum);
        var iade = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 10m), H(2, CekHareketTurleri.Iade)]);
        Assert.Equal((CekDurumlari.IadeEdildi, 0m, true), (iade.Durum, iade.Kalan, iade.Kapali));
    }

    [Fact]
    public void Gecis_tablosu_izinli_hareketleri_verir()
    {
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade],
            CekKurallari.IzinliHareketler(A, 100m, []));
        // Ciro ve kırdırma yalnız hiç tahsilat yokken.
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade],
            CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 10m)]));
        Assert.Equal([CekHareketTurleri.Odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(V, 100m, []));
        Assert.Equal([CekHareketTurleri.Donus], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "X")]));
        Assert.Equal([CekHareketTurleri.Donus], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Kirdirma, 100m, 90m, "B")]));
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz)]));
        Assert.Equal([CekHareketTurleri.Odeme, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(V, 100m, [H(1, CekHareketTurleri.Karsiliksiz)]));
        Assert.Empty(CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 100m)]));
        Assert.Empty(CekKurallari.IzinliHareketler(V, 100m, [H(1, CekHareketTurleri.Iade)]));
    }

    [Fact]
    public void Hareket_kurallari_tutar_tarih_ve_gecis_ihlalini_turkce_iletir()
    {
        string? Hata(string yon, IReadOnlyList<CekHareketi> once, CekHareketi yeni) => CekKurallari.HareketHatasi(yon, 100m, once, yeni);
        Assert.Null(Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 40m)));
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (100,00 TL) aşamaz.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 100.01m)));
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (60,00 TL) aşamaz.", Hata(A, [H(1, CekHareketTurleri.Tahsilat, 40m)], H(2, CekHareketTurleri.Tahsilat, 0m)));
        Assert.Equal("Bu kayıt kısmen tahsil edildi; şu an yalnız şu hareketler girilebilir: Tahsilat, Karşılıksız, İade.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 40m)], H(2, CekHareketTurleri.Ciro, 60m, karsi: "Ali")));
        Assert.Equal("Bu kayıt tahsil edildi; yeni hareket girilemez. Gerekirse son hareketi geri alın.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 100m)], H(2, CekHareketTurleri.Iade)));
        Assert.Equal("Ciroda tutar kalan tutarın tamamı (100,00 TL) olmalı.", Hata(A, [], H(1, CekHareketTurleri.Ciro, 50m, karsi: "Ali")));
        Assert.Equal("Ciroda ciro edilen kişiyi, kırdırmada banka ya da faktoring adını yazın.", Hata(A, [], H(1, CekHareketTurleri.Ciro, 100m)));
        Assert.Null(Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, 97.5m, "Faktoring A.Ş.")));
        Assert.Equal("Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.", Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, 100.01m, "Banka")));
        Assert.Equal("Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.", Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, null, "Banka")));
        Assert.Equal("Hesaba geçen tutar yalnız kırdırmada girilir.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 10m, 5m)));
        Assert.Equal("Dönüş tutarı ciro ya da kırdırma tutarına (100,00 TL) eşit olmalı.",
            Hata(A, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Ali")], H(2, CekHareketTurleri.Donus, 90m)));
        Assert.Null(Hata(A, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Ali")], H(2, CekHareketTurleri.Donus, 100m)));
        Assert.Equal("Karşılıksız ve iade hareketinde tutar girilmez.", Hata(V, [], H(1, CekHareketTurleri.Karsiliksiz, 5m)));
        Assert.Equal("Hareket tarihi önceki hareketin tarihinden (03.10.2026) önce olamaz.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 10m, gun: 2)], H(2, CekHareketTurleri.Tahsilat, 10m, gun: 1)));
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 1.001m)));
    }

    [Fact]
    public void Kasa_etkili_hareketler_ve_ayni_cek_karsilastirmasi()
    {
        Assert.All(new[] { CekHareketTurleri.Tahsilat, CekHareketTurleri.Odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Donus },
            t => Assert.True(CekKurallari.KasaEtkili(t)));
        Assert.False(CekKurallari.KasaEtkili(CekHareketTurleri.Karsiliksiz));
        Assert.False(CekKurallari.KasaEtkili(CekHareketTurleri.Iade));
        Assert.True(CekKurallari.AyniCek(A, " Ziraat ", "AB-12", A, "ziraat", "ab-12"));
        Assert.True(CekKurallari.AyniCek(A, null, "7", A, "", "7"));
        Assert.False(CekKurallari.AyniCek(A, "Ziraat", "12", V, "Ziraat", "12"));
        Assert.False(CekKurallari.AyniCek(A, "Ziraat", "12", A, "Halk", "12"));
    }

    [Fact]
    public void AyniCek_turkce_I_harflerini_katlar_ve_boslugu_kirpar()
    {
        Assert.True(CekKurallari.AyniCek(A, "ZIRAAT", "1", A, "ziraat", "1"));
        Assert.True(CekKurallari.AyniCek(A, "ZİRAAT", "1", A, "ziraat", "1"));
        Assert.True(CekKurallari.AyniCek(A, "Ziraat ", "1", A, "ZIRAAT", "1"));
        Assert.False(CekKurallari.AyniCek(A, "Garanti", "1", A, "Ziraat", "1"));
    }

    /// <summary>Arama anahtarı (CekServisi.Liste'nin arama süzgecinin kullandığı normalleştirme; AyniCek'teki katlamanın tek kaynağı):
    /// küçük harfe çevirir, baştaki/sondaki boşluğu kırpar, Türkçe I/ı/İ/i'yi tek harfe katlar.</summary>
    [Fact]
    public void AramaAnahtari_turkce_I_harflerini_katlar_bosluk_kirpar_kucuk_harfe_cevirir()
    {
        Assert.Equal("ziraat", CekKurallari.AramaAnahtari("ZIRAAT"));
        Assert.Equal("ziraat", CekKurallari.AramaAnahtari("ZİRAAT"));
        Assert.Equal("ziraat", CekKurallari.AramaAnahtari(" Ziraat "));
        Assert.Equal("ti-12", CekKurallari.AramaAnahtari("TI-12"));
        Assert.Equal("", CekKurallari.AramaAnahtari(null));
        Assert.Equal("", CekKurallari.AramaAnahtari("   "));
    }

    [Fact]
    public void Hareket_karsi_taraf_yalniz_ciro_ve_kirdirmada_girilir()
    {
        Assert.Equal("Karşı taraf yalnız ciro ve kırdırmada girilir.",
            CekKurallari.HareketHatasi(A, 100m, [], H(1, CekHareketTurleri.Tahsilat, 40m, karsi: "Ali")));
        Assert.Equal("Karşı taraf yalnız ciro ve kırdırmada girilir.",
            CekKurallari.HareketHatasi(V, 100m, [], H(1, CekHareketTurleri.Odeme, 40m, karsi: "Ali")));
        Assert.Equal("Karşı taraf yalnız ciro ve kırdırmada girilir.",
            CekKurallari.HareketHatasi(A, 100m, [], H(1, CekHareketTurleri.Karsiliksiz, 0m, karsi: "Ali")));
        Assert.Equal("Karşı taraf yalnız ciro ve kırdırmada girilir.",
            CekKurallari.HareketHatasi(A, 100m, [], H(1, CekHareketTurleri.Iade, 0m, karsi: "Ali")));
        Assert.Null(CekKurallari.HareketHatasi(A, 100m, [], H(1, CekHareketTurleri.Ciro, 100m, karsi: "Ali")));
        Assert.Null(CekKurallari.HareketHatasi(A, 100m, [], H(1, CekHareketTurleri.Kirdirma, 100m, 90m, "Ali")));
    }

    [Fact]
    public void Hareket_hatasi_tarih_tutar_ve_gecis_ek_senaryolari()
    {
        string? Hata(string yon, IReadOnlyList<CekHareketi> once, CekHareketi yeni) => CekKurallari.HareketHatasi(yon, 100m, once, yeni);
        // Önceki hareketle aynı günlü hareket kabul edilir.
        Assert.Null(Hata(A, [H(1, CekHareketTurleri.Tahsilat, 10m, gun: 0)], H(2, CekHareketTurleri.Tahsilat, 10m, gun: 0)));
        // Kalandan farklı tutarlı kırdırma reddedilir.
        Assert.Equal("Kırdırmada tutar çek tutarının tamamı (100,00 TL) olmalı.",
            Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 60m, 50m, "Banka")));
        // Eksi tutarlı tahsilat reddedilir.
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (100,00 TL) aşamaz.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, -10m)));
        // Tahsilattan sonra kırdırma reddedilir.
        Assert.Equal("Bu kayıt kısmen tahsil edildi; şu an yalnız şu hareketler girilebilir: Tahsilat, Karşılıksız, İade.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 40m)], H(2, CekHareketTurleri.Kirdirma, 60m, 55m, "Banka")));
        // Karşılıksızda dolu NetTutar reddedilir.
        Assert.Equal("Hesaba geçen tutar yalnız kırdırmada girilir.", Hata(A, [], H(1, CekHareketTurleri.Karsiliksiz, 0m, 10m)));
    }

    [Fact]
    public void Kismi_tahsilden_sonra_karsiliksiz_durumu_ve_kalani_dogru_hesaplar()
    {
        var d = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 40m), H(2, CekHareketTurleri.Karsiliksiz)]);
        Assert.Equal((CekDurumlari.Karsiliksiz, 60m), (d.Durum, d.Kalan));
    }
}
