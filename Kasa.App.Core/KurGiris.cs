using System.Globalization;

namespace Kasa.App.Core;

/// <summary>Kur/endeks giriş kutusunun ayrıştırma sonucu. Boş giriş "değer yok" (null) demektir.</summary>
public readonly record struct KurGirisSonucu(bool Gecerli, decimal? Deger, string? Hata);

/// <summary>
/// Kur ve TÜFE endeksi girişini Türkçe kurallarla ayrıştırır (saf fonksiyon). <see cref="ParaGiris"/> ile
/// aynı gruplama kuralları; farklar: en fazla 4 ondalık, sıfır geçersiz, boş = değer yok (null; asla 0 değil).
/// <list type="bullet">
/// <item>Virgül ondalık ayırıcıdır: "41,2345" → 41,2345; "4.321,5" → 4321,5.</item>
/// <item>Yalnız nokta varsa ve her grup tam 3 haneyse nokta binliktir: "4.321" → 4321.</item>
/// <item>Tek nokta ve ardından 1, 2 ya da 4 hane ondalıktır: "41.25" → 41,25. Belirsiz olmasın diye
/// 3 haneli tek nokta binlik sayılır; 3 ondalık için virgül kullanın.</item>
/// </list>
/// </summary>
public static class KurGiris
{
    public const int EnFazlaOndalik = 4;
    public const decimal EnBuyuk = 10_000_000m;

    public const string HataGecersiz = "Geçersiz değer. Örnek: 41,2345 veya 4.321,50";
    public const string HataSifir = "Değer sıfırdan büyük olmalı.";
    public const string HataOndalik = "En fazla 4 ondalık hane girilebilir.";
    public const string HataCokBuyuk = "Değer çok büyük.";

    public static KurGirisSonucu Ayristir(string? metin)
    {
        var s = new string((metin ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace("₺", "");
        if (s.EndsWith("TL", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
        if (s.Length == 0) return new(true, null, null);
        if (s.StartsWith('-') || s.StartsWith('−')) return new(false, null, HataSifir);
        foreach (var c in s)
            if (!(c is >= '0' and <= '9' || c == '.' || c == ','))
                return new(false, null, HataGecersiz);

        string tam, ondalik;
        var virgul = s.IndexOf(',');
        if (virgul >= 0)
        {
            if (s.IndexOf(',', virgul + 1) >= 0) return new(false, null, HataGecersiz);
            tam = s[..virgul];
            ondalik = s[(virgul + 1)..];
            if (ondalik.Contains('.')) return new(false, null, HataGecersiz);
            if (tam.Contains('.'))
            {
                var g = tam.Split('.');
                if (g[0].Length is < 1 or > 3 || g.Skip(1).Any(x => x.Length != 3)) return new(false, null, HataGecersiz);
                tam = string.Concat(g);
            }
        }
        else if (s.Contains('.'))
        {
            var g = s.Split('.');
            if (g.Length >= 2 && g[0].Length is >= 1 and <= 3 && g.Skip(1).All(x => x.Length == 3))
            {
                tam = string.Concat(g);
                ondalik = "";
            }
            else if (g.Length == 2 && g[1].Length is >= 1 and <= EnFazlaOndalik and not 3)
            {
                tam = g[0];
                ondalik = g[1];
            }
            else if (g.Length == 2 && g[1].Length > EnFazlaOndalik)
                return new(false, null, HataOndalik);
            else
                return new(false, null, HataGecersiz);
        }
        else
        {
            tam = s;
            ondalik = "";
        }

        if (ondalik.Length > EnFazlaOndalik) return new(false, null, HataOndalik);
        if (tam.Length == 0 && ondalik.Length == 0) return new(false, null, HataGecersiz);
        if (tam.TrimStart('0').Length > 9) return new(false, null, HataCokBuyuk);
        var normal = (tam.Length == 0 ? "0" : tam) + (ondalik.Length > 0 ? "." + ondalik : "");
        if (!decimal.TryParse(normal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            return new(false, null, HataGecersiz);
        if (d <= 0m) return new(false, null, HataSifir);
        if (d > EnBuyuk) return new(false, null, HataCokBuyuk);
        return new(true, d, null);
    }

    /// <summary>Değeri giriş kutusu metnine çevirir (null = boş; en fazla 4 ondalık, binlik ayırıcısız).</summary>
    public static string Bicimle(decimal? d) => d is { } v ? v.ToString("0.####", Kultur.Turkce) : "";
}
