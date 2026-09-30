using System.Globalization;
using System.Text;

namespace Kasa.App.Core;

/// <summary>Kart kutusunun renk ailesi; her aile Colors.xaml'da Zemin, Kenar ve Yazi anahtarıyla tanımlıdır.</summary>
public enum KartRenkAilesi { Yesil, Kirmizi, Mavi, Mor, Sari, Lacivert, Turuncu, Camgobegi, Kahve, Gri }

/// <summary>Renk ailesinin parçası: açık zemin, kenar (ve doluluk çubuğunun izi), koyu yazı (ve çubuğun dolgusu).</summary>
public enum KartRenkParcasi { Zemin, Kenar, Yazi }

/// <summary>
/// Kart kutusunun rengi kart adında geçen bankadan seçilir (tasarım 2026-09-30 §2 Renk); veritabanında renk alanı yoktur.
/// Ad tr-TR küçük harfe çevrilir, aksanlar atılır (ş→s, ğ→g, ü→u, ö→o, ç→c, ı→i) ve sözcüklere bölünür: "İş", "is" ve
/// "ISBANK" aynı sayılır. Kısa anahtarlar ("is", "teb", "halk", "vakif") yalnız tam sözcük olarak eşleşir ("visa" İş Bankası
/// sayılmaz); uzun anahtarlar sözcüklerin bitişik yazımında aranır ("Yapı Kredi" → "yapikredi"). Tanınmayan banka kart
/// kimliğine göre <see cref="Palet"/>'ten sabit bir renk alır: aynı kart her açılışta aynı rengi alır.
/// </summary>
public static class KartRengi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private sealed record Kural(string Anahtar, KartRenkAilesi Aile, bool TamSozcuk);

    private static readonly Kural[] Kurallar =
    [
        new("garanti", KartRenkAilesi.Yesil, false),
        new("akbank", KartRenkAilesi.Kirmizi, false),
        new("isbank", KartRenkAilesi.Mavi, false),
        new("is", KartRenkAilesi.Mavi, true),
        new("qnb", KartRenkAilesi.Mor, false),
        new("finansbank", KartRenkAilesi.Mor, false),
        new("vakifbank", KartRenkAilesi.Sari, false),
        new("vakif", KartRenkAilesi.Sari, true),
        new("yapikredi", KartRenkAilesi.Lacivert, false),
        new("ziraat", KartRenkAilesi.Kirmizi, false),
        new("halkbank", KartRenkAilesi.Mavi, false),
        new("halk", KartRenkAilesi.Mavi, true),
        new("denizbank", KartRenkAilesi.Mavi, false),
        new("enpara", KartRenkAilesi.Mor, false),
        new("teb", KartRenkAilesi.Yesil, true),
    ];

    /// <summary>Tanınmayan bankaların renkleri (bankalara ayrılan ailelerden ayrı).</summary>
    public static IReadOnlyList<KartRenkAilesi> Palet { get; } =
        [KartRenkAilesi.Turuncu, KartRenkAilesi.Camgobegi, KartRenkAilesi.Kahve, KartRenkAilesi.Gri];

    public static KartRenkAilesi Sec(string? ad, int kimlik)
    {
        var sozcukler = Sozcukler(ad);
        var bitisik = string.Concat(sozcukler);
        foreach (var kural in Kurallar)
            if (kural.TamSozcuk ? sozcukler.Contains(kural.Anahtar) : bitisik.Contains(kural.Anahtar, StringComparison.Ordinal))
                return kural.Aile;
        return Palet[(int)((uint)kimlik % (uint)Palet.Count)];
    }

    /// <summary>Colors.xaml anahtarı: "Kart" + aile + parça (ör. KartYesilZemin).</summary>
    public static string Anahtar(KartRenkAilesi aile, KartRenkParcasi parca) => $"Kart{aile}{parca}";

    private static string[] Sozcukler(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad))
            return [];
        var ayrik = ad.ToLower(Tr).Normalize(NormalizationForm.FormD);
        var sade = new StringBuilder(ayrik.Length);
        foreach (var c in ayrik)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sade.Append(c == 'ı' ? 'i' : char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sade.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
