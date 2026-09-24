using System.Security.Cryptography;
using System.Text;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Paket E inceleme: iki adımlı giriş kurulumunda anahtar QR kodu olarak da gösterilir. Kodlayıcı
/// (ISO/IEC 18004, bayt kipi) başvuru uygulamasının (Nayuki qrcodegen) ürettiği matrislerle birebir
/// karşılaştırılır; özetler "#"/"." satırlarının "\n" ile birleşiminin SHA-256'sıdır.
/// </summary>
public class QrKoduTests
{
    private static string Ozet(QrMatris m)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(m.Satirlar())));

    private static byte[] Dizi(int adet, int carpan, int ekle)
        => Enumerable.Range(0, adet).Select(i => (byte)((i * carpan + ekle) % 256)).ToArray();

    [Fact]
    public void Kisa_metin_surum_1_matrisi_birebir()
    {
        var m = QrKodu.Olustur("HELLO", QrSeviye.M);
        Assert.Equal(1, m.Surum);
        Assert.Equal(4, m.Maske);
        Assert.Equal(21, m.Boyut);
        string[] beklenen =
        [
            "#######.##.#..#######",
            "#.....#..##.#.#.....#",
            "#.###.#..####.#.###.#",
            "#.###.#.#..#..#.###.#",
            "#.###.#.#...#.#.###.#",
            "#.....#.#.##..#.....#",
            "#######.#.#.#.#######",
            "........#####........",
            "#...#.######.#####..#",
            "...###..#.###..#.####",
            "#.##..#.#.##..###..#.",
            "###..#...#...##.#....",
            "..#.###..#..###...##.",
            "........###.###..#.##",
            "#######.##..##...#.#.",
            "#.....#....##..#...#.",
            "#.###.#.#..#..###.#.#",
            "#.###.#....##....#.##",
            "#.###.#..###..####...",
            "#.....#..#...##......",
            "#######.#...#####.#.#",
        ];
        Assert.Equal(string.Join("\n", beklenen), m.Satirlar());
        Assert.True(m[0, 0]);          // sol üst bulucu köşesi koyu
        Assert.False(m[7, 0]);         // ayırıcı açık
    }

    [Fact]
    public void Otpauth_adresi_surum_7_surum_bilgisiyle()
    {
        var m = QrKodu.Olustur("otpauth://totp/Emar%20Kasa:emar?secret=JBSWY3DPEHPK3PXP&issuer=Emar%20Kasa&algorithm=SHA1&digits=6&period=30", QrSeviye.M);
        Assert.Equal(7, m.Surum);
        Assert.Equal(4, m.Maske);
        Assert.Equal(45, m.Boyut);
        Assert.Equal("6e3f6998fca5816665ee87fa53e355fa83fcfe3037331d0775c5d589c8e409bb", Ozet(m));
    }

    [Theory]
    [InlineData(QrSeviye.L, 11, 4, "7b99c17d63031644ed7ebfb115ecb9f6541712c385852cfc659dd775fbadcccc")]
    [InlineData(QrSeviye.Q, 16, 2, "f2aee5373805f83c4bb1c10850edd1c6316fe735d2c043a6d20802d93635752d")]
    [InlineData(QrSeviye.H, 18, 3, "c719b2aff8a4d36cf91b3771440360f854633ba6b30bc5aa5122caaf7c2e209a")]
    public void Coklu_blok_ve_duzeltme_seviyeleri(QrSeviye seviye, int surum, int maske, string ozet)
    {
        var m = QrKodu.Olustur(Dizi(300, 7, 3), seviye);
        Assert.Equal(surum, m.Surum);
        Assert.Equal(maske, m.Maske);
        Assert.Equal(17 + 4 * surum, m.Boyut);
        Assert.Equal(ozet, Ozet(m));
    }

    [Fact]
    public void En_buyuk_surum_40_sabit_maskeyle()
    {
        var m = QrKodu.Olustur(Dizi(2331, 13, 5), QrSeviye.M, maske: 5);
        Assert.Equal(40, m.Surum);
        Assert.Equal(5, m.Maske);
        Assert.Equal(177, m.Boyut);
        Assert.Equal("6b63a8358d7567d0b69fe0eba2af1530d74b766ebcbf486aeac4ab39050bc32a", Ozet(m));
    }

    [Fact]
    public void Sigmayan_veri_ve_gecersiz_maske_reddedilir()
    {
        var ex = Assert.Throws<ArgumentException>(() => QrKodu.Olustur(new byte[2332], QrSeviye.M));
        Assert.StartsWith("Veri QR koduna sığmıyor.", ex.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => QrKodu.Olustur("A", QrSeviye.M, maske: 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrKodu.Olustur("A", QrSeviye.M, maske: -1));
    }

    [Fact]
    public void Turkce_metin_utf8_olarak_kodlanir()
    {
        var m = QrKodu.Olustur("ŞİRKET ğüşıöç");
        Assert.Equal(QrKodu.Olustur(Encoding.UTF8.GetBytes("ŞİRKET ğüşıöç")).Satirlar(), m.Satirlar());
    }
}
