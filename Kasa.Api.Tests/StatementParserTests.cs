using Kasa.Api.Servisler;
using Microsoft.Extensions.Configuration;

namespace Kasa.Api.Tests;

public class StatementParserTests
{
    [Theory]
    [InlineData("Vakifbank")][InlineData("Akbank")][InlineData("QNB")][InlineData("Isbank")][InlineData("Garanti")][InlineData("Denizbank")]
    public void Tum_banka_secimlerinde_turkce_isaretli_hareketler_okunur(string bank)
    {
        var read=EkstreMetinOkuyucu.Oku("Para Birimi: TL\n01.09.2026 Gelen EFT  +1.250,50\n02.09.2026 Havale komisyonu  -12,30\n03.09.2026 Virman  -500,00", "Banka",bank);
        Assert.Equal(3,read.Satirlar.Count);
        Assert.Equal(1250.50m,read.Satirlar[0].Tutar);Assert.Equal("Gelir",read.Satirlar[0].OnerilenIslem);
        Assert.Equal(12.30m,read.Satirlar[1].Tutar);Assert.Equal("Komisyon",read.Satirlar[1].Sinif);Assert.Equal("Gider",read.Satirlar[1].OnerilenIslem);
        Assert.Equal("Atla",read.Satirlar[2].OnerilenIslem);Assert.Contains(read.Satirlar[2].Uyarilar,w=>w.Contains("transfer"));
        Assert.All(read.Satirlar,r=>Assert.Equal("TRY",r.ParaBirimi));
    }
    [Fact]
    public void Bakiye_kolonu_islem_tutari_olarak_alinmaz()
    {
        var header=$"{"Tarih",-14}{"Açıklama",-27}{"Tutar",12}{"Bakiye",16}";
        var line=$"{"01.09.2026",-14}{"POS komisyonu",-27}{"-25,00",12}{"15.975,00",16}";
        var missing=$"{"02.09.2026",-14}{"Belirsiz hareket",-27}{"",12}{"15.975,00",16}";
        var read=EkstreMetinOkuyucu.Oku("Para birimi: TRY\n"+header+"\n"+line+"\n"+missing,"Banka","Akbank");
        Assert.Equal(25m,read.Satirlar[0].Tutar); Assert.Equal("Cikis",read.Satirlar[0].Yon);
        Assert.Null(read.Satirlar[1].Tutar);
    }
    [Fact]
    public void Borc_alacak_kolonlari_giris_cikisi_belirler()
    {
        var header=$"{"Tarih",-14}{"Açıklama",-24}{"Borç",12}{"Alacak",12}{"Bakiye",14}";
        var a=$"{"01.09.2026",-14}{"Tahsilat",-24}{"0,00",12}{"100,00",12}{"900,00",14}";
        var b=$"{"02.09.2026",-14}{"Hesap ücreti",-24}{"25,00",12}{"0,00",12}{"875,00",14}";
        var rows=EkstreMetinOkuyucu.Oku(header+"\n"+a+"\n"+b,"Banka","QNB").Satirlar;
        Assert.Equal(100m,rows[0].Tutar);Assert.Equal("Giris",rows[0].Yon);
        Assert.Equal(25m,rows[1].Tutar);Assert.Equal("Cikis",rows[1].Yon);
    }
    [Fact]
    public void Basliksiz_cok_tutarli_satirda_bakiye_veya_vergi_tahmin_edilmez()
    {
        var row=Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Komisyon  100,00  5,00  15.000,00 TL","Banka","Garanti").Satirlar);
        Assert.Null(row.Tutar);Assert.Contains(row.Uyarilar,w=>w.Contains("Birden fazla tutar"));
    }
    [Fact]
    public void Ozet_toplam_ve_oran_satirlari_yeniden_harcama_olmaz()
    {
        var read=EkstreMetinOkuyucu.Oku("Hesap Kesim Tarihi: 23.09.2026\nDönem Borcu 2.000,00 TL\nAsgari ödeme 400,00 TL\nToplam Faiz 50,00 TL\nAkdi Faiz Oranı %4,50\n01.09.2026 Akdi faiz 50,00 TL\n02.09.2026 TOPLAM MARKET 100,00 TL","Kart","Denizbank");
        Assert.Equal(2,read.Satirlar.Count);Assert.Equal("Faiz",read.Satirlar[0].Sinif);Assert.Equal("KartHarcama",read.Satirlar[0].OnerilenIslem);
        Assert.Contains(read.Uyarilar,w=>w.Contains("mali hareket olarak alınmadı"));
    }
    [Fact]
    public void Kart_odeme_iade_ve_kredi_taksidi_borctan_ayrilir()
    {
        var rows=EkstreMetinOkuyucu.Oku("01.09.2026 Kart ödemesi +100,00 TL\n02.09.2026 Alış iade +20,00 TL\n03.09.2026 Kart alışveriş 75,00 TL","Kart","Vakifbank").Satirlar;
        Assert.Equal(new[]{"KartOdemesi","KartIade","KartHarcama"},rows.Select(r=>r.OnerilenIslem));
        var loan=Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Kredi taksit ödemesi -500,00 TL","Banka","Isbank").Satirlar);
        Assert.Contains(loan.Uyarilar,w=>w.Contains("Kredi takibinde"));
    }
    [Fact]
    public void Doviz_ve_bilinmeyen_para_acik_isaretlenir()
    {
        var rows=EkstreMetinOkuyucu.Oku("Para Birimi: USD\n01.09.2026 Komisyon 1.25\n02.09.2026 Hizmet 25,00 TL","Banka","Akbank").Satirlar;
        Assert.Equal("USD",rows[0].ParaBirimi);Assert.Equal("TRY",rows[1].ParaBirimi);
        Assert.Contains(rows[0].Uyarilar,w=>w.Contains("farklı para"));
        var unknown=Assert.Single(EkstreMetinOkuyucu.Oku("01.09.2026 Ücret 1,00","Banka","Akbank").Satirlar);
        Assert.Equal("Belirsiz",unknown.ParaBirimi);
    }
    [Fact]
    public void Eksik_yil_ve_satir_devami_duzeltme_icin_korunur()
    {
        var rows=EkstreMetinOkuyucu.Oku("01.09 Alış\n   Uzun mağaza açıklaması 12,34 TL\f02.09.2026 EFT  -40,00 TL","Banka","QNB").Satirlar;
        Assert.Equal(2,rows.Count);Assert.Null(rows[0].Tarih);Assert.Equal(12.34m,rows[0].Tutar);Assert.Equal(2,rows[1].Sayfa);
    }
    [Fact]
    public void Fazla_metni_ve_satiri_sinirlar()
    {
        Assert.Throws<PdfOkumaException>(()=>EkstreMetinOkuyucu.Oku(new string('x',1_000_001),"Banka","QNB"));
        Assert.Throws<PdfOkumaException>(()=>EkstreMetinOkuyucu.Oku(string.Join('\n',Enumerable.Repeat("01.09.2026 Komisyon -1,00 TL",1501)),"Banka","QNB"));
    }
    [Fact]
    public async Task Pdf_imzasi_gecersizse_harici_islem_baslatilmaz()
    {
        var reader=new PdfMetinOkuyucu(new ConfigurationBuilder().Build());
        var error=await Assert.ThrowsAsync<PdfOkumaException>(()=>reader.OkuAsync("not a PDF"u8.ToArray()));
        Assert.Equal(400,error.StatusCode);
    }
}
