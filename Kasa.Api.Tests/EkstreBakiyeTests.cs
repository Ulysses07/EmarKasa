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
    public void Esit_tutarli_satirlarin_yonu_yalniz_sirasi_kanitli_komsuya_dayaniyorsa_okunur()
    {
        // Bütün tutarlar eşit: artan da azalan da okunur, çözülmez.
        var esit = Banka("01.09.2026 Hareket 100,00 900,00", "02.09.2026 Hareket 100,00 1.000,00", "03.09.2026 Hareket 100,00 1.100,00").Satirlar;
        Assert.All(esit, r => Assert.Equal("Belirsiz", r.Yon));
        // Devir satırı sırayı kanıtlar: zincir oradan başlar.
        var devirli = Banka("DEVİR BAKİYESİ 800,00", "01.09.2026 Hareket 100,00 900,00", "02.09.2026 Hareket 100,00 1.000,00", "03.09.2026 Hareket 100,00 1.100,00").Satirlar;
        Assert.All(devirli, r => Assert.Equal((100m, "Giris", YonKaynaklari.Bakiye), Ozet(r)));
        // Günler yeniden eskiye, 2 Eylül'ün iki eşit çekimi gün içinde eskiden yeniye: o çiftin yönü tersine okunmaz.
        var karisik = Banka(
            "04.09.2026 Hareket 50,00 7.430,00", "03.09.2026 Hareket 300,00 7.480,00",
            "02.09.2026 ATM cekim 1.000,00 8.780,00", "02.09.2026 ATM cekim 1.000,00 7.780,00",
            "01.09.2026 Hareket 220,00 9.780,00", "31.08.2026 Hareket 400,00 10.000,00", "30.08.2026 Hareket 100,00 10.400,00", "29.08.2026 Hareket 60,00 10.500,00").Satirlar;
        Assert.Equal((50m, "Cikis", YonKaynaklari.Bakiye), Ozet(karisik[0]));
        Assert.Equal((300m, "Belirsiz", null), Ozet(karisik[1]));
        Assert.DoesNotContain(karisik.Skip(2).Take(2), r => r.Yon == "Giris");
        Assert.Equal((220m, "Cikis", YonKaynaklari.Bakiye), Ozet(karisik[4]));
        Assert.Equal((400m, "Cikis", YonKaynaklari.Bakiye), Ozet(karisik[5]));
    }

    [Theory]
    [InlineData("-₺500,00", "-₺300,00", "-₺400,00", "-₺550,00")]
    [InlineData("- 500,00", "- 300,00", "- 400,00", "- 550,00")]
    [InlineData("−500,00", "−300,00", "−400,00", "−550,00")]
    [InlineData("(500,00)", "(300,00)", "(400,00)", "(550,00)")]
    [InlineData("500,00 (B)", "300,00 (B)", "400,00 (B)", "550,00 (B)")]
    [InlineData("500,00 B", "300,00 B", "400,00 B", "550,00 B")]
    public void Isareti_okunamayan_ya_da_iki_anlamli_bakiyeden_ters_yon_okunmaz(string b1, string b2, string b3, string b4)
    {
        // Gerçek: eksi bakiyeli hesapta +200, −100, −150.
        var rows = Banka($"01.09.2026 Hareket 100,00 {b1}", $"02.09.2026 Hareket 200,00 {b2}", $"03.09.2026 Hareket 100,00 {b3}", $"04.09.2026 Hareket 150,00 {b4}").Satirlar;
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, r => r.YonKaynagi == YonKaynaklari.Bakiye);
    }

    [Fact]
    public void Islem_yonu_kolonundaki_B_bakiyenin_eksisi_sayilmaz()
    {
        // "Tutar Bakiye B/A": hepsi B ise iki anlam da tutar, çözülmez; karışıksa yalnız ek yok sayılınca tutar.
        var hepsiB = Banka("01.09.2026 Hareket 1.000,00 9.000,00 B", "02.09.2026 Hareket 500,00 8.500,00 B", "03.09.2026 Hareket 250,00 8.250,00 B").Satirlar;
        Assert.DoesNotContain(hepsiB, r => r.Yon == "Giris");
        var karisik = Banka("01.09.2026 Hareket 1.000,00 9.000,00 B", "02.09.2026 Hareket 500,00 9.500,00 A", "03.09.2026 Hareket 250,00 9.250,00 B", "04.09.2026 Hareket 70,00 9.320,00 A").Satirlar;
        Assert.Equal((500m, "Giris", YonKaynaklari.Bakiye), Ozet(karisik[1]));
        Assert.Equal((250m, "Cikis", YonKaynaklari.Bakiye), Ozet(karisik[2]));
        Assert.Equal((70m, "Giris", YonKaynaklari.Bakiye), Ozet(karisik[3]));
        // Açıklamanın B ile başlayan sözcüğü ek değildir.
        foreach (var ek in new[] { "B BLOK GIDER", "A 101 MAGAZA", "B-12 DEPO", "B ABC LTD" })
        {
            var blok = Banka($"01.09.2026 100,00 9.000,00 {ek}", $"02.09.2026 500,00 8.500,00 {ek}", $"03.09.2026 250,00 8.250,00 {ek}").Satirlar;
            Assert.DoesNotContain(blok, r => r.YonKaynagi == YonKaynaklari.Bakiye);
            var tek = Assert.Single(Banka($"01.09.2026 100,00 {ek}").Satirlar);
            Assert.Equal((100m, "Belirsiz", null), Ozet(tek));
        }
        // Eksi bakiyeli hesapta bakiyeden sonra B ve açıklama: gerçek +200, −100, +50; ters okunmaz.
        var kmh = Banka("01.09.2026 100,00 500,00 B ABC LTD", "02.09.2026 200,00 300,00 B XYZ AS", "03.09.2026 100,00 400,00 B DEF LTD", "04.09.2026 50,00 350,00 B GHI").Satirlar;
        Assert.DoesNotContain(kmh, r => r.YonKaynagi == YonKaynaklari.Bakiye);
    }

    [Theory]
    [InlineData("-₺ {0}")]
    [InlineData("- ₺ {0}")]
    [InlineData("-TL {0}")]
    [InlineData("{0} TL-")]
    [InlineData("{0} TL (B)")]
    [InlineData("{0} TL B")]
    [InlineData("{0} BORÇ")]
    [InlineData("‑{0}")]
    [InlineData("‐{0}")]
    [InlineData("­{0}")]
    [InlineData("- {0}")]
    public void Eksi_bakiyenin_diger_yazimlari_da_ters_yon_uretmez(string bicim)
    {
        string B(string tutar) => string.Format(System.Globalization.CultureInfo.InvariantCulture, bicim, tutar);
        var rows = Banka($"01.09.2026 Hareket 100,00 {B("500,00")}", $"02.09.2026 Hareket 200,00 {B("300,00")}", $"03.09.2026 Hareket 100,00 {B("400,00")}", $"04.09.2026 Hareket 150,00 {B("550,00")}").Satirlar;
        Assert.DoesNotContain(rows, r => r.YonKaynagi == YonKaynaklari.Bakiye);
    }

    [Fact]
    public void Tek_yaniltici_sozcuk_dogru_zinciri_dusurmez_yanlis_yon_yazmaz()
    {
        foreach (var aciklama in new[] { "HAVALE AYŞE KARGO ÜCRETİ", "POS BLOKE ÇÖZÜM KOMİSYON SONRASI", "ATM PARA YATIRMA SİTE AİDATI" })
        {
            var rows = Banka("01.09.2026 Hareket 100,00 10.000,00", "02.09.2026 Hareket 300,00 9.700,00", $"03.09.2026 {aciklama} 980,00 10.680,00",
                "04.09.2026 Hareket 80,00 10.600,00", "05.09.2026 Hareket 25,00 10.625,00").Satirlar;
            Assert.Equal((980m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[2]));
            Assert.Equal((80m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[3]));
        }
    }

    [Fact]
    public void Birakilan_zincirle_celisen_sozcuk_yon_vermez()
    {
        // İşareti yazılmayan borç bakiye: zincir bırakılır; onunla çelişen açıklama da tek başına yön yazmaz.
        var read = Banka("01.09.2026 Hareket 100,00 1.000,00", "02.09.2026 GIDEN EFT 200,00 1.200,00", "03.09.2026 GELEN EFT 500,00 700,00", "04.09.2026 Hareket 50,00 750,00");
        Assert.All(read.Satirlar, r => Assert.Equal("Belirsiz", r.Yon));
    }

    [Fact]
    public void Ayni_gun_karisik_basilan_satirlarda_rastlantiyla_tutan_bag_yon_vermez()
    {
        // Gerçek +1.000, +1.000, −1.000; basım sırası −, +, +.
        var uc = Banka("01.09.2026 Hareket 300,00 9.700,00", "02.09.2026 Odeme 1.000,00 10.700,00", "02.09.2026 Tahsilat 1.000,00 10.700,00", "02.09.2026 Tahsilat 1.000,00 11.700,00",
            "03.09.2026 Hareket 50,00 10.650,00", "04.09.2026 Hareket 20,00 10.630,00", "05.09.2026 Hareket 30,00 10.600,00").Satirlar;
        Assert.DoesNotContain(uc.Skip(1).Take(3), r => r.YonKaynagi == YonKaynaklari.Bakiye);
        // Gerçek +2.000, −1.000; ters basım.
        var iki = Banka("01.09.2026 Hareket 300,00 10.000,00", "02.09.2026 Odeme 1.000,00 11.000,00", "02.09.2026 Tahsilat 2.000,00 12.000,00",
            "04.09.2026 Hareket 20,00 11.980,00", "05.09.2026 Hareket 30,00 11.950,00", "06.09.2026 Hareket 10,00 11.960,00").Satirlar;
        Assert.DoesNotContain(iki.Skip(1).Take(2), r => r.YonKaynagi == YonKaynaklari.Bakiye);
        Assert.Equal((30m, "Cikis", YonKaynaklari.Bakiye), Ozet(iki[4]));
    }

    [Fact]
    public void Ozet_ve_bolum_satirlari_zinciri_koparir_aciklamasinda_devir_gecen_hareket_kalir()
    {
        var hareket = Banka("01.09.2026 Hareket 100,00 10.100,00", "02.09.2026 Hareket 300,00 9.800,00", "02.09.2026 GELEN HAVALE AHMET DEVİR BAKİYESİ ÖDEMESİ 2.000,00 11.800,00",
            "03.09.2026 DEF 1.000,00 10.800,00", "04.09.2026 GELEN EFT ALİ ÖNCEKİ DÖNEM BORCU 50,00 10.850,00").Satirlar;
        Assert.Equal(5, hareket.Count);
        Assert.Equal((2000m, "Giris", YonKaynaklari.Bakiye), Ozet(hareket[2]));
        Assert.Equal((1000m, "Cikis", YonKaynaklari.Bakiye), Ozet(hareket[3]));

        foreach (var ara in new[] { "TOPLAM 600,00", "VADELİ HESAP HAREKETLERİ USD", "\fHesap No   Şube\n123-456789  Kadıköy" })
        {
            var iki = Banka("01.09.2026 ABC 300,00 700,00", "02.09.2026 ABC 250,00 450,00", "03.09.2026 ABC 450,00 0,00", ara,
                "01.09.2026 GHI 5.000,00 5.000,00", "02.09.2026 GHI 1.000,00 4.000,00", "03.09.2026 GHI 500,00 3.500,00").Satirlar;
            Assert.Equal(("Belirsiz", (string?)null), (iki[3].Yon, iki[3].YonKaynagi));
            Assert.Equal((1000m, "Cikis", YonKaynaklari.Bakiye), Ozet(iki[4]));
        }
        // Her sayfada yinelenen belge başlığı zinciri koparmaz.
        var sayfali = Banka("Hesap Hareketleri", "01.09.2026 ABC 300,00 700,00", "02.09.2026 ABC 250,00 450,00", "\fHesap Hareketleri", "03.09.2026 ABC 150,00 300,00").Satirlar;
        Assert.Equal((150m, "Cikis", YonKaynaklari.Bakiye), Ozet(sayfali[2]));
    }

    [Fact]
    public void Atlanan_satir_ve_baska_hesap_zinciri_koparir()
    {
        var atlanan = Banka(
            "01.09.2026 Hareket 100,00 10.100,00", "02.09.2026 Odeme 300,00 9.800,00", "Tahsilat 2.000,00 11.800,00", "03.09.2026 Odeme 1.000,00 10.800,00",
            "04.09.2026 Odeme 50,00 10.750,00", "05.09.2026 Odeme 25,00 10.725,00").Satirlar;
        Assert.Equal("Cikis", atlanan[1].Yon);
        Assert.Equal((1000m, "Belirsiz", null), Ozet(atlanan[2]));
        Assert.Equal((50m, "Cikis", YonKaynaklari.Bakiye), Ozet(atlanan[3]));

        foreach (var baslik in new[] { ("Hesap No: 111-1", "Hesap No: 222-2"), ("IBAN: TR11 0001", "IBAN: TR22 0002"), ("HESAP 1", "HESAP 2") })
        {
            var iki = Banka(
                baslik.Item1, "01.09.2026 Odeme 300,00 700,00", "02.09.2026 Odeme 250,00 450,00", "03.09.2026 Odeme 450,00 0,00",
                baslik.Item2, "01.09.2026 Odeme 5.000,00 5.000,00", "02.09.2026 Odeme 1.000,00 4.000,00", "03.09.2026 Odeme 500,00 3.500,00").Satirlar;
            Assert.Equal((5000m, "Belirsiz", null), Ozet(iki[3]));
            Assert.Equal((1000m, "Cikis", YonKaynaklari.Bakiye), Ozet(iki[4]));
        }
        // Aynı hesabın sayfa başında yinelenen numarası zinciri koparmaz.
        var sayfali = Banka("Hesap No: 111-1", "01.09.2026 Odeme 300,00 700,00", "02.09.2026 Odeme 250,00 450,00", "\fHesap No: 111-1", "03.09.2026 Odeme 150,00 300,00").Satirlar;
        Assert.Equal((150m, "Cikis", YonKaynaklari.Bakiye), Ozet(sayfali[2]));
    }

    [Fact]
    public void Sayfa_ortasinda_yeniden_baslayan_tablo_zinciri_koparir()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        static string Satir(string tarih, string tutar, string bakiye) => $"{tarih,-14}{"Odeme",-27}{tutar,12}{bakiye,16}";
        var rows = Banka(header, Satir("01.09.2026", "300,00", "700,00"), Satir("02.09.2026", "700,00", "0,00"), "Vadeli", header,
            Satir("01.09.2026", "5.000,00", "5.000,00"), Satir("02.09.2026", "1.000,00", "4.000,00")).Satirlar;
        Assert.Equal((5000m, "Belirsiz", null), Ozet(rows[2]));
        Assert.Equal((1000m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[3]));
    }

    [Fact]
    public void Bakiye_kolonlu_belgede_baglarin_cogu_tutmuyorsa_tek_rastlanti_kullanilmaz()
    {
        // Her satırda gün sonu bakiyesi basan düzen: yürüyen bakiye değildir.
        var header = $"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        static string Satir(string tarih, string ad, string tutar, string bakiye) => $"{tarih,-14}{ad,-27}{tutar,12}{bakiye,16}";
        var rows = Banka(header, Satir("01.09.2026", "Odeme", "100,00", "10.000,00"), Satir("02.09.2026", "Tahsilat", "1.000,00", "9.000,00"),
            Satir("02.09.2026", "Odeme", "2.000,00", "9.000,00"), Satir("03.09.2026", "Odeme", "40,00", "8.500,00"), Satir("03.09.2026", "Odeme", "460,00", "8.500,00")).Satirlar;
        Assert.DoesNotContain(rows, r => r.YonKaynagi == YonKaynaklari.Bakiye);
        Assert.Equal(1000m, rows[1].Tutar);
    }

    [Fact]
    public void Bakiye_diger_kanitlarla_celisiyorsa_zincir_butunuyle_birakilir()
    {
        // İşareti yazılmayan borç bakiye (kredi hesabı): çekim bakiyeyi artırır. Yönler tersine okunmaz.
        var read = Banka(
            "01.09.2026 Hareket 100,00 1.000,00", "02.09.2026 GIDEN EFT 200,00 1.200,00", "03.09.2026 GELEN EFT 500,00 700,00", "04.09.2026 Hareket 50,00 750,00");
        Assert.DoesNotContain(read.Satirlar, r => r.YonKaynagi == YonKaynaklari.Bakiye);
        Assert.Equal(("Belirsiz", (decimal?)null), (read.Satirlar[3].Yon, read.Satirlar[3].Tutar));
        // Yazılı işaretle tek çelişki de yeter: işaret kesin kanıttır.
        var isaretli = Banka("01.09.2026 Hareket 100,00 1.000,00", "02.09.2026 Hareket -200,00 1.200,00", "03.09.2026 Hareket 500,00 700,00");
        Assert.DoesNotContain(isaretli.Satirlar, r => r.YonKaynagi == YonKaynaklari.Bakiye);
        Assert.Contains(read.Uyarilar, w => w.Contains("yönler bakiyeden alınmadı"));
    }

    [Theory]
    [InlineData("GELEN HAVALE AHMET YILMAZ ÖNCEKİ BAKİYE ÖDEMESİ")]
    [InlineData("AÇILIŞ BAKİYESİ DÜZELTME")]
    public void Aciklamasinda_bakiye_gecen_hareket_devir_satiri_sayilmaz(string aciklama)
    {
        var rows = Banka("01.09.2026 Hareket 100,00 10.000,00", $"02.09.2026 {aciklama} 5.000,00 15.000,00", "03.09.2026 Hareket 250,00 15.250,00").Satirlar;
        Assert.Equal(3, rows.Count);
        Assert.Equal((5000m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[1]));
    }

    [Fact]
    public void Kart_ekstresinde_onceki_bakiye_odemesi_hareket_kalir()
    {
        var rows = EkstreMetinOkuyucu.Oku("Para Birimi: TL\n05.09.2026 ÖNCEKİ BAKİYE ÖDEMENİZ - TEŞEKKÜRLER -5.000,00\n06.09.2026 MARKET 100,00", "Kart", "Akbank").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.Equal(5000m, rows[0].Tutar);
    }

    [Fact]
    public void Devir_satiri_onceki_hareketin_devami_sayilmaz()
    {
        var rows = Banka("30.08.2026 UZUN ACIKLAMA", "BAŞLANGIÇ BAKİYESİ 5.000,00", "01.09.2026 Hareket 100,00 5.100,00", "02.09.2026 Hareket 50,00 5.050,00").Satirlar;
        Assert.Equal(3, rows.Count);
        Assert.Null(rows[0].Tutar);
        Assert.Equal((100m, "Giris", YonKaynaklari.Bakiye), Ozet(rows[1]));
    }

    [Fact]
    public void Bakiye_kolonlu_belgede_sarili_satir_cozulur()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        static string Satir(string tarih, string ad, string tutar, string bakiye) => $"{tarih,-14}{ad,-27}{tutar,12}{bakiye,16}";
        var rows = Banka(header, Satir("01.09.2026", "Hareket", "100,00", "1.000,00"), "02.09.2026    UZUN ACIKLAMA", Satir("", "DEVAMI", "50,00", "950,00"),
            Satir("03.09.2026", "Hareket", "25,00", "975,00")).Satirlar;
        Assert.Equal(3, rows.Count);
        Assert.Equal((50m, "Cikis", YonKaynaklari.Bakiye), Ozet(rows[1]));
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
        var rows = Banka("01.09.2026 Hareket 100,00 10.000,00", "02.09.2026 GELEN EFT geri cekildi 1.500,00 8.500,00", "03.09.2026 GELEN EFT 250,00 8.750,00", "04.09.2026 GIDEN EFT 50,00 8.700,00").Satirlar;
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
    [InlineData("POS HAKEDİŞ", "Giris")]
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
    [InlineData("EFT İŞLEM ÜCRETİ", "Cikis")]
    [InlineData("HESAP İŞLETİM ÜCRETİ", "Cikis")]
    [InlineData("POS SATIŞ KESİNTİ", "Cikis")]
    [InlineData("NAKİT YATIRMA ABC LTD VERGİ NO 1234567890", "Giris")]
    [InlineData("GELEN EFT BAYRAM BEDELİ", "Giris")]
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
    [InlineData("KREDİLİ MEVDUAT FAİZİ")]
    [InlineData("KREDİLİ   MEVDUAT FAİZİ")]
    [InlineData("KREDİLİ MEVDUAT FAİZ TAHSİLATI")]
    [InlineData("MEVDUAT FAİZİ STOPAJI")]
    [InlineData("MEVDUAT FAİZİ BSMV")]
    [InlineData("MEVDUAT FAİZ ÖDEMESİ STOPAJ DÜŞÜLMÜŞ NET")]
    [InlineData("POS BLOKE ÇÖZÜM KOMİSYON SONRASI")]
    [InlineData("POS HAKEDİŞ KOMİSYON KESİLMİŞ")]
    [InlineData("ATM PARA YATIRMA SİTE AİDATI")]
    [InlineData("HAVALE AYŞE KARGO ÜCRETİ")]
    [InlineData("ÜYE İŞYERİ PAKET BEDELİ")]
    [InlineData("ÜYE İŞYERİ KATKI PAYI")]
    [InlineData("POS KİRA BEDELİ")]
    [InlineData("POS SATIŞ MİGROS")]
    [InlineData("HAVALE AHMET ELEKTRİK FATURASI")]
    [InlineData("HAVALE AHMET VERGİ ÖDEMESİ")]
    [InlineData("GELEN ÇEK ÖDEMESİ")]
    public void Iki_anlamli_aciklama_belirsiz_kalir(string aciklama)
    {
        var row = Assert.Single(Banka($"01.09.2026 {aciklama}  100,00").Satirlar);
        Assert.Equal((100m, "Belirsiz", null), Ozet(row));
        Assert.Equal("Atla", row.OnerilenIslem);
    }

    [Fact]
    public void Alt_satira_bolunen_kredili_mevduat_faizi_giris_sayilmaz()
    {
        var row = Banka("01.09.2026 KREDİLİ", "MEVDUAT FAİZİ 250,00").Satirlar.Single();
        Assert.Equal((250m, "Belirsiz", null), Ozet(row));
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
