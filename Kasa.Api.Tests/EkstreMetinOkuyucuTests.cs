using Kasa.Api.Servisler;
using Microsoft.Extensions.Configuration;

namespace Kasa.Api.Tests;

public class EkstreMetinOkuyucuTests
{
    [Theory]
    [InlineData("Vakifbank")]
    [InlineData("Akbank")]
    [InlineData("QNB")]
    [InlineData("Isbank")]
    [InlineData("Garanti")]
    [InlineData("Denizbank")]
    public void Tum_banka_secimlerinde_turkce_isaretli_hareketler_okunur(string bank)
    {
        var read = EkstreMetinOkuyucu.Oku("Para Birimi: TL\n01.09.2026 Gelen EFT  +1.250,50\n02.09.2026 Havale komisyonu  -12,30\n03.09.2026 Virman  -500,00", "Banka", bank);
        Assert.Equal(3, read.Satirlar.Count);
        Assert.Equal(1250.50m, read.Satirlar[0].Tutar);
        Assert.Equal("Gelir", read.Satirlar[0].OnerilenIslem);
        Assert.Equal(12.30m, read.Satirlar[1].Tutar);
        Assert.Equal("Komisyon", read.Satirlar[1].Sinif);
        Assert.Equal("Gider", read.Satirlar[1].OnerilenIslem);
        Assert.Equal("Atla", read.Satirlar[2].OnerilenIslem);
        Assert.Contains(read.Satirlar[2].Uyarilar, w => w.Contains("transfer"));
        Assert.All(read.Satirlar, r => Assert.Equal("TRY", r.ParaBirimi));
    }
    [Fact]
    public void Bakiye_kolonu_islem_tutari_olarak_alinmaz()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        var line = $"{"01.09.2026",-14}{"POS komisyonu",-27}{"-25,00",12}{"15.975,00",16}";
        var missing = $"{"02.09.2026",-14}{"Belirsiz hareket",-27}{"",12}{"15.975,00",16}";
        var read = EkstreMetinOkuyucu.Oku("Para birimi: TRY\n" + header + "\n" + line + "\n" + missing, "Banka", "Akbank");
        Assert.Equal(25m, read.Satirlar[0].Tutar);
        Assert.Equal("Cikis", read.Satirlar[0].Yon);
        Assert.Null(read.Satirlar[1].Tutar);
    }
    [Fact]
    public void Borc_alacak_kolonlari_giris_cikisi_belirler()
    {
        var header = $"{"Tarih",-14}{"Açıklama",-24}{"Borç",12}{"Alacak",12}{"Bakiye",14}";
        var a = $"{"01.09.2026",-14}{"Tahsilat",-24}{"0,00",12}{"100,00",12}{"900,00",14}";
        var b = $"{"02.09.2026",-14}{"Hesap ücreti",-24}{"25,00",12}{"0,00",12}{"875,00",14}";
        var rows = EkstreMetinOkuyucu.Oku(header + "\n" + a + "\n" + b, "Banka", "QNB").Satirlar;
        Assert.Equal(100m, rows[0].Tutar);
        Assert.Equal("Giris", rows[0].Yon);
        Assert.Equal(25m, rows[1].Tutar);
        Assert.Equal("Cikis", rows[1].Yon);
    }
    [Fact]
    public void Basliksiz_cok_tutarli_satirda_bakiye_veya_vergi_tahmin_edilmez()
    {
        var row = Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Komisyon  100,00  5,00  15.000,00 TL", "Banka", "Garanti").Satirlar);
        Assert.Null(row.Tutar);
        Assert.Contains(row.Uyarilar, w => w.Contains("Birden fazla tutar"));
    }
    [Fact]
    public void Ozet_toplam_ve_oran_satirlari_yeniden_harcama_olmaz()
    {
        var read = EkstreMetinOkuyucu.Oku("Hesap Kesim Tarihi: 23.09.2026\nDönem Borcu 2.000,00 TL\nAsgari ödeme 400,00 TL\nToplam Faiz 50,00 TL\nAkdi Faiz Oranı %4,50\n01.09.2026 Akdi faiz 50,00 TL\n02.09.2026 TOPLAM MARKET 100,00 TL", "Kart", "Denizbank");
        Assert.Equal(2, read.Satirlar.Count);
        Assert.Equal("Faiz", read.Satirlar[0].Sinif);
        Assert.Equal("KartHarcama", read.Satirlar[0].OnerilenIslem);
        Assert.Contains(read.Uyarilar, w => w.Contains("mali hareket olarak alınmadı"));
    }
    [Fact]
    public void Kart_odeme_iade_ve_kredi_taksidi_borctan_ayrilir()
    {
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026 Kart ödemesi +100,00 TL\n02.09.2026 Alış iade +20,00 TL\n03.09.2026 Kart alışveriş 75,00 TL", "Kart", "Vakifbank").Satirlar;
        Assert.Equal(new[] { "KartOdemesi", "KartIade", "KartHarcama" }, rows.Select(r => r.OnerilenIslem));
        var loan = Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Kredi taksit ödemesi -500,00 TL", "Banka", "Isbank").Satirlar);
        Assert.Contains(loan.Uyarilar, w => w.Contains("Kredi takibinde"));
    }
    [Fact]
    public void Doviz_ve_bilinmeyen_para_acik_isaretlenir()
    {
        var rows = EkstreMetinOkuyucu.Oku("Para Birimi: USD\n01.09.2026 Komisyon 1.25\n02.09.2026 Hizmet 25,00 TL", "Banka", "Akbank").Satirlar;
        Assert.Equal("USD", rows[0].ParaBirimi);
        Assert.Equal("TRY", rows[1].ParaBirimi);
        Assert.Contains(rows[0].Uyarilar, w => w.Contains("farklı para"));
        var unknown = Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Ücret 1,00", "Banka", "Akbank").Satirlar);
        Assert.Equal("Belirsiz", unknown.ParaBirimi);
    }
    [Fact]
    public void Farkli_sayfa_basliklari_kodsuz_hareketlere_kendi_para_birimini_verir()
    {
        var text = "Para Birimi: TL\n01.09.2026 Market -10,00"
            + "\fPara Birimi: USD\n02.09.2026 Hizmet -20,00";
        var rows = EkstreMetinOkuyucu.Oku(text, "Banka", "Akbank").Satirlar;
        Assert.Equal(new[] { "TRY", "USD" }, rows.Select(r => r.ParaBirimi));
        Assert.Contains(rows[1].Uyarilar, w => w.Contains("farklı para"));
    }
    [Fact]
    public void Basliksiz_sayfa_celisen_doviz_basliklarindan_tl_tahmin_etmez()
    {
        var text = "Para Birimi: TL\n01.09.2026 Market -10,00"
            + "\f02.09.2026 Kira -20,00"
            + "\fPara Birimi: USD\n03.09.2026 Hizmet -30,00";
        var rows = EkstreMetinOkuyucu.Oku(text, "Banka", "Akbank").Satirlar;
        Assert.Equal(new[] { "TRY", "Belirsiz", "USD" }, rows.Select(r => r.ParaBirimi));
        Assert.Contains(rows[1].Uyarilar, w => w.Contains("Para birimi okunamadı"));
    }
    [Fact]
    public void Basliksiz_sayfa_tutarlı_belge_para_birimini_devralir()
    {
        var text = "Para Birimi: TL\n01.09.2026 Market -10,00"
            + "\f02.09.2026 Kira -20,00"
            + "\fPara Birimi: TL\n03.09.2026 Hizmet -30,00";
        var rows = EkstreMetinOkuyucu.Oku(text, "Banka", "Akbank").Satirlar;
        Assert.All(rows, r => Assert.Equal("TRY", r.ParaBirimi));
    }
    [Fact]
    public void Ayni_sayfadaki_celisen_para_birimi_etiketleri_belirsiz_kalir()
    {
        var text = "Para Birimi: TL\nDöviz Cinsi: USD\n01.09.2026 Hizmet -10,00";
        var row = Assert.Single(EkstreMetinOkuyucu.Oku(text, "Banka", "Akbank").Satirlar);
        Assert.Equal("Belirsiz", row.ParaBirimi);
    }
    [Fact]
    public void Eksik_yil_ve_satir_devami_duzeltme_icin_korunur()
    {
        var rows = EkstreMetinOkuyucu.Oku("01.09 Alış\n   Uzun mağaza açıklaması 12,34 TL\f02.09.2026 EFT  -40,00 TL", "Banka", "QNB").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].Tarih);
        Assert.Equal(12.34m, rows[0].Tutar);
        Assert.Equal(2, rows[1].Sayfa);
    }
    [Fact]
    public void Fazla_metni_ve_satiri_sinirlar()
    {
        Assert.Throws<PdfOkumaException>(() => EkstreMetinOkuyucu.Oku(new string('x', 1_000_001), "Banka", "QNB"));
        Assert.Throws<PdfOkumaException>(() => EkstreMetinOkuyucu.Oku(string.Join('\n', Enumerable.Repeat("01.09.2026 Komisyon -1,00 TL", 1501)), "Banka", "QNB"));
    }
    // statement-1: kart ekstresinde işaretin anlamı bankaya göre değişir; alacak ve belirsiz ödeme satırları borç artışı önerilmez.
    [Fact]
    public void Kart_eksi_isaretli_alacak_harcama_onerilmez()
    {
        var row = Assert.Single(EkstreMetinOkuyucu.Oku("05.09.2026 ANINDA İNDİRİM -15,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal(15m, row.Tutar);
        Assert.Equal("Atla", row.OnerilenIslem);
        Assert.NotEqual("Cikis", row.Yon);
        Assert.Contains(row.Uyarilar, w => w.Contains("Kart alacağı"));
    }
    [Fact]
    public void Kart_otomatik_odeme_satiri_kart_borcu_odemesi_onerilmez()
    {
        var row = Assert.Single(EkstreMetinOkuyucu.Oku("12.09.2026 OTOMATİK ÖDEME TURKCELL 350,00 TL", "Kart", "Akbank").Satirlar);
        Assert.Equal("Atla", row.OnerilenIslem);
        Assert.Equal("Belirsiz", row.Yon);
        Assert.Contains(row.Uyarilar, w => w.Contains("ödeme geçiyor"));
        var talimat = Assert.Single(EkstreMetinOkuyucu.Oku("12.09.2026 OTOMATİK ÖDEME TALİMATI TURKCELL 350,00 TL", "Kart", "Akbank").Satirlar);
        Assert.Equal("KartHarcama", talimat.OnerilenIslem);
        Assert.Equal("Cikis", talimat.Yon);
        Assert.Contains(talimat.Uyarilar, w => w.Contains("ödeme kuruluşu"));
    }
    [Fact]
    public void Kart_odeme_kurulusu_harcamasi_uyariyla_harcama_onerilir()
    {
        var row = Assert.Single(EkstreMetinOkuyucu.Oku("10.09.2026 IYZICO ODEME HIZMETLERI 120,00 TL", "Kart", "QNB").Satirlar);
        Assert.Equal("KartHarcama", row.OnerilenIslem);
        Assert.Equal("Cikis", row.Yon);
        Assert.Contains(row.Uyarilar, w => w.Contains("ödeme kuruluşu"));
        // "Ödeme" geçen faiz/ücret satırı borçtur; kart borcu ödemesi önerilmez.
        var fee = Assert.Single(EkstreMetinOkuyucu.Oku("11.09.2026 GECİKMİŞ ÖDEME FAİZİ 25,00 TL", "Kart", "QNB").Satirlar);
        Assert.Equal("KartHarcama", fee.OnerilenIslem);
        Assert.Equal("Faiz", fee.Sinif);
    }
    [Fact]
    public void Kart_isaret_anlami_yonu_bilinen_satirlardan_cikarilir()
    {
        // Ödemeyi eksi basan banka: eksi alacak, işaretsiz harcama.
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026 ÖDEME-TEŞEKKÜR EDERİZ -5.000,00 TL\n03.09.2026 MIGROS 75,00 TL\n04.09.2026 BIM -20,00 TL", "Kart", "Isbank").Satirlar;
        Assert.Equal(new[] { "KartOdemesi", "KartHarcama", "Atla" }, rows.Select(r => r.OnerilenIslem));
        Assert.Equal("Giris", rows[2].Yon);
        Assert.Contains(rows[2].Uyarilar, w => w.Contains("Kart alacağı"));
        Assert.Empty(rows[1].Uyarilar);
        // Ödemeyi artı basan banka: eksi harcama, artı alacak.
        rows = EkstreMetinOkuyucu.Oku("01.09.2026 Kart ödemesi +1.000,00 TL\n02.09.2026 MIGROS -75,00 TL\n03.09.2026 BIM +20,00 TL", "Kart", "Akbank").Satirlar;
        Assert.Equal(new[] { "KartOdemesi", "KartHarcama", "Atla" }, rows.Select(r => r.OnerilenIslem));
        Assert.Equal(new[] { "Giris", "Cikis", "Giris" }, rows.Select(r => r.Yon));
        // Faiz/ücret borçtur: işaretli faiz satırı da anlamı belirler.
        rows = EkstreMetinOkuyucu.Oku("01.09.2026 Akdi faiz -50,00 TL\n02.09.2026 MIGROS -75,00 TL\n03.09.2026 BIM +20,00 TL", "Kart", "Denizbank").Satirlar;
        Assert.Equal(new[] { "KartHarcama", "KartHarcama", "Atla" }, rows.Select(r => r.OnerilenIslem));
    }
    [Fact]
    public void Kart_isaret_anlami_bilinmiyorsa_yon_belirsiz_kalir()
    {
        var read = EkstreMetinOkuyucu.Oku("04.09.2026 BIM -20,00 TL", "Kart", "Vakifbank");
        var row = Assert.Single(read.Satirlar);
        Assert.Equal("Belirsiz", row.Yon);
        Assert.Equal("Atla", row.OnerilenIslem);
        Assert.Contains(row.Uyarilar, w => w.Contains("işaret"));
        Assert.Contains(read.Uyarilar, w => w.Contains("yönü belirsiz"));
        // Çelişen kanıt (eksi ödeme ve eksi faiz) tahmin üretmez; anlamı açıklamadan belli satırlar yine doğru önerilir.
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026 Kart ödemesi -100,00 TL\n02.09.2026 Akdi faiz -5,00 TL\n03.09.2026 MIGROS -75,00 TL", "Kart", "Garanti").Satirlar;
        Assert.Equal(new[] { "KartOdemesi", "KartHarcama", "Atla" }, rows.Select(r => r.OnerilenIslem));
        Assert.Equal("Belirsiz", rows[2].Yon);
    }
    [Fact]
    public void Banka_isaretli_hareketin_yonu_degismez()
    {
        var rows = EkstreMetinOkuyucu.Oku("Para Birimi: TL\n01.09.2026 POS harcaması -20,00\n02.09.2026 İade +5,00", "Banka", "Akbank").Satirlar;
        Assert.Equal(new[] { "Cikis", "Giris" }, rows.Select(r => r.Yon));
        Assert.Equal(new[] { "Gider", "Gelir" }, rows.Select(r => r.OnerilenIslem));
    }

    // statement-3: para birimi yalnız tutara bitişik koddan okunur; adres kısaltması (Cad.) döviz sayılmaz.
    [Fact]
    public void Adresteki_cadde_kisaltmasi_doviz_sayilmaz()
    {
        var rows = EkstreMetinOkuyucu.Oku("03.09.2026 MIGROS BAGDAT CAD ISTANBUL 412,35 TL\n04.09.2026 MIGROS BAGDAT CAD 50,00 TL", "Kart", "Akbank").Satirlar;
        Assert.All(rows, r => { Assert.Equal("TRY", r.ParaBirimi); Assert.DoesNotContain(r.Uyarilar, w => w.Contains("para birim", StringComparison.OrdinalIgnoreCase)); });
        Assert.Equal(412.35m, rows[0].Tutar);
    }
    [Fact]
    public void Altbilgideki_adres_belge_para_birimini_bozmaz()
    {
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026 Market 250,00 TL\n02.09.2026 Kira -1.000,00\nBüyükdere Cad. No:1 Şişli İstanbul", "Banka", "Garanti").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("TRY", r.ParaBirimi));
        var header = Assert.Single(EkstreMetinOkuyucu.Oku("Adres: Büyükdere Cad. No:1   Para Birimi: TL\n01.09.2026 Hizmet -10,00", "Banka", "Garanti").Satirlar);
        Assert.Equal("TRY", header.ParaBirimi);
        // Para birimi yalnız tablo başlığında yazan belge önceki gibi TL okunur.
        foreach (var title in new[] { "Tarih       Açıklama       Tutar (TL)", "Tarih       Açıklama       Tutar TL" })
            Assert.Equal("TRY", Assert.Single(EkstreMetinOkuyucu.Oku(title + "\n01.09.2026  Hizmet BAGDAT CAD  -10,00", "Banka", "Garanti").Satirlar).ParaBirimi);
    }
    [Theory]
    [InlineData("Para Birimi: Türk Lirası        Şube Adresi: Bağdat Cad. No:5")]
    [InlineData("Para Birimi: Türk Lirası Şube Adresi: Bağdat Cad. No:5")]
    [InlineData("Hesap Cinsi: Vadesiz TL Hesabı    Şube: Bağdat Cad. Şubesi")]
    [InlineData("Hesap Cinsi: VADESIZ    Para Birimi : TL    Şube: BAĞDAT CAD. ŞUBESİ")]
    public void Etiket_satirindaki_cadde_kisaltmasi_belge_para_birimini_bozmaz(string baslik)
    {
        // Etiket değeri yalnız kendi alanından (kolon boşluğu ya da sonraki "Etiket:" öncesi) okunur; aynı satırdaki şube
        // adresindeki "Cad." belgeyi Kanada doları yapmaz, kodsuz satırlar kaydedilebilir TL kalır.
        var rows = EkstreMetinOkuyucu.Oku(baslik + "\n01.09.2026 Market -250,00\n02.09.2026 Kira -1.000,00", "Banka", "Garanti").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal("TRY", r.ParaBirimi); Assert.DoesNotContain(r.Uyarilar, w => w.Contains("para birim", StringComparison.OrdinalIgnoreCase)); });
    }
    [Fact]
    public void Para_birimi_yazmayan_hesap_cinsi_etiketi_adresten_doviz_okumaz()
    {
        // "Hesap Cinsi: VADESIZ" para birimi söylemez; şube adresindeki "CAD." yerine tutarların yanındaki TL belirleyicidir.
        var rows = EkstreMetinOkuyucu.Oku("Hesap Cinsi: VADESIZ     Şube: BAĞDAT CAD. ŞUBESİ\n01.09.2026 Market 250,00 TL\n02.09.2026 Kira -1.000,00", "Banka", "Garanti").Satirlar;
        Assert.Equal(new[] { "TRY", "TRY" }, rows.Select(r => r.ParaBirimi));
        // Gerçek döviz hesabı etiketi kodla ya da yazıyla yine döviz okunur.
        foreach (var (baslik, kod) in new[] { ("Para Birimi: CAD", "CAD"), ("Para Birimi:\tCAD", "CAD"), ("Para Birimi: ABD Doları", "USD"), ("Hesap Cinsi: VADESİZ EURO", "EUR"), ("Döviz Cinsi: Kanada Doları   Şube: Bağdat Cad.", "CAD") })
        {
            var row = Assert.Single(EkstreMetinOkuyucu.Oku(baslik + "\n01.09.2026 Hizmet -10,00", "Banka", "Garanti").Satirlar);
            Assert.Equal(kod, row.ParaBirimi);
            Assert.Contains(row.Uyarilar, w => w.Contains("farklı para"));
        }
    }
    [Fact]
    public void Tutara_bitisik_olmayan_doviz_kodu_satiri_sessizce_tl_saymaz()
    {
        // Döviz bölüm başlığı olan belgede kodsuz satır TL varsayılmaz.
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026 Market 250,00 TL\nUSD İşlemleri\n02.09.2026 AMAZON 12,00", "Kart", "Garanti").Satirlar;
        Assert.Equal("TRY", rows[0].ParaBirimi);
        Assert.Equal("Belirsiz", rows[1].ParaBirimi);
        Assert.Contains(rows[1].Uyarilar, w => w.Contains("Para birimi"));
        // Ayrı döviz kolonu: TL etiketli belgede USD yazan satır sessizce TL olmaz; uyarıyla Belirsiz gelir (ek onayla kaydedilir).
        var usd = Assert.Single(EkstreMetinOkuyucu.Oku("Para Birimi: TL\n03.09.2026  AMAZON EU        USD         12,00", "Banka", "Akbank").Satirlar);
        Assert.Equal("Belirsiz", usd.ParaBirimi);
        Assert.Contains(usd.Uyarilar, w => w.Contains("USD") && w.Contains("tutarın yanında değil"));
        // Döviz etiketli belgede açıklamadaki "TL" satırı TL'ye çevirmez.
        var foreign = Assert.Single(EkstreMetinOkuyucu.Oku("Para Birimi: USD\n03.09.2026  TL HESABINA VIRMAN        -12,00", "Banka", "Akbank").Satirlar);
        Assert.Equal("USD", foreign.ParaBirimi);
    }
    [Fact]
    public void Ayri_kolondaki_tl_kodu_gereksiz_belirsizlik_uretmez()
    {
        var rows = EkstreMetinOkuyucu.Oku("01.09.2026  MARKET          412,35        TL\n02.09.2026  KIRA          1.000,00        TL", "Kart", "QNB").Satirlar;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal("TRY", r.ParaBirimi); Assert.DoesNotContain(r.Uyarilar, w => w.Contains("para birim", StringComparison.OrdinalIgnoreCase)); });
        var labelled = Assert.Single(EkstreMetinOkuyucu.Oku("Para Birimi: TL\n01.09.2026  MARKET          412,35        TL", "Kart", "QNB").Satirlar);
        Assert.Equal("TRY", labelled.ParaBirimi);
        Assert.Equal("KartHarcama", labelled.OnerilenIslem);
        Assert.Empty(labelled.Uyarilar);
    }
    [Fact]
    public void Tutara_bitisik_para_birimi_ve_lira_isareti_okunur()
    {
        var lira = Assert.Single(EkstreMetinOkuyucu.Oku("05.09.2026 Market 12,50 ₺", "Banka", "QNB").Satirlar);
        Assert.Equal("TRY", lira.ParaBirimi);
        Assert.Equal(12.50m, lira.Tutar);
        var usd = Assert.Single(EkstreMetinOkuyucu.Oku("06.09.2026 Hizmet USD 10,00", "Banka", "QNB").Satirlar);
        Assert.Equal("USD", usd.ParaBirimi);
        Assert.Contains(usd.Uyarilar, w => w.Contains("farklı para"));
        var mixed = Assert.Single(EkstreMetinOkuyucu.Oku("07.09.2026 AMAZON 12,00 USD 450,00 TL", "Kart", "QNB").Satirlar);
        Assert.Equal("Karisik", mixed.ParaBirimi);
    }

    // statement-9: taksit satırı tek taksitli yeni harcama olarak önerilmez; taksit bilgisi uyarıda korunur.
    [Fact]
    public void Kart_taksit_satiri_yeni_harcama_onerilmez()
    {
        var row = Assert.Single(EkstreMetinOkuyucu.Oku("15.07.2026 MEDIAMARKT 2/6 TAKSİT 150,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("Atla", row.OnerilenIslem);
        Assert.Equal(150m, row.Tutar);
        Assert.Equal(new DateOnly(2026, 7, 15), row.Tarih);
        Assert.Contains(row.Uyarilar, w => w.Contains("2/6. taksidi") && w.Contains("6 taksitle"));
        Assert.Contains("2/6", row.Aciklama);
        var other = Assert.Single(EkstreMetinOkuyucu.Oku("16.07.2026 TEKNOSA TAKSİT 03/12 250,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("Atla", other.OnerilenIslem);
        Assert.Contains(other.Uyarilar, w => w.Contains("3/12. taksidi"));
        var dotted = Assert.Single(EkstreMetinOkuyucu.Oku("17.07.2026 BOYNER 4/6. TAKSİT 99,90 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("Atla", dotted.OnerilenIslem);
        Assert.Contains(dotted.Uyarilar, w => w.Contains("4/6. taksidi"));
        // Taksit oranı olmayan, tarihi eğik çizgili satır taksit sayılmaz.
        var plain = Assert.Single(EkstreMetinOkuyucu.Oku("15/07/2026 MEDIAMARKT TAKSİTLİ SATIŞ 150,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("KartHarcama", plain.OnerilenIslem);
        Assert.DoesNotContain(plain.Uyarilar, w => w.Contains("taksid"));
    }
    [Fact]
    public void Kart_taksit_kolonu_ve_parantezli_oran_taksit_sayilir()
    {
        // Taksit oranı ayrı "Taksit" kolonundaysa satırda "TAKSİT" kelimesi geçmez; oran kolonun altından okunur.
        var header = $"{"İşlem Tarihi",-14}{"Açıklama",-20}{"Taksit",-10}{"Tutar",12}";
        var taksitli = $"{"15.07.2026",-14}{"MEDIAMARKT",-20}{"2/6",-10}{"150,00 TL",12}";
        var tek = $"{"16.07.2026",-14}{"MIGROS 1/2 KG",-20}{"",-10}{"80,00 TL",12}";
        var rows = EkstreMetinOkuyucu.Oku(header + "\n" + taksitli + "\n" + tek, "Kart", "Garanti").Satirlar;
        Assert.Equal(new[] { "Atla", "KartHarcama" }, rows.Select(r => r.OnerilenIslem));
        Assert.Equal(150m, rows[0].Tutar);
        Assert.Contains(rows[0].Uyarilar, w => w.Contains("2/6. taksidi"));
        // Açıklamadaki "1/2" Taksit kolonunun dışında kalır; taksit sayılmaz.
        Assert.DoesNotContain(rows[1].Uyarilar, w => w.Contains("taksid"));
        // Parantez içindeki oran kelime olmadan da taksittir.
        var paren = Assert.Single(EkstreMetinOkuyucu.Oku("15.07.2026 MEDIAMARKT (2/6) 150,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("Atla", paren.OnerilenIslem);
        Assert.Contains(paren.Uyarilar, w => w.Contains("2/6. taksidi"));
        // Parantezli ama tarih olan ya da geçersiz oran taksit sayılmaz.
        var date = Assert.Single(EkstreMetinOkuyucu.Oku("15.07.2026 MEDIAMARKT (15/07) 150,00 TL", "Kart", "Garanti").Satirlar);
        Assert.Equal("KartHarcama", date.OnerilenIslem);
        Assert.DoesNotContain(date.Uyarilar, w => w.Contains("taksid"));
    }
    [Fact]
    public void Kart_borc_kolonundaki_indirim_adli_isyeri_harcama_onerilir()
    {
        // Borç/Alacak kolonu kesindir: adında "indirim/bonus" geçen işyerinin borç satırı alacak sayılıp önerisini kaybetmez.
        var header = $"{"Tarih",-14}{"Açıklama",-26}{"Borç",12}{"Alacak",12}";
        var borc = $"{"01.09.2026",-14}{"A101 INDIRIM MARKET",-26}{"45,00",12}{"",12}";
        var bonus = $"{"02.09.2026",-14}{"BONUS FLAS KAMPANYA",-26}{"30,00",12}{"",12}";
        var alacak = $"{"03.09.2026",-14}{"ANINDA INDIRIM",-26}{"",12}{"15,00",12}";
        var rows = EkstreMetinOkuyucu.Oku(header + "\n" + borc + "\n" + bonus + "\n" + alacak, "Kart", "Akbank").Satirlar;
        Assert.Equal(new[] { "KartHarcama", "KartHarcama", "Atla" }, rows.Select(r => r.OnerilenIslem));
        Assert.Equal(new[] { "Cikis", "Cikis", "Giris" }, rows.Select(r => r.Yon));
        Assert.All(rows.Take(2), r => Assert.DoesNotContain(r.Uyarilar, w => w.Contains("Kart alacağı")));
        Assert.Contains(rows[2].Uyarilar, w => w.Contains("Kart alacağı"));
        // B/A soneki de kesindir.
        var suffix = Assert.Single(EkstreMetinOkuyucu.Oku("04.09.2026 PUAN MARKET 45,00 B", "Kart", "Akbank").Satirlar);
        Assert.Equal("KartHarcama", suffix.OnerilenIslem);
        Assert.Equal("Cikis", suffix.Yon);
        // Kolonsuz, işaretsiz indirim satırı önceki gibi Atla ve uyarıyla gelir.
        var unsigned = Assert.Single(EkstreMetinOkuyucu.Oku("05.09.2026 ANINDA INDIRIM 15,00 TL", "Kart", "Akbank").Satirlar);
        Assert.Equal("Atla", unsigned.OnerilenIslem);
        Assert.Contains(unsigned.Uyarilar, w => w.Contains("Kart alacağı"));
    }

    [Fact]
    public async Task Pdf_imzasi_gecersizse_harici_islem_baslatilmaz()
    {
        var reader = new PdfMetinOkuyucu(new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<PdfOkumaException>(() => reader.OkuAsync("not a PDF"u8.ToArray(), TestContext.Current.CancellationToken));
        Assert.Equal(400, error.StatusCode);
    }
}
