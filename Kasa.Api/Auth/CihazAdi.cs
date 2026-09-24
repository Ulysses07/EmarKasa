namespace Kasa.Api.Auth;

/// <summary>
/// Giriş yapan cihazın adı: Windows uygulaması <c>X-Kasa-Cihaz</c> başlığında bilgisayar adını
/// (ör. "EMAR-LAPTOP", URL kodlu) gönderir. İstemci beyanıdır; doğrulanmaz, yalnız gösterilir.
/// Kontrol karakterleri atılır, en fazla <see cref="EnCok"/> karakter tutulur.
/// </summary>
public static class CihazAdi
{
    public const string Baslik = "X-Kasa-Cihaz";
    public const int EnCok = 64;

    public static string? Oku(HttpRequest istek) => Temizle(istek.Headers[Baslik].FirstOrDefault());

    public static string? Temizle(string? ham)
    {
        if (string.IsNullOrWhiteSpace(ham)) return null;
        string metin;
        try { metin = Uri.UnescapeDataString(ham); }
        catch (Exception) { metin = ham; }
        var temiz = new string(metin.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        temiz = string.Join(' ', temiz.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (temiz.Length == 0) return null;
        return temiz.Length > EnCok ? temiz[..EnCok] : temiz;
    }
}
