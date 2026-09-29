using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Seçili dönem + kanalın kayıtlı gelir satırları: toplam, satır sayısı, salt okunur mu. Surum (contract-6): tek normal satırın
/// sürümü, satır yoksa 0; kayıtla gönderilir, satır arada başka oturumda değiştiyse (ya da eklendiyse) sunucu 409 verir.</summary>
public readonly record struct GelirSecimSonucu(decimal Toplam, int Sayi, bool SaltOkunur, int Surum = 0);

/// <summary>Web ui-core.js incomeSelection / currentPeriod aynası. PUT /api/gelenler dönem+kanal toplamını
/// yerine koyduğu için form önce bu seçimle mevcut toplamı gösterir.</summary>
public static class GelirSecimi
{
    public static GelirSecimSonucu Hesapla(IEnumerable<GelenDto> satirlar, KanalDto? kanal)
    {
        var kanalAdi = SqliteAdi(kanal?.Ad);
        var secilen = satirlar.Where(s => s.KanalId is { } id
            ? kanal is { Id: > 0 } && id == kanal.Id
            : kanalAdi.Length > 0 && SqliteAdi(s.Kanal) == kanalAdi).ToList();
        var saltOkunur = secilen.Count > 1 || secilen.Any(s => s.EskiYinelenenGrup);
        return new(secilen.Sum(s => s.TutarTl), secilen.Count, saltOkunur, secilen.Count == 1 && !saltOkunur ? secilen[0].Surum : 0);
    }

    /// <summary>SQLite NOCASE yalnız ASCII A-Z'yi küçültür; Türkçe İ/ı ayrı kalır.</summary>
    private static string SqliteAdi(string? ad) => string.Create((ad ?? "").Length, ad ?? "", (hedef, kaynak) =>
    {
        for (var i = 0; i < kaynak.Length; i++) hedef[i] = kaynak[i] is >= 'A' and <= 'Z' ? (char)(kaynak[i] + 32) : kaynak[i];
    });

    /// <summary>Bugünü içeren dönem; yoksa başlangıcı bugün ya da önce olan en yeni dönem; o da yoksa ilk dönem.</summary>
    public static DonemDto? VarsayilanDonem(IEnumerable<DonemDto> donemler, DateOnly bugun)
    {
        var liste = donemler.ToList();
        return liste.FirstOrDefault(d => d.Start <= bugun && bugun <= d.End)
            ?? liste.Where(d => d.Start <= bugun).MaxBy(d => d.Start)
            ?? liste.MinBy(d => d.Start);
    }
}
