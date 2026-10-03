namespace Kasa.App.Core.Tests;

/// <summary>Kartlar ekranında açılan ayrıntı ya da form: üstü görünür alanın içindeyse kaydırılmaz, değilse üstü görünür alanın
/// başına gelir (ScrollToPosition.Start). MakeVisible sığmayan öğeyi alttan hizaladığı için kullanılmaz.</summary>
public class KaydirmaHesabiTests
{
    [Theory]
    [InlineData(500, 400, 600)]     // üstü görünür alanın ortasında
    [InlineData(400, 400, 600)]     // tam başta
    [InlineData(399.5, 400, 600)]   // yuvarlama payı içinde
    [InlineData(951, 400, 600)]     // alttan 49 px yukarıda: başlık okunur
    public void Ustu_gorunur_alandaysa_kaydirilmaz(double hedefUst, double kaydirmaY, double gorunurYukseklik)
        => Assert.False(KaydirmaHesabi.BasaKaydirilmali(hedefUst, kaydirmaY, gorunurYukseklik));

    [Theory]
    [InlineData(100, 400, 600)]     // üstü görünür alanın üstünde kaldı
    [InlineData(398, 400, 600)]     // yuvarlama payından fazla yukarıda
    [InlineData(1200, 400, 600)]    // görünür alanın altında
    [InlineData(1000, 400, 600)]    // tam alt kenarda
    [InlineData(960, 400, 600)]     // alt kenara 40 px: yalnız kenarı görünür
    public void Ustu_gorunur_alanin_disindaysa_basa_kaydirilir(double hedefUst, double kaydirmaY, double gorunurYukseklik)
        => Assert.True(KaydirmaHesabi.BasaKaydirilmali(hedefUst, kaydirmaY, gorunurYukseklik));

    [Theory]
    [InlineData(500, 0, 0)]
    [InlineData(500, 0, -1)]
    [InlineData(double.NaN, 0, 600)]
    [InlineData(500, double.NaN, 600)]
    [InlineData(500, 0, double.PositiveInfinity)]
    public void Olculmemis_ya_da_gecersiz_degerde_kaydirilmaz(double hedefUst, double kaydirmaY, double gorunurYukseklik)
        => Assert.False(KaydirmaHesabi.BasaKaydirilmali(hedefUst, kaydirmaY, gorunurYukseklik));

    // Form, hata ve önizleme (G-2): tamamı görünmüyorsa başı 24 px boşlukla görünür alanın başına kaydırılır.

    [Theory]
    [InlineData(500, 300, 400, 600)]    // tamamı görünüyor
    [InlineData(400, 600, 400, 600)]    // tam sığıyor, tam yerinde
    [InlineData(399.5, 300, 400, 600)]  // yuvarlama payı içinde
    [InlineData(424, 900, 400, 600)]    // sığmıyor ama başı zaten boşlukla görünür alanın başında
    [InlineData(500, 300, 400, double.NaN)]
    [InlineData(500, 300, 400, 0)]
    [InlineData(double.NaN, 300, 400, 600)]
    [InlineData(500, -1, 400, 600)]
    public void Form_gorunuyorsa_ya_da_olculmemisse_kaydirilmaz(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik)
        => Assert.Null(KaydirmaHesabi.FormKaydirmasi(ust, yukseklik, kaydirmaY, gorunurYukseklik));

    [Theory]
    [InlineData(900, 300, 400, 600, 876)]    // başlığı altta görünen form: alt kenara değil, başı boşlukla üste gelir
    [InlineData(1200, 300, 400, 600, 1176)]  // tamamen aşağıda
    [InlineData(100, 300, 400, 600, 76)]     // yukarıda kaldı
    [InlineData(300, 300, 400, 600, 276)]    // yarısı yukarıda
    [InlineData(900, 800, 400, 600, 876)]    // sığmıyor
    [InlineData(400, 900, 400, 600, 376)]    // sığmıyor, başı tam kenarda: boşluk bırakılır
    [InlineData(10, 300, 400, 600, 0)]       // içeriğin başından yukarı taşmaz
    public void Gorunmeyen_hedefin_basi_bosluklu_olarak_gorunur_alanin_basina_kaydirilir(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik,
        double beklenen)
        => Assert.Equal(beklenen, KaydirmaHesabi.FormKaydirmasi(ust, yukseklik, kaydirmaY, gorunurYukseklik));
}
