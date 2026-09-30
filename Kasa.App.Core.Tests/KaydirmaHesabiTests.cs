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
}
