using System.Globalization;

namespace Kasa.App.Core;

/// <summary>tr-TR para biçimi + kanal renkleri.</summary>
public static class Bicim
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    /// <summary>Dönem seçici etiketi: "13 Tem – 19 Tem"; yıllı "13 Tem 2026 – 19 Tem 2026" (gelir formu dönem
    /// toplamını yerine koyduğundan farklı yılların aynı haftaları karışmasın).</summary>
    public static string Donem(Kasa.ApiClient.DonemDto d, bool yilli = false) => Aralik(d.Start, d.End, yilli);

    /// <summary>Tarih aralığı etiketi; dönem seçicisiyle aynı biçim ("06 Tem – 12 Tem").</summary>
    public static string Aralik(DateOnly bas, DateOnly bit, bool yilli = false)
    {
        var bicim = yilli ? "dd MMM yyyy" : "dd MMM";
        return $"{bas.ToString(bicim, Tr)} – {bit.ToString(bicim, Tr)}";
    }

    /// <summary>Dosya boyutu: "512 B", "12,3 KB", "150 MB".</summary>
    /// <summary>Gigabayt, bir ondalık (ör. "12,5 GB"); disk alanı ve yedek boyutu gösterimi.</summary>
    public static string Gb(long bayt) => (bayt / (1024d * 1024 * 1024)).ToString("0.0", Tr) + " GB";

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
