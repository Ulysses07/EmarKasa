using System.Security.Cryptography;

namespace Kasa.Api.Auth;

/// <summary>
/// İki adımlı girişin kurtarma kodları: telefon kaybolursa kod yerine bir kez kullanılır.
/// <see cref="Adet"/> kod üretilir ("ABCDE-FGH23" biçiminde; karışan 0/O ve 1/I harfleri yok),
/// yalnız bir kez gösterilir, DB'de her biri ayrı PBKDF2 hash'iyle (';' ile ayrılmış) saklanır.
/// Kullanılan kodun hash'i listeden düşer.
/// </summary>
public static class KurtarmaKodlari
{
    public const int Adet = 10;
    public const int Uzunluk = 10;
    /// <summary>32 karakter: rastgele baytın alt 5 biti eşit dağılımla seçer.</summary>
    private const string Alfabe = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int Iterasyon = 10_000;
    private const int SaltBoyutu = 16;
    private const int HashBoyutu = 32;

    /// <summary>Yeni kodlar (gösterim biçiminde, "XXXXX-XXXXX").</summary>
    public static IReadOnlyList<string> Uret()
    {
        var kodlar = new List<string>(Adet);
        for (var i = 0; i < Adet; i++)
        {
            var bayt = RandomNumberGenerator.GetBytes(Uzunluk);
            var k = new string(bayt.Select(b => Alfabe[b & 31]).ToArray());
            kodlar.Add($"{k[..5]}-{k[5..]}");
        }
        return kodlar;
    }

    /// <summary>Kodların saklanacak hash listesi.</summary>
    public static string Hashle(IEnumerable<string> kodlar)
        => string.Join(';', kodlar.Select(k => HashTek(Normal(k) ?? throw new ArgumentException("Geçersiz kurtarma kodu.", nameof(kodlar)))));

    /// <summary>Kullanıcının yazdığı kod: büyük harfe çevrilir, boşluk ve tire atılır; biçim tutmuyorsa null.</summary>
    public static string? Normal(string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod)) return null;
        var s = new string(kod.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
        return s.Length == Uzunluk && s.All(c => Alfabe.Contains(c)) ? s : null;
    }

    /// <summary>Kalan (kullanılmamış) kod sayısı.</summary>
    public static int Kalan(string? kayit)
        => string.IsNullOrEmpty(kayit) ? 0 : kayit.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Kodu dener. Eşleşirse kodun düştüğü yeni listeyi (kod kalmadıysa null) <paramref name="yeniKayit"/>'ta
    /// verir ve true döner. Tüm hash'ler denenir (erken çıkış yok).
    /// </summary>
    public static bool Kullan(string? kayit, string? kod, out string? yeniKayit)
    {
        yeniKayit = kayit;
        var n = Normal(kod);
        if (n is null || string.IsNullOrEmpty(kayit)) return false;
        var hashler = kayit.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var bulunan = -1;
        for (var i = 0; i < hashler.Count; i++)
            if (HashDogrula(n, hashler[i]) && bulunan < 0) bulunan = i;
        if (bulunan < 0) return false;
        hashler.RemoveAt(bulunan);
        yeniKayit = hashler.Count == 0 ? null : string.Join(';', hashler);
        return true;
    }

    private static string HashTek(string normal)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBoyutu);
        var hash = Rfc2898DeriveBytes.Pbkdf2(normal, salt, Iterasyon, HashAlgorithmName.SHA256, HashBoyutu);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool HashDogrula(string normal, string kayitli)
    {
        var p = kayitli.Split('.');
        if (p.Length != 2) return false;
        try
        {
            var salt = Convert.FromBase64String(p[0]);
            var beklenen = Convert.FromBase64String(p[1]);
            var gelen = Rfc2898DeriveBytes.Pbkdf2(normal, salt, Iterasyon, HashAlgorithmName.SHA256, beklenen.Length);
            return CryptographicOperations.FixedTimeEquals(gelen, beklenen);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
