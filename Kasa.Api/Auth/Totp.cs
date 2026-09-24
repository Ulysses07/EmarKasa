using System.Security.Cryptography;
using System.Text;

namespace Kasa.Api.Auth;

/// <summary>
/// Zaman tabanlı tek kullanımlık kod (TOTP, RFC 6238): HMAC-SHA1, 30 saniyelik adım, 6 hane —
/// Google Authenticator ve benzerlerinin varsayılanı. Doğrulama bir önceki ve bir sonraki adımı
/// da kabul eder (±30 sn saat farkı) ve daha önce kullanılmış adımı reddeder (aynı kod iki kez
/// kullanılamaz).
/// </summary>
public static class Totp
{
    public const int AdimSaniye = 30;
    public const int Hane = 6;
    public const int SirBayt = 20;
    public const string Yayinci = "Emar Kasa";

    public static string SirUret() => Base32.Yaz(RandomNumberGenerator.GetBytes(SirBayt));

    public static long AdimNo(DateTimeOffset zaman) => zaman.ToUnixTimeSeconds() / AdimSaniye;

    /// <summary>RFC 4226 HOTP değeri (dinamik kesme), <paramref name="hane"/> haneli ve sıfır dolgulu.</summary>
    public static string Kod(byte[] sir, long adim, int hane = Hane)
    {
        Span<byte> sayac = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(sayac, adim);
        Span<byte> ozet = stackalloc byte[20];
        HMACSHA1.HashData(sir, sayac, ozet);
        var ofset = ozet[^1] & 0x0F;
        var ikili = ((ozet[ofset] & 0x7F) << 24) | (ozet[ofset + 1] << 16) | (ozet[ofset + 2] << 8) | ozet[ofset + 3];
        var mod = (int)Math.Pow(10, hane);
        return (ikili % mod).ToString(new string('0', hane), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Kullanıcının yazdığı kod: boşluklar atılır; tam 6 rakam değilse null.</summary>
    public static string? Normallestir(string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod)) return null;
        var s = new string(kod.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return s.Length == Hane && s.All(char.IsAsciiDigit) ? s : null;
    }

    /// <summary>
    /// Kodu doğrular; eşleşen zaman adımını döner (eşleşme yoksa null). <paramref name="sonKullanilan"/>
    /// ve öncesindeki adımlar kabul edilmez (tekrar oynatma koruması).
    /// </summary>
    public static long? Dogrula(string? sirBase32, string? kod, DateTimeOffset simdi, long? sonKullanilan = null)
    {
        var k = Normallestir(kod);
        if (k is null || Base32.Oku(sirBase32) is not { Length: > 0 } sir) return null;
        var an = AdimNo(simdi);
        long? bulunan = null;
        // Üç adımın hepsi hesaplanır (erken çıkış yok): süre koddan bağımsız kalsın.
        for (var d = -1; d <= 1; d++)
        {
            var adim = an + d;
            var esit = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Kod(sir, adim)), Encoding.ASCII.GetBytes(k));
            if (esit && (sonKullanilan is not { } s || adim > s) && bulunan is null) bulunan = adim;
        }
        return bulunan;
    }

    /// <summary>
    /// Kimlik doğrulayıcı uygulamaya elle ya da bağlantıyla eklemek için otpauth:// adresi
    /// (Key URI Format): yayıncı "Emar Kasa", hesap kullanıcı adı.
    /// </summary>
    public static string Adres(string sirBase32, string hesap)
    {
        var yayinci = Uri.EscapeDataString(Yayinci);
        return $"otpauth://totp/{yayinci}:{Uri.EscapeDataString(hesap)}?secret={sirBase32}&issuer={yayinci}&algorithm=SHA1&digits={Hane}&period={AdimSaniye}";
    }
}

/// <summary>RFC 4648 base32 (A–Z, 2–7), dolgusuz yazar; okurken boşluk, tire ve dolgu yok sayılır.</summary>
public static class Base32
{
    private const string Alfabe = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Yaz(ReadOnlySpan<byte> veri)
    {
        var sb = new StringBuilder((veri.Length * 8 + 4) / 5);
        int tampon = 0, bit = 0;
        foreach (var b in veri)
        {
            tampon = (tampon << 8) | b;
            bit += 8;
            while (bit >= 5)
            {
                sb.Append(Alfabe[(tampon >> (bit - 5)) & 31]);
                bit -= 5;
            }
        }
        if (bit > 0) sb.Append(Alfabe[(tampon << (5 - bit)) & 31]);
        return sb.ToString();
    }

    /// <summary>Geçersiz karakter varsa null.</summary>
    public static byte[]? Oku(string? metin)
    {
        if (metin is null) return null;
        var sonuc = new List<byte>(metin.Length * 5 / 8);
        int tampon = 0, bit = 0;
        foreach (var c0 in metin)
        {
            if (c0 is ' ' or '-' or '=') continue;
            var deger = Alfabe.IndexOf(char.ToUpperInvariant(c0));
            if (deger < 0) return null;
            tampon = (tampon << 5) | deger;
            bit += 5;
            if (bit >= 8)
            {
                sonuc.Add((byte)((tampon >> (bit - 8)) & 0xFF));
                bit -= 8;
            }
        }
        return sonuc.ToArray();
    }
}
