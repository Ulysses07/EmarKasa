namespace Kasa.App.Core.Tests;

/// <summary>Kart ızgarası (tasarım 2026-09-30 §2): satırdaki kutu sayısı genişliğe göre değişir, kutu en az 220 px'tir;
/// ayrıntı açık kutunun bulunduğu satırın hemen altında tam genişlikte yer alır, sonraki satırlar aşağı kayar.</summary>
public class KartIzgarasiHesabiTests
{
    private const double Aralik = 16;

    [Theory]
    [InlineData(1136, 4)]
    [InlineData(700, 3)]
    [InlineData(456, 2)]
    [InlineData(455, 1)]
    [InlineData(220, 1)]
    [InlineData(200, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Sutun_sayisi_en_az_220_piksellik_kutu_sigdirir(double genislik, int beklenen)
        => Assert.Equal(beklenen, KartIzgarasiHesabi.Olcu(genislik, Aralik).Sutun);

    [Fact]
    public void Kutular_satira_esit_genislikte_yayilir()
    {
        var olcu = KartIzgarasiHesabi.Olcu(1136, Aralik);
        Assert.Equal(272, olcu.KutuGenisligi);
        Assert.Equal(1136, olcu.Genislik);
        Assert.Equal(220, KartIzgarasiHesabi.Olcu(double.PositiveInfinity, Aralik).Genislik);
        Assert.Equal(200, KartIzgarasiHesabi.Olcu(200, Aralik).KutuGenisligi);
    }

    [Fact]
    public void Acik_kutu_yokken_kutular_satir_satir_dizilir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], -1, 300);
        Assert.Equal(4, y.Sutun);
        Assert.Equal(new[] { 0d, 288, 576, 864, 0 }, y.Kutular.Select(k => k.X));
        Assert.Equal(new[] { 0d, 0, 0, 0, 166 }, y.Kutular.Select(k => k.Y));
        Assert.All(y.Kutular, k => Assert.Equal(272, k.Genislik));
        Assert.Null(y.Ayrinti);
        Assert.Equal(316, y.Yukseklik);
    }

    [Fact]
    public void Ayrinti_acik_kutunun_satirindan_sonra_tam_genislikte_yer_alir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], 1, 300);
        Assert.Equal(new Dikdortgen(0, 166, 1136, 300), y.Ayrinti!.Value);
        Assert.Equal(482, y.Kutular[4].Y);   // ikinci satır ayrıntının altına kayar
        Assert.Equal(632, y.Yukseklik);
    }

    [Fact]
    public void Son_satirdaki_kutu_acilinca_ayrinti_en_alta_gelir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], 4, 300);
        Assert.Equal(166, y.Kutular[4].Y);
        Assert.Equal(new Dikdortgen(0, 332, 1136, 300), y.Ayrinti!.Value);
        Assert.Equal(632, y.Yukseklik);
    }

    [Fact]
    public void Satir_yuksekligi_satirdaki_en_yuksek_kutudur()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [100, 180, 120, 90, 100], -1, 0);
        Assert.All(y.Kutular.Take(4), k => Assert.Equal(180, k.Yukseklik));
        Assert.Equal(196, y.Kutular[4].Y);
        Assert.Equal(100, y.Kutular[4].Yukseklik);
    }

    [Fact]
    public void Kutu_yoksa_yukseklik_sifir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [], 0, 300);
        Assert.Empty(y.Kutular);
        Assert.Null(y.Ayrinti);
        Assert.Equal(0, y.Yukseklik);
    }

    [Theory]
    [InlineData(7, false, 1)]
    [InlineData(null, true, 3)]   // yeni kart formu: "Yeni kart ekle" kutusu (son kutu) açık
    [InlineData(42, false, -1)]   // listede olmayan kart
    [InlineData(null, false, -1)]
    public void Acik_kutu_sirasi_kimlikten_ya_da_yeni_kart_formundan_gelir(int? acikKimlik, bool yeniAcik, int beklenen)
        => Assert.Equal(beklenen, KartIzgarasiHesabi.AcikIndeks([5, 7, 9], acikKimlik, yeniAcik));
}
