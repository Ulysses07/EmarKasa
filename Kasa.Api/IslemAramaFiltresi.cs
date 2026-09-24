using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

/// <summary>
/// İşlem listesinin ve "Excel'e aktar"ın gelişmiş süzgeci (hepsi isteğe bağlı; hiçbiri verilmezse
/// eski davranış aynen kalır): not içinde metin, gider tipi, kart ve tutar aralığı. Sorgu
/// parametrelerinden bağlanır (<c>notAra</c>, <c>tip</c>, <c>kartId</c>, <c>minTutar</c>, <c>maxTutar</c>).
/// Tip ve kart SQL'de, not ve tutar bellekte süzülür (Türkçe harf duyarsız arama; tutar SQLite'ta metin).
/// </summary>
public sealed class IslemAramaFiltresi
{
    /// <summary>Notta geçen metin (Türkçe büyük/küçük harf duyarsız).</summary>
    public string? NotAra { get; set; }
    /// <summary>Gider tipi adı: Cari, SabitGider, KrediKarti (büyük/küçük harf duyarsız).</summary>
    public string? Tip { get; set; }
    /// <summary>Yalnız bu karta bağlı harcamalar.</summary>
    public int? KartId { get; set; }
    /// <summary>En az tutar (dahil).</summary>
    public decimal? MinTutar { get; set; }
    /// <summary>En çok tutar (dahil).</summary>
    public decimal? MaxTutar { get; set; }

    private GiderTipi? TipDegeri
        => string.IsNullOrWhiteSpace(Tip) ? null
            : Enum.GetNames<GiderTipi>().FirstOrDefault(n => string.Equals(n, Tip.Trim(), StringComparison.OrdinalIgnoreCase)) is { } ad
                ? Enum.Parse<GiderTipi>(ad) : null;

    /// <summary>Süzgeç geçersizse Türkçe açıklama, değilse null.</summary>
    public string? Hata()
    {
        if (!string.IsNullOrWhiteSpace(Tip) && TipDegeri is null) return "Geçersiz gider tipi (Cari, SabitGider ya da KrediKarti).";
        if (MinTutar is < 0 || MaxTutar is < 0) return "Tutar aralığı negatif olamaz.";
        if (MinTutar is { } en && MaxTutar is { } ec && en > ec) return "En az tutar en çok tutardan büyük olamaz.";
        if (NotAra is { Length: > 200 }) return "Not araması en fazla 200 karakter olabilir.";
        return null;
    }

    /// <summary>Tip ve kart süzgecini sorguya ekler. Karta bağlı harcama hesapta K.K sayıldığı için "KrediKarti" onları da kapsar.</summary>
    public IQueryable<IslemEntity> SorguyaUygula(IQueryable<IslemEntity> q)
    {
        if (TipDegeri is { } t)
            q = t == GiderTipi.KrediKarti
                ? q.Where(i => i.Tip == GiderTipi.KrediKarti || i.KrediKartiId != null)
                : q.Where(i => i.Tip == t && i.KrediKartiId == null);
        if (KartId is { } k) q = q.Where(i => i.KrediKartiId == k);
        return q;
    }

    /// <summary>Bellekte süzülecek bir koşul (not, tutar) var mı: varsa sayfalama da bellekte yapılır.</summary>
    public bool BellekteVarMi() => !string.IsNullOrWhiteSpace(NotAra) || MinTutar is not null || MaxTutar is not null;

    public IEnumerable<IslemEntity> BellekteUygula(IEnumerable<IslemEntity> l)
    {
        if (!string.IsNullOrWhiteSpace(NotAra))
        {
            var aranan = NotAra.Trim();
            l = l.Where(i => Metin.Icerir(i.Not, aranan));
        }
        if (MinTutar is { } en) l = l.Where(i => i.TutarTl >= en);
        if (MaxTutar is { } ec) l = l.Where(i => i.TutarTl <= ec);
        return l;
    }
}
