using System.Text;

namespace Kasa.App.Core;

/// <summary>QR hata düzeltme seviyesi (kurtarılabilen hasar: L %7, M %15, Q %25, H %30).</summary>
public enum QrSeviye { L, M, Q, H }

/// <summary>
/// QR kodunun modülleri (<c>true</c> = koyu). Sessiz alan (4 modüllük beyaz kenar) dahil değildir;
/// çizen ekler (bkz. <see cref="QrKodu.SessizAlan"/>).
/// </summary>
public sealed class QrMatris
{
    private readonly bool[,] _moduller;

    internal QrMatris(bool[,] moduller, int surum, QrSeviye seviye, int maske)
    {
        _moduller = moduller;
        Surum = surum;
        Seviye = seviye;
        Maske = maske;
    }

    /// <summary>1–40; kenar 17 + 4 × sürüm modül.</summary>
    public int Surum { get; }
    public QrSeviye Seviye { get; }
    public int Maske { get; }
    public int Boyut => _moduller.GetLength(0);

    /// <summary>Sütun <paramref name="x"/>, satır <paramref name="y"/> koyu mu.</summary>
    public bool this[int x, int y] => _moduller[y, x];

    /// <summary>Satır satır "#" (koyu) ve "." (açık): testler ve karşılaştırma için.</summary>
    public string Satirlar()
    {
        var sb = new StringBuilder(Boyut * (Boyut + 1));
        for (var y = 0; y < Boyut; y++)
        {
            if (y > 0) sb.Append('\n');
            for (var x = 0; x < Boyut; x++) sb.Append(this[x, y] ? '#' : '.');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Küçük QR kodu üretici (ISO/IEC 18004; bayt kipi, sürüm 1–40, dört hata düzeltme seviyesi): iki
/// adımlı giriş kurulumunda otpauth:// adresi telefonla okutulsun, 32 harflik anahtar elle yazılmasın
/// diye. NuGet paketi eklenmedi; adımlar standarttaki gibidir: veri bitleri ve dolgu → Reed–Solomon
/// blokları ve serpiştirme → sabit desenler ve yerleştirme → en az cezalı maske.
/// </summary>
public static class QrKodu
{
    /// <summary>Okuyucuların beklediği beyaz kenar (modül).</summary>
    public const int SessizAlan = 4;

    // Sürüm başına (indeks = sürüm; 0 kullanılmaz) blok başına hata düzeltme kod sözcüğü ve blok sayısı.
    private static readonly int[][] BlokHataSozcugu =
    [
        [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // L
        [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28], // M
        [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // Q
        [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // H
    ];

    private static readonly int[][] BlokSayisi =
    [
        [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25], // L
        [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49], // M
        [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68], // Q
        [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81], // H
    ];

    /// <summary>Metni UTF-8 baytları olarak kodlar; sığan en küçük sürüm seçilir.</summary>
    public static QrMatris Olustur(string metin, QrSeviye seviye = QrSeviye.M, int? maske = null)
        => Olustur(Encoding.UTF8.GetBytes(metin), seviye, maske);

    /// <param name="maske">0–7; null ise en az cezalı maske seçilir.</param>
    public static QrMatris Olustur(byte[] veri, QrSeviye seviye = QrSeviye.M, int? maske = null)
    {
        if (maske is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(maske), "Maske 0–7 olmalı.");
        var surum = 1;
        while (4 + SayacBiti(surum) + veri.Length * 8 > VeriSozcugu(surum, seviye) * 8)
            if (++surum > 40) throw new ArgumentException("Veri QR koduna sığmıyor.", nameof(veri));

        // Veri bitleri: kip (bayt = 0100), uzunluk, baytlar; sonlandırıcı, bayt sınırı, dolgu baytları.
        var bitler = new List<bool>();
        void Ekle(int deger, int uzunluk)
        {
            for (var i = uzunluk - 1; i >= 0; i--) bitler.Add(((deger >> i) & 1) != 0);
        }
        Ekle(0b0100, 4);
        Ekle(veri.Length, SayacBiti(surum));
        foreach (var b in veri) Ekle(b, 8);
        var kapasite = VeriSozcugu(surum, seviye) * 8;
        Ekle(0, Math.Min(4, kapasite - bitler.Count));
        Ekle(0, (8 - bitler.Count % 8) % 8);
        for (var dolgu = 0xEC; bitler.Count < kapasite; dolgu ^= 0xEC ^ 0x11) Ekle(dolgu, 8);
        var sozcukler = new byte[bitler.Count / 8];
        for (var i = 0; i < bitler.Count; i++)
            if (bitler[i]) sozcukler[i >> 3] |= (byte)(0x80 >> (i & 7));

        var tumu = HataDuzeltmeEkle(sozcukler, surum, seviye);
        var boyut = surum * 4 + 17;
        var m = new bool[boyut, boyut];
        var sabit = new bool[boyut, boyut];
        SabitDesenler(m, sabit, surum, seviye);
        Yerlestir(m, sabit, tumu);

        var secilen = maske ?? 0;
        if (maske is null)
        {
            var enAz = int.MaxValue;
            for (var k = 0; k < 8; k++)
            {
                MaskeUygula(m, sabit, k);
                BicimBitleri(m, sabit, seviye, k);
                var ceza = Ceza(m);
                if (ceza < enAz)
                {
                    enAz = ceza;
                    secilen = k;
                }
                MaskeUygula(m, sabit, k);   // XOR: geri alır
            }
        }
        MaskeUygula(m, sabit, secilen);
        BicimBitleri(m, sabit, seviye, secilen);
        return new QrMatris(m, surum, seviye, secilen);
    }

    private static int SayacBiti(int surum) => surum <= 9 ? 8 : 16;

    /// <summary>Sabit desenler dışında kalan (veri + hata düzeltme) modül sayısı.</summary>
    private static int HamModul(int surum)
    {
        var sonuc = (16 * surum + 128) * surum + 64;
        if (surum >= 2)
        {
            var hizalama = surum / 7 + 2;
            sonuc -= (25 * hizalama - 10) * hizalama - 55;
            if (surum >= 7) sonuc -= 36;
        }
        return sonuc;
    }

    private static int VeriSozcugu(int surum, QrSeviye seviye)
        => HamModul(surum) / 8 - BlokHataSozcugu[(int)seviye][surum] * BlokSayisi[(int)seviye][surum];

    /// <summary>Veriyi bloklara böler, her bloğa Reed–Solomon sözcükleri ekler ve serpiştirir.</summary>
    private static byte[] HataDuzeltmeEkle(byte[] veri, int surum, QrSeviye seviye)
    {
        var blokSayisi = BlokSayisi[(int)seviye][surum];
        var hataUzunlugu = BlokHataSozcugu[(int)seviye][surum];
        var ham = HamModul(surum) / 8;
        var kisaBlok = blokSayisi - ham % blokSayisi;
        var kisaUzunluk = ham / blokSayisi;
        var bolen = RsBolen(hataUzunlugu);

        var bloklar = new List<byte[]>(blokSayisi);
        for (int i = 0, k = 0; i < blokSayisi; i++)
        {
            var veriUzunlugu = kisaUzunluk - hataUzunlugu + (i < kisaBlok ? 0 : 1);
            var dat = veri.AsSpan(k, veriUzunlugu).ToArray();
            k += veriUzunlugu;
            var hata = RsKalan(dat, bolen);
            // Kısa bloklara serpiştirmede atlanan bir yer tutucu eklenir (hepsi aynı boyda olsun).
            var blok = new byte[kisaUzunluk + 1];
            dat.CopyTo(blok, 0);
            hata.CopyTo(blok, blok.Length - hataUzunlugu);
            bloklar.Add(blok);
        }

        var sonuc = new byte[ham];
        var n = 0;
        for (var i = 0; i < bloklar[0].Length; i++)
            for (var j = 0; j < bloklar.Count; j++)
                if (i != kisaUzunluk - hataUzunlugu || j >= kisaBlok)
                    sonuc[n++] = bloklar[j][i];
        return sonuc;
    }

    private static byte[] RsBolen(int derece)
    {
        var sonuc = new byte[derece];
        sonuc[derece - 1] = 1;
        byte kok = 1;
        for (var i = 0; i < derece; i++)
        {
            for (var j = 0; j < sonuc.Length; j++)
            {
                sonuc[j] = RsCarp(sonuc[j], kok);
                if (j + 1 < sonuc.Length) sonuc[j] ^= sonuc[j + 1];
            }
            kok = RsCarp(kok, 0x02);
        }
        return sonuc;
    }

    private static byte[] RsKalan(byte[] veri, byte[] bolen)
    {
        var sonuc = new byte[bolen.Length];
        foreach (var b in veri)
        {
            var carpan = (byte)(b ^ sonuc[0]);
            Array.Copy(sonuc, 1, sonuc, 0, sonuc.Length - 1);
            sonuc[^1] = 0;
            for (var i = 0; i < sonuc.Length; i++) sonuc[i] ^= RsCarp(bolen[i], carpan);
        }
        return sonuc;
    }

    /// <summary>GF(2^8) çarpımı (indirgeyen polinom 0x11D).</summary>
    private static byte RsCarp(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static void Koy(bool[,] m, bool[,] sabit, int x, int y, bool koyu)
    {
        m[y, x] = koyu;
        sabit[y, x] = true;
    }

    /// <summary>Zamanlama, bulucu ve hizalama desenleri; biçim (yer ayırma) ve sürüm bilgisi.</summary>
    private static void SabitDesenler(bool[,] m, bool[,] sabit, int surum, QrSeviye seviye)
    {
        var boyut = m.GetLength(0);
        for (var i = 0; i < boyut; i++)
        {
            Koy(m, sabit, 6, i, i % 2 == 0);
            Koy(m, sabit, i, 6, i % 2 == 0);
        }
        foreach (var (cx, cy) in new[] { (3, 3), (boyut - 4, 3), (3, boyut - 4) })
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= boyut || y < 0 || y >= boyut) continue;
                    var uzaklik = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Koy(m, sabit, x, y, uzaklik != 2 && uzaklik != 4);
                }

        var konumlar = HizalamaKonumlari(surum, boyut);
        for (var i = 0; i < konumlar.Length; i++)
            for (var j = 0; j < konumlar.Length; j++)
            {
                // Bulucu desenlerinin köşeleri atlanır.
                if ((i == 0 && j == 0) || (i == 0 && j == konumlar.Length - 1) || (i == konumlar.Length - 1 && j == 0)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Koy(m, sabit, konumlar[i] + dx, konumlar[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

        BicimBitleri(m, sabit, seviye, 0);
        if (surum < 7) return;
        var kalan = surum;
        for (var i = 0; i < 12; i++) kalan = (kalan << 1) ^ ((kalan >> 11) * 0x1F25);
        var bitler = surum << 12 | kalan;
        for (var i = 0; i < 18; i++)
        {
            var koyu = ((bitler >> i) & 1) != 0;
            int a = boyut - 11 + i % 3, b = i / 3;
            Koy(m, sabit, a, b, koyu);
            Koy(m, sabit, b, a, koyu);
        }
    }

    private static int[] HizalamaKonumlari(int surum, int boyut)
    {
        if (surum == 1) return [];
        var adet = surum / 7 + 2;
        var adim = (surum * 8 + adet * 3 + 5) / (adet * 4 - 4) * 2;
        var sonuc = new int[adet];
        sonuc[0] = 6;
        for (int i = adet - 1, konum = boyut - 7; i >= 1; i--, konum -= adim) sonuc[i] = konum;
        return sonuc;
    }

    /// <summary>Seviye ve maskeyi (BCH ile) iki kopya hâlinde yazar; koyu modül de buradadır.</summary>
    private static void BicimBitleri(bool[,] m, bool[,] sabit, QrSeviye seviye, int maske)
    {
        var boyut = m.GetLength(0);
        var seviyeBiti = seviye switch { QrSeviye.L => 1, QrSeviye.M => 0, QrSeviye.Q => 3, _ => 2 };
        var veri = seviyeBiti << 3 | maske;
        var kalan = veri;
        for (var i = 0; i < 10; i++) kalan = (kalan << 1) ^ ((kalan >> 9) * 0x537);
        var bitler = (veri << 10 | kalan) ^ 0x5412;
        bool Bit(int i) => ((bitler >> i) & 1) != 0;

        for (var i = 0; i <= 5; i++) Koy(m, sabit, 8, i, Bit(i));
        Koy(m, sabit, 8, 7, Bit(6));
        Koy(m, sabit, 8, 8, Bit(7));
        Koy(m, sabit, 7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Koy(m, sabit, 14 - i, 8, Bit(i));

        for (var i = 0; i < 8; i++) Koy(m, sabit, boyut - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Koy(m, sabit, 8, boyut - 15 + i, Bit(i));
        Koy(m, sabit, 8, boyut - 8, true);
    }

    /// <summary>Kod sözcüklerini sağ alttan başlayıp iki sütunluk zikzakla sabit olmayan modüllere yazar.</summary>
    private static void Yerlestir(bool[,] m, bool[,] sabit, byte[] veri)
    {
        var boyut = m.GetLength(0);
        var i = 0;
        for (var sag = boyut - 1; sag >= 1; sag -= 2)
        {
            if (sag == 6) sag = 5;   // dikey zamanlama deseni
            for (var dikey = 0; dikey < boyut; dikey++)
                for (var j = 0; j < 2; j++)
                {
                    var x = sag - j;
                    var yukari = ((sag + 1) & 2) == 0;
                    var y = yukari ? boyut - 1 - dikey : dikey;
                    if (sabit[y, x] || i >= veri.Length * 8) continue;
                    m[y, x] = ((veri[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
        }
    }

    private static void MaskeUygula(bool[,] m, bool[,] sabit, int maske)
    {
        var boyut = m.GetLength(0);
        for (var y = 0; y < boyut; y++)
            for (var x = 0; x < boyut; x++)
            {
                var ters = maske switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (!sabit[y, x] && ters) m[y, x] = !m[y, x];
            }
    }

    /// <summary>Standarttaki dört ceza kuralı: uzun seriler, 2×2 bloklar, bulucuya benzeyen dizi, koyu oranı.</summary>
    private static int Ceza(bool[,] m)
    {
        var boyut = m.GetLength(0);
        var sonuc = 0;
        for (var yon = 0; yon < 2; yon++)
            for (var a = 0; a < boyut; a++)
            {
                var renk = false;
                var seri = 0;
                var gecmis = new int[7];
                for (var b = 0; b < boyut; b++)
                {
                    var modul = yon == 0 ? m[a, b] : m[b, a];
                    if (modul == renk)
                    {
                        seri++;
                        if (seri == 5) sonuc += 3;
                        else if (seri > 5) sonuc++;
                    }
                    else
                    {
                        GecmiseEkle(seri, gecmis, boyut);
                        if (!renk) sonuc += BulucuSay(gecmis) * 40;
                        renk = modul;
                        seri = 1;
                    }
                }
                if (renk)
                {
                    GecmiseEkle(seri, gecmis, boyut);
                    seri = 0;
                }
                GecmiseEkle(seri + boyut, gecmis, boyut);
                sonuc += BulucuSay(gecmis) * 40;
            }

        var koyu = 0;
        for (var y = 0; y < boyut; y++)
            for (var x = 0; x < boyut; x++)
            {
                if (m[y, x]) koyu++;
                if (x < boyut - 1 && y < boyut - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                    sonuc += 3;
            }
        var toplam = boyut * boyut;
        var k = (Math.Abs(koyu * 20 - toplam * 10) + toplam - 1) / toplam - 1;
        return sonuc + k * 10;
    }

    private static void GecmiseEkle(int seri, int[] gecmis, int boyut)
    {
        if (gecmis[0] == 0) seri += boyut;   // ilk seriye açık kenar eklenir
        Array.Copy(gecmis, 0, gecmis, 1, gecmis.Length - 1);
        gecmis[0] = seri;
    }

    /// <summary>1:1:3:1:1 (bulucu benzeri) dizisi, önünde ya da arkasında 4 birim açık alanla.</summary>
    private static int BulucuSay(int[] g)
    {
        var n = g[1];
        var cekirdek = n > 0 && g[2] == n && g[3] == n * 3 && g[4] == n && g[5] == n;
        return (cekirdek && g[0] >= n * 4 && g[6] >= n ? 1 : 0) + (cekirdek && g[6] >= n * 4 && g[0] >= n ? 1 : 0);
    }
}
