using System.Globalization;

namespace Kasa.Api;

/// <summary>Türkçe sıralama/arama ve hata mesajı yardımcıları.</summary>
public static class Metin
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Türkçe alfabetik sıralama (Ç, Ğ, İ, Ö, Ş, Ü yerinde).</summary>
    public static readonly StringComparer Sirala = StringComparer.Create(Tr, CompareOptions.None);

    /// <summary>Türkçe büyük/küçük harf duyarsız eşitlik (I/ı, İ/i doğru eşlenir).</summary>
    public static readonly StringComparer EsitBuyukKucukDuyarsiz = StringComparer.Create(Tr, CompareOptions.IgnoreCase);

    /// <summary><paramref name="aranan"/> metnin içinde geçiyor mu (Türkçe, büyük/küçük harf duyarsız).</summary>
    public static bool Icerir(string? metin, string aranan)
        => metin is not null && Tr.CompareInfo.IndexOf(metin, aranan, CompareOptions.IgnoreCase) >= 0;

    /// <summary>Kullanıcı girdisini hata mesajına koymadan önce kısaltır.</summary>
    public static string Kisalt(string? s, int en = 60)
    {
        s ??= "";
        return s.Length <= en ? s : s[..en] + "…";
    }
}
