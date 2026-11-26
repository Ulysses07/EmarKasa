using System.Globalization;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Hareket penceresinin tablo hücreleri (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2): tür · kanal, doldurmanın kaynağı ve işaretli tutar.
public partial class EkstreSatirEditor
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"Gider · Genel", "Gider · PERAKENDE, MEZAT"; türü seçilmemiş satırda "—".</summary>
    public string TurKanalMetni => IslemTuru is not { } tur ? "—" : DagilimMetni() is { Length: > 0 } dagilim ? $"{tur.Ad} · {dagilim}" : tur.Ad;

    private string DagilimMetni() => DagilimTuru?.Kod switch
    {
        DagilimBicimleri.Genel => "Genel",
        DagilimBicimleri.Esit or DagilimBicimleri.Ozel => string.Join(", ", Paylar.Select(p => p.Kanal?.Ad).OfType<string>()),
        _ => ""
    };

    /// <summary>Satırı neyin doldurduğu: "hafıza", "yapay zekâ", "kural", "ERP12"; elle ya da okuyucunun önerisiyle kalan satırda boş.</summary>
    public string KaynakEtiketi => Kapali ? ""
        : _erpden ? "ERP12"
        : _hafizadan && HafizaVar ? "hafıza"
        : _hafizadan && YapayZekaVar ? "yapay zekâ"
        : Oneri is { Durum: "Oneri" } o && IslemTuru?.Kod == o.IslemTuru ? "kural"
        : "";

    /// <summary>Tutar yönüyle: giriş "+1.500,00", çıkış "−553,15", yönü belirsiz işaretsiz.</summary>
    public string TutarGosterimi => EtkinTutar is not { } tutar ? ""
        : EtkinYon switch { "Giris" => "+", "Cikis" => "−", _ => "" } + tutar.ToString("N2", Tr);
}
