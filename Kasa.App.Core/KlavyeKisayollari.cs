namespace Kasa.App.Core;

/// <summary>İşlem formundaki klavye kısayollarının eylemi.</summary>
public enum KisayolEylemi { Yok, Kaydet, YeniSatir, Vazgec, Kanal1, Kanal2, Kanal3, Kanal4 }

/// <summary>Bir kısayol: tuş adı (platform tuş adı normalleştirilmiş), Ctrl/Alt ve eylem.</summary>
public readonly record struct Kisayol(string Tus, bool Ctrl, bool Alt, KisayolEylemi Eylem, string Aciklama);

/// <summary>
/// İşlem sayfasının klavye kısayolları (Windows). Eşleme burada, platformdan bağımsız ve testli;
/// sayfa yalnız tuşu okuyup <see cref="Coz"/>'a verir.
/// <list type="bullet">
/// <item>Enter / Ctrl+S: kaydet (Enter form alanlarında; cari kutusunda önce öneriyi seçer).</item>
/// <item>Ctrl+N: yeni satır (formu temizler, cariye odaklanır).</item>
/// <item>Esc: bekleyen uyarıyı/onayı kapatır, yoksa düzenlemeden çıkar.</item>
/// <item>Alt+1..4: gider kanalı çiplerinden 1..4'üncüyü seçer.</item>
/// <item>Tarih kutusunda B: bugün, D: dün.</item>
/// </list>
/// </summary>
public static class KlavyeKisayollari
{
    public static IReadOnlyList<Kisayol> Liste { get; } =
    [
        new("Enter", false, false, KisayolEylemi.Kaydet, "Kaydet"),
        new("S", true, false, KisayolEylemi.Kaydet, "Kaydet"),
        new("N", true, false, KisayolEylemi.YeniSatir, "Yeni satır"),
        new("Escape", false, false, KisayolEylemi.Vazgec, "Düzenlemeden çık"),
        new("1", false, true, KisayolEylemi.Kanal1, "1. kanal"),
        new("2", false, true, KisayolEylemi.Kanal2, "2. kanal"),
        new("3", false, true, KisayolEylemi.Kanal3, "3. kanal"),
        new("4", false, true, KisayolEylemi.Kanal4, "4. kanal"),
    ];

    /// <summary>Formun altında gösterilen kısa yardım metni.</summary>
    public const string Yardim = "Enter / Ctrl+S kaydet · Ctrl+N yeni · Esc vazgeç · Alt+1…4 kanal · Tarihte B bugün, D dün";

    /// <summary>
    /// Platform tuş adını normalleştirir: "Number1"/"NumberPad1"/"D1" → "1", "Esc" → "Escape",
    /// "Return" → "Enter", harfler büyük.
    /// </summary>
    public static string TusAdi(string? tus)
    {
        var t = (tus ?? "").Trim();
        if (t.Length == 0) return "";
        foreach (var on in new[] { "NumberPad", "Number", "NumPad", "Numpad" })
            if (t.StartsWith(on, StringComparison.OrdinalIgnoreCase) && t.Length == on.Length + 1 && char.IsAsciiDigit(t[^1]))
                return t[^1].ToString();
        if (t.Length == 2 && (t[0] is 'D' or 'd') && char.IsAsciiDigit(t[1])) return t[1].ToString();
        if (string.Equals(t, "Esc", StringComparison.OrdinalIgnoreCase)) return "Escape";
        if (string.Equals(t, "Return", StringComparison.OrdinalIgnoreCase)) return "Enter";
        return t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>Tuş + değiştiriciler → eylem (eşleşme yoksa <see cref="KisayolEylemi.Yok"/>). Shift ile hiçbir kısayol çalışmaz.</summary>
    public static KisayolEylemi Coz(string? tus, bool ctrl, bool alt, bool shift = false)
    {
        if (shift) return KisayolEylemi.Yok;
        var ad = TusAdi(tus);
        foreach (var k in Liste)
            if (string.Equals(k.Tus, ad, StringComparison.OrdinalIgnoreCase) && k.Ctrl == ctrl && k.Alt == alt)
                return k.Eylem;
        return KisayolEylemi.Yok;
    }

    /// <summary>Kanal kısayolunun 0 tabanlı çip sırası; kanal kısayolu değilse null.</summary>
    public static int? KanalSirasi(KisayolEylemi e)
        => e is >= KisayolEylemi.Kanal1 and <= KisayolEylemi.Kanal4 ? e - KisayolEylemi.Kanal1 : null;

    /// <summary>Tarih kutusunda basılan tuşun tarihi: B bugün, D dün (Türkçe; büyük/küçük harf fark etmez); diğerleri null.</summary>
    public static DateOnly? TarihTusu(string? tus, DateOnly bugun) => TusAdi(tus) switch
    {
        "B" => bugun,
        "D" => bugun.AddDays(-1),
        _ => null,
    };
}
