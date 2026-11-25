using Kasa.Core.Kodlar;

namespace Kasa.Api.Tests;

/// <summary>Banka belgesinde tutar ve yönün bakiyeden çözülmesi, anahtar kelimeler ve atlanan satırlar (docs/specs/2026-11-25-pdf-ekrani.md §3).</summary>
public class EkstreBakiyeTests
{
    private static EkstreOkumaSonucu Banka(params string[] satirlar) => EkstreMetinOkuyucu.Oku("Para Birimi: TL\n" + string.Join("\n", satirlar), "Banka", "Akbank");
    private static (decimal? Tutar, string Yon, string? Kaynak) Ozet(EkstreOkunanSatir r) => (r.Tutar, r.Yon, r.YonKaynagi);

    [Fact]
    public void Basliksiz_eskiden_yeniye_belgede_tutar_ve_yon_bakiyeden_okunur()
    {
        var rows = Banka(
            "01.09.2026 Hareket bir 100,00 10.000,00",
            "02.09.2026 Hareket iki 1.500,00 8.500,00",
            "03.09.2026 Hareket uc 250,50 8.750,50").Satirlar;
        // İlk satırın komşusu yok: bakiyesi ikinci satırla doğrulandığı için tutarı okunur, yönü tahmin edilmez.
        Assert.Equal((100m, "Belirsiz", null), Ozet(rows[0]));
        Assert.Equal((1500m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Equal("Gider", rows[1].OnerilenIslem);
        Assert.Equal((250.50m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[2]));
        Assert.Equal("Gelir", rows[2].OnerilenIslem);
        Assert.DoesNotContain(rows[1].Uyarilar, w => w.Contains("Birden fazla tutar"));
    }

    [Fact]
    public void Yeniden_eskiye_siralanan_belgede_yon_alt_satirin_bakiyesinden_okunur()
    {
        var rows = Banka(
            "03.09.2026 Hareket uc 250,50 8.750,50",
            "02.09.2026 Hareket iki 1.500,00 8.500,00",
            "01.09.2026 Hareket bir 100,00 10.000,00").Satirlar;
        Assert.Equal((250.50m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[0]));
        Assert.Equal((1500m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Equal((100m, "Belirsiz", null), Ozet(rows[2]));
    }

    [Theory]
    [InlineData("DEVİR BAKİYESİ 9.900,00")]
    [InlineData("Önceki Bakiye: 9.900,00 TL")]
    [InlineData("01.09.2026 Devir Bakiyesi 9.900,00")]
    public void Devir_satiri_ilk_hareketin_komsusudur(string devir)
    {
        var rows = Banka(devir, "01.09.2026 Hareket bir 100,00 10.000,00", "02.09.2026 Hareket iki 1.500,00 8.500,00").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.Equal((100m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[0]));
        Assert.Equal((1500m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
    }

    [Fact]
    public void Aciklamasi_alt_satira_kayan_hareket_bakiyeden_cozulur()
    {
        var rows = Banka(
            "01.09.2026 Hareket bir 100,00 10.000,00",
            "02.09.2026 UZUN ACIKLAMALI",
            "FIRMA X 1.500,00 8.500,00",
            "03.09.2026 Hareket uc 250,50 8.750,50").Satirlar;
        Assert.Equal(3, rows.Count);
        Assert.Equal((1500m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
    }

    [Fact]
    public void Eksi_bakiye_ve_borc_alacak_ekli_bakiye_isaretiyle_karsilastirilir()
    {
        var rows = Banka(
            "01.09.2026 Hareket bir 100,00 500,00",
            "02.09.2026 Hareket iki 700,00 -200,00",
            "03.09.2026 Hareket uc 50,00 250,00 B",
            "04.09.2026 Hareket dort 300,00 50,00 A").Satirlar;
        Assert.Equal((700m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Equal((50m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[2]));
        Assert.Equal((300m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[3]));
    }

    [Fact]
    public void Uc_tutarli_satirda_yalniz_farka_esit_tutar_alinir()
    {
        var rows = Banka(
            "01.09.2026 Hareket bir 100,00 10.000,00",
            "02.09.2026 POS 1.234,56 brut 34,56 1.200,00 11.200,00",
            "03.09.2026 Hareket uc 200,00 11.000,00",
            "04.09.2026 Hareket dort 5,00 10,00 11.015,00").Satirlar;
        Assert.Equal((1200m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Equal((200m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[2]));
        // Fark 15,00: satırda böyle bir tutar yok; toplamı uydurulmaz.
        Assert.Null(rows[3].Tutar);
        Assert.Equal("Belirsiz", rows[3].Yon);
    }

    [Fact]
    public void Yazmayan_tutar_bakiye_farkindan_uydurulmaz()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        static string Satir(string tarih, string ad, string tutar, string bakiye) => $"{tarih,-14}{ad,-27}{tutar,12}{bakiye,16}";
        var rows = Banka(header, Satir("01.09.2026", "Hareket", "100,00", "1.000,00"), Satir("02.09.2026", "Hareket", "50,00", "1.050,00"),
            Satir("03.09.2026", "Tutarsiz", "", "900,00")).Satirlar;
        Assert.Equal((50m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Null(rows[2].Tutar);
        Assert.Equal("Belirsiz", rows[2].Yon);
    }

    [Fact]
    public void Tek_bag_basliksiz_belgede_yeterli_kanit_degildir()
    {
        var rows = Banka("01.09.2026 Hareket bir 100,00 120,00", "02.09.2026 Hareket iki 20,00 140,00").Satirlar;
        Assert.All(rows, r => Assert.Null(r.Tutar));
        Assert.All(rows, r => Assert.Equal("Belirsiz", r.Yon));
    }

    [Fact]
    public void Baglarin_yarisi_tutmuyorsa_bakiye_kullanilmaz()
    {
        var rows = Banka(
            "01.09.2026 Hareket 100,00 120,00", "02.09.2026 Hareket 20,00 140,00", "03.09.2026 Hareket 10,00 150,00",
            "04.09.2026 Hareket 75,00 90,00", "05.09.2026 Hareket 75,00 91,00", "06.09.2026 Hareket 75,00 93,00",
            "07.09.2026 Hareket 75,00 96,00").Satirlar;
        Assert.All(rows, r => Assert.Null(r.Tutar));
    }

    [Fact]
    public void Esit_tutarli_iki_yonlu_belirsizlikte_sira_tarihten_bulunur()
    {
        var artan = Banka("01.09.2026 Hareket 100,00 900,00", "02.09.2026 Hareket 100,00 1.000,00", "03.09.2026 Hareket 100,00 1.100,00").Satirlar;
        Assert.Equal((100m, "Giris", YonKaynaklari.Bakiye), Ozet(artan[1]));
        Assert.Equal("Belirsiz", artan[0].Yon);
        var azalan = Banka("03.09.2026 Hareket 100,00 1.100,00", "02.09.2026 Hareket 100,00 1.000,00", "01.09.2026 Hareket 100,00 900,00").Satirlar;
        Assert.Equal((100m, "Giris", YonKaynaklari.Bakiye), Ozet(azalan[0]));
        Assert.Equal("Belirsiz", azalan[2].Yon);
        // Tarihler de aynıysa sıra bilinmez: hiçbir satır çözülmez.
        var ayni = Banka("01.09.2026 Hareket 100,00 900,00", "01.09.2026 Hareket 100,00 1.000,00", "01.09.2026 Hareket 100,00 1.100,00").Satirlar;
        Assert.All(ayni, r => Assert.Equal("Belirsiz", r.Yon));
    }

    [Fact]
    public void Okunan_yon_bakiyeyle_celisirse_belirsiz_kalir_ve_uyarir()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-24}{"Borç",12}{"Alacak",12}{"Bakiye",14}";
        static string Satir(string tarih, string borc, string alacak, string bakiye) => $"{tarih,-14}{"Hareket",-24}{borc,12}{alacak,12}{bakiye,14}";
        var rows = Banka(header, Satir("01.09.2026", "", "100,00", "1.000,00"), Satir("02.09.2026", "25,00", "", "975,00"),
            Satir("03.09.2026", "50,00", "", "1.025,00"), Satir("04.09.2026", "", "75,00", "1.100,00")).Satirlar;
        Assert.Equal((25m, "Cikis", YonKaynaklari.Sutun), Ozet(rows[1]));
        Assert.Equal((50m, "Belirsiz", null), Ozet(rows[2]));
        Assert.Equal("Atla", rows[2].OnerilenIslem);
        Assert.Contains(rows[2].Uyarilar, w => w.Contains("bakiye") && w.Contains("çelişiyor"));
        Assert.Equal((75m, "Giris", YonKaynaklari.Sutun), Ozet(rows[3]));

        var isaretli = Banka("01.09.2026 Hareket -100,00 1.000,00", "02.09.2026 Hareket -25,00 975,00", "03.09.2026 Hareket -50,00 1.025,00", "04.09.2026 Hareket +75,00 1.100,00").Satirlar;
        Assert.Equal((25m, "Cikis", YonKaynaklari.Isaret), Ozet(isaretli[1]));
        Assert.Equal((50m, "Belirsiz", null), Ozet(isaretli[2]));
        Assert.Equal((75m, "Giris", YonKaynaklari.Isaret), Ozet(isaretli[3]));
    }

    [Fact]
    public void Kopan_zincir_belge_uyarisinda_satir_numarasiyla_soylenir()
    {
        var read = Banka(
            "01.09.2026 Hareket 100,00 10.000,00", "02.09.2026 Hareket 1.500,00 8.500,00", "03.09.2026 Hareket 250,00 8.750,00",
            "05.09.2026 Hareket 300,00 7.000,00", "06.09.2026 Hareket 50,00 7.050,00");
        // Dördüncü satırın komşusuyla bağı kopuk; bakiyesi beşinci satırla doğrulandığı için tutarı okunur, yönü belirsizdir.
        Assert.Equal((300m, "Belirsiz", null), Ozet(read.Satirlar[3]));
        Assert.Equal((50m, "Giris", YonKaynaklari.Bakiye), Ozet(read.Satirlar[4]));
        var uyari = Assert.Single(read.Uyarilar, w => w.Contains("okunamayan"));
        Assert.Contains("satır 4", uyari);
        Assert.DoesNotContain("3", uyari);
    }

    [Fact]
    public void Aciklama_bakiyeyle_celisirse_bakiye_kazanir_ve_uyarir()
    {
        var rows = Banka("01.09.2026 Hareket 100,00 10.000,00", "02.09.2026 GELEN EFT geri cekildi 1.500,00 8.500,00", "03.09.2026 GELEN EFT 250,00 8.750,00").Satirlar;
        Assert.Equal((1500m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
        Assert.Contains(rows[1].Uyarilar, w => w.Contains("Açıklama giriş"));
        Assert.Equal((250m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[2]));
        Assert.DoesNotContain(rows[2].Uyarilar, w => w.Contains("Açıklama giriş"));
    }

    [Fact]
    public void Kart_ekstresinde_bakiye_zinciri_uygulanmaz()
    {
        var rows = EkstreMetinOkuyucu.Oku("Para Birimi: TL\n01.09.2026 Hareket 100,00 10.000,00\n02.09.2026 Hareket 1.500,00 8.500,00\n03.09.2026 Hareket 250,50 8.750,50", "Kart", "Akbank").Satirlar;
        Assert.All(rows, r => Assert.Null(r.Tutar));
        Assert.DoesNotContain(rows, r => r.YonKaynagi == YonKaynaklari.Bakiye);
    }

    [Theory]
    [InlineData("GELEN EFT AHMET", "Giris")]
    [InlineData("Gelen FAST", "Giris")]
    [InlineData("ATM PARA YATIRMA", "Giris")]
    [InlineData("NAKİT YATIRMA", "Giris")]
    [InlineData("POS SATIŞ TUTARI", "Giris")]
    [InlineData("POS BLOKE ÇÖZÜM", "Giris")]
    [InlineData("ÜYE İŞYERİ ALACAĞI", "Giris")]
    [InlineData("MEVDUAT FAİZİ", "Giris")]
    [InlineData("GİDEN HAVALE", "Cikis")]
    [InlineData("ATM PARA ÇEKME", "Cikis")]
    [InlineData("ATM NAKİT ÇEKİM", "Cikis")]
    [InlineData("POS HARCAMASI MIGROS", "Cikis")]
    [InlineData("POS ALIŞVERİŞ", "Cikis")]
    [InlineData("ELEKTRİK FATURA ÖDEMESİ", "Cikis")]
    [InlineData("OTOMATİK ÖDEME TURKCELL", "Cikis")]
    [InlineData("KREDİ KARTI ÖDEMESİ", "Cikis")]
    [InlineData("KREDİ TAKSİT TAHSİLATI", "Cikis")]
    [InlineData("ÇEK ÖDEMESİ 123", "Cikis")]
    [InlineData("HAVALE KOMİSYONU", "Cikis")]
    public void Anahtar_kelime_baska_kanit_yokken_yonu_verir(string aciklama, string yon)
    {
        var row = Assert.Single(Banka($"01.09.2026 {aciklama}  100,00").Satirlar);
        Assert.Equal((100m, yon, YonKaynaklari.Sozcuk), Ozet(row));
    }

    [Theory]
    [InlineData("MAAŞ ÖDEMESİ")]
    [InlineData("ÇEK TAHSİLATI")]
    [InlineData("POS")]
    [InlineData("FAİZ")]
    [InlineData("GELEN EFT İADESİ")]
    [InlineData("OTOMATİK ÖDEME İPTALİ")]
    [InlineData("GELEN EFT / GİDEN EFT DÜZELTME")]
    [InlineData("REPOS HARCAMASIZ")]
    [InlineData("FATURA ÖDEMESİ ABC LTD")]
    [InlineData("GELEN EFT KOMİSYONU")]
    public void Iki_anlamli_aciklama_belirsiz_kalir(string aciklama)
    {
        var row = Assert.Single(Banka($"01.09.2026 {aciklama}  100,00").Satirlar);
        Assert.Equal((100m, "Belirsiz", null), Ozet(row));
        Assert.Equal("Atla", row.OnerilenIslem);
    }

    [Fact]
    public void Isaret_anahtar_kelimeden_once_gelir()
    {
        var row = Assert.Single(Banka("01.09.2026 GİDEN EFT GERİ GELEN  +100,00").Satirlar);
        Assert.Equal(("Giris", YonKaynaklari.Isaret), (row.Yon, row.YonKaynagi));
    }

    [Fact]
    public void Harekete_alinmayan_tutarli_ya_da_tarihli_satirlar_nedeniyle_saklanir()
    {
        var read = Banka(
            "Sayın müşterimiz",
            "Kullanılabilir Limit 5.000,00",
            "01.09.2026 Hareket bir 100,00",
            "Bloke tutarı 250,00",
            "TOPLAM 100,00");
        Assert.Single(read.Satirlar);
        var atlanan = read.Atlananlar;
        Assert.Contains(atlanan, a => a.Metin.StartsWith("Kullanılabilir Limit", StringComparison.Ordinal) && a.Neden.StartsWith("Toplam", StringComparison.Ordinal));
        Assert.Contains(atlanan, a => a.Metin.StartsWith("Bloke tutarı", StringComparison.Ordinal) && a.Neden == "Satırın başında tarih yok" && a.Sayfa == 1);
        Assert.Contains(atlanan, a => a.Metin.StartsWith("TOPLAM", StringComparison.Ordinal));
        Assert.Equal(3, atlanan.Count);
    }

    [Fact]
    public void Atlanan_satir_listesi_sinirlidir_ve_metni_kisaltilir()
    {
        var read = Banka(Enumerable.Range(1, 400).Select(_ => "Bloke " + new string('x', 400) + " 1,00").ToArray());
        Assert.Equal(300, read.Atlananlar.Count);
        Assert.All(read.Atlananlar, a => Assert.True(a.Metin.Length <= 300));
        Assert.Contains(read.Uyarilar, w => w.Contains("400 satır"));
    }
}
