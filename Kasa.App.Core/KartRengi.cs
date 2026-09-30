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
/// "ISBANK" aynı sayılır. Eşleme iki turda yapılır: önce ayırt edici (uzun) banka anahtarları ("ziraat", "isbank" gibi),
/// sonra yalnız bunlardan hiçbiri eşleşmezse kısa tam sözcük anahtarları ("is", "teb", "halk", "vakif" — "visa" İş
/// Bankası sayılmaz). Böylece "Ziraat Bankkart İş" gibi bir ad, içinde "İş" sözcüğü geçse de Ziraat kalır. Çok sözcüklü
/// banka anahtarları ("yapikredi", "vakifbank", "denizbank", "isbank", "finansbank", "halkbank") ardışık iki sözcüğün
/// bitişiğinde de aranır, ama yalnız sözcük başından itibaren ("Yapı Kredi" → "yapi"+"kredi"; "İş Bankası" →
/// "is"+"bankasi" "isbank"ı baştan yakalar); ortada bir yerde geçmesi ("Paris Bank" → "parisbank") sayılmaz. Tanınmayan
/// banka kart kimliğine göre <see cref="Palet"/>'ten sabit bir renk alır: aynı kart her açılışta aynı rengi alır.
/// </summary>
public static class KartRengi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Bir eşleme kuralı: tek sözcük olarak tam eşitlikle aranır; <paramref name="CokSozcuklu"/> işaretliyse
    /// ardışık iki sözcüğün bitişiğinin başında da (sözcük sınırından itibaren) aranır.</summary>
    private sealed record Kural(string Anahtar, KartRenkAilesi Aile, bool CokSozcuklu = false);

    /// <summary>Ayırt edici (uzun) banka anahtarları: kısa anahtarlardan önce, tüm sözcükler taranarak aranır.</summary>
    private static readonly Kural[] UzunKurallar =
    [
        new("garanti", KartRenkAilesi.Yesil),
        new("akbank", KartRenkAilesi.Kirmizi),
        new("isbank", KartRenkAilesi.Mavi, CokSozcuklu: true),
        new("qnb", KartRenkAilesi.Mor),
        new("finansbank", KartRenkAilesi.Mor, CokSozcuklu: true),
        new("vakifbank", KartRenkAilesi.Sari, CokSozcuklu: true),
        new("vakiflar", KartRenkAilesi.Sari),
        new("yapikredi", KartRenkAilesi.Lacivert, CokSozcuklu: true),
        new("ziraat", KartRenkAilesi.Kirmizi),
        new("halkbank", KartRenkAilesi.Mavi, CokSozcuklu: true),
        new("denizbank", KartRenkAilesi.Mavi, CokSozcuklu: true),
        new("enpara", KartRenkAilesi.Mor),
    ];

    /// <summary>Kısa anahtarlar: uzun anahtarların hiçbiri eşleşmezse, yalnız tam sözcük olarak aranır.</summary>
    private static readonly Kural[] KisaKurallar =
    [
        new("is", KartRenkAilesi.Mavi),
        new("teb", KartRenkAilesi.Yesil),
        new("halk", KartRenkAilesi.Mavi),
        new("vakif", KartRenkAilesi.Sari),
    ];

    /// <summary>Tanınmayan bankaların renkleri (bankalara ayrılan ailelerden ayrı).</summary>
    public static IReadOnlyList<KartRenkAilesi> Palet { get; } =
        [KartRenkAilesi.Turuncu, KartRenkAilesi.Camgobegi, KartRenkAilesi.Kahve, KartRenkAilesi.Gri];

    /// <summary>Banka adına göre renk ailesi seçer; eşleşme yoksa kimliğe göre sabit palet rengi döner.</summary>
    public static KartRenkAilesi Sec(string? ad, int kimlik)
    {
        var sozcukler = Sozcukler(ad);
        return Bul(sozcukler, UzunKurallar) ?? Bul(sozcukler, KisaKurallar) ?? Palet[(int)((uint)kimlik % (uint)Palet.Count)];
    }

    /// <summary>Sözcükleri baştan tarar: her konumda önce tek sözcük tam eşitliğine, sonra (varsa bir sonraki sözcükle)
    /// çok sözcüklü anahtarların bitişiğinin sözcük başından itibaren eşleşmesine bakar; ilk bulunan aileyi döner.</summary>
    private static KartRenkAilesi? Bul(string[] sozcukler, Kural[] kurallar)
    {
        for (var i = 0; i < sozcukler.Length; i++)
        {
            foreach (var kural in kurallar)
                if (sozcukler[i] == kural.Anahtar)
                    return kural.Aile;
            if (i + 1 < sozcukler.Length)
            {
                var ikili = sozcukler[i] + sozcukler[i + 1];
                foreach (var kural in kurallar)
                    if (kural.CokSozcuklu && ikili.StartsWith(kural.Anahtar, StringComparison.Ordinal))
                        return kural.Aile;
            }
        }
        return null;
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
