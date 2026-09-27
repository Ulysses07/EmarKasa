using System.Globalization;

namespace Kasa.App.Core;

/// <summary>tr-TR para biçimi + kanal renkleri (web/src/format.ts + theme.ts aynası).</summary>
public static class Bicim
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    /// <summary>Dönem seçici etiketi: "13 Tem – 19 Tem"; yıllı "13 Tem 2026 – 19 Tem 2026" (gelir formu dönem
    /// toplamını yerine koyduğundan farklı yılların aynı haftaları karışmasın).</summary>
    public static string Donem(Kasa.ApiClient.DonemDto d, bool yilli = false)
    {
        var bicim = yilli ? "dd MMM yyyy" : "dd MMM";
        return $"{d.Start.ToString(bicim, Tr)} – {d.End.ToString(bicim, Tr)}";
    }

    /// <summary>Dosya boyutu: "512 B", "12,3 KB", "150 MB".</summary>
    public static string Boyut(long bayt) => bayt switch
    {
        < 1024 => $"{bayt} B",
        < 1024 * 1024 => (bayt / 1024d).ToString("0.#", Tr) + " KB",
        _ => (bayt / (1024d * 1024)).ToString("0.#", Tr) + " MB",
    };

    public static string KanalRengi(string kanal) => kanal switch
    {
        "MEZAT" => "#C98A12",
        "PERAKENDE" => "#3572C1",
        "TOPTAN" => "#7E5BBF",
        _ => "#7A828E",
    };
}
