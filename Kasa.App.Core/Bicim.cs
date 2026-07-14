using System.Globalization;

namespace Kasa.App.Core;

/// <summary>tr-TR para biçimi + kanal renkleri (web/src/format.ts + theme.ts aynası).</summary>
public static class Bicim
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    public static string KanalRengi(string kanal) => kanal switch
    {
        "MEZAT" => "#C98A12",
        "PERAKENDE" => "#3572C1",
        "TOPTAN" => "#7E5BBF",
        _ => "#7A828E",
    };
}
