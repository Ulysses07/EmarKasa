using System.Security.Cryptography;

namespace Kasa.Api.Auth;

/// <summary>PBKDF2 (SHA256) tabanlı şifre hash'leme. Format: base64(salt).base64(hash).</summary>
public static class SifreHasher
{
    private const int SaltBoyutu = 16;
    private const int HashBoyutu = 32;
    private const int Iterasyon = 100_000;

    public static string Hashle(string sifre)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBoyutu);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(sifre, salt, Iterasyon, HashAlgorithmName.SHA256, HashBoyutu);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Dogrula(string sifre, string kayitliHash)
    {
        var parcalar = kayitliHash.Split('.');
        if (parcalar.Length != 2) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parcalar[0]);
            byte[] beklenen = Convert.FromBase64String(parcalar[1]);
            byte[] gelen = Rfc2898DeriveBytes.Pbkdf2(sifre, salt, Iterasyon, HashAlgorithmName.SHA256, beklenen.Length);
            return CryptographicOperations.FixedTimeEquals(gelen, beklenen);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
