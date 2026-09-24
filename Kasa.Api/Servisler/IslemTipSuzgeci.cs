using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Servisler;

/// <summary>
/// 22 · İşlem listesinde gider tipi süzgeci (rapordaki rakamdan İşlemler'e inişte). Tip motorun etkin
/// tipidir (<see cref="HesapMotoru.EtkinTip"/>): karta bağlı işlem, kayıtlı tipi ne olursa olsun K.K
/// sayılır. Böylece süzülen liste rapordaki rakamı oluşturan işlemlerle aynıdır. "Nakit" = K.K olmayan
/// (kasadan kendi tarihinde çıkan) işlemler: kasa dökümündeki Ortak gider böyle toplanır.
/// </summary>
public static class IslemTipSuzgeci
{
    public const string Cari = "Cari";
    public const string SabitGider = "SabitGider";
    public const string KrediKarti = "KrediKarti";
    public const string Nakit = "Nakit";

    /// <summary>Bilinen değerler. İşlem listesi ve CSV'de <c>tip</c> parametresini <see cref="IslemAramaFiltresi"/> bağlar
    /// (büyük/küçük harf duyarsız; bilinmeyen değer 400).</summary>
    public static readonly string[] Degerler = [Cari, SabitGider, KrediKarti, Nakit];

    public static IQueryable<IslemEntity> TipeGoreSuz(this IQueryable<IslemEntity> q, string? tip) => tip switch
    {
        Cari => q.Where(i => i.Tip == GiderTipi.Cari && i.KrediKartiId == null),
        SabitGider => q.Where(i => i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null),
        KrediKarti => q.Where(i => i.Tip == GiderTipi.KrediKarti || i.KrediKartiId != null),
        Nakit => q.Where(i => i.Tip != GiderTipi.KrediKarti && i.KrediKartiId == null),
        _ => q,
    };
}
