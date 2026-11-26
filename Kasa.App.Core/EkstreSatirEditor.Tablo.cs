using System.Globalization;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Hareket penceresinin tablo hücreleri (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2): tür · kanal, doldurmanın kaynağı ve işaretli tutar.
public partial class EkstreSatirEditor
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"Gider · Genel", "Gider · PERAKENDE, MEZAT"; türü seçilmemiş satırda "—".</summary>
    public string TurKanalMetni => Kayitli ? "Kaydedildi" : YokSayildi ? "Yok sayıldı"
        : IslemTuru is not { } tur ? "—" : DagilimMetni() is { Length: > 0 } dagilim ? $"{tur.Ad} · {dagilim}" : tur.Ad;

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

    private string? _aramaMetni;
    /// <summary>Aramanın karşılaştırdığı metin (açıklama ve PDF açıklaması, sadeleşmiş); açıklama değişince yeniden hesaplanır.</summary>
    internal string AramaMetni => _aramaMetni ??= Sade(Aciklama) + "\n" + Sade(Kaynak.Aciklama);
    private void AramaMetniniUnut() => _aramaMetni = null;

    /// <summary>Karşılaştırma biçimi: Türkçe i/ı ve aksanlar sadeleşir, büyük harf.</summary>
    internal static string Sade(string metin) => new string(metin.Replace('ı', 'i').Replace('İ', 'I').Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();

    /// <summary>Tablo hücrelerini yeniden okutur: doldurmanın kaynağı (hafıza, yapay zekâ, ERP12) alanlardan sonra belli olur, hücre bildirimleri
    /// o anda eski değeri verir.</summary>
    public void GosterimiYenile()
    {
        foreach (var ad in new[] { nameof(KontrolNedeni), nameof(TurKanalMetni), nameof(KaynakEtiketi), nameof(TutarGosterimi) })
            OnPropertyChanged(ad);
    }

    /// <summary>Tarih hücresi ("27.09"): kapanan satırda kaydedilen tarih, bekleyende düzenlenen tarih; okunamayan tarihte boş.</summary>
    public string TarihGosterimi => Kayit is { } kayit ? kayit.Veri.Tarih.ToString("dd.MM", Tr)
        : DateOnly.TryParseExact(TarihMetni.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih) ? tarih.ToString("dd.MM", Tr) : "";

    /// <summary>Tutar yönüyle: giriş "+1.500,00", çıkış "−553,15", yönü belirsiz işaretsiz.</summary>
    public string TutarGosterimi => EtkinTutar is not { } tutar ? ""
        : EtkinYon switch { "Giris" => "+", "Cikis" => "−", _ => "" } + tutar.ToString("N2", Tr);
}
