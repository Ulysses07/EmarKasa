using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Denetim;

/// <summary>
/// Ay kilidi açılışlarının açık pencereleri. Kilit geçmişi (AyKilidiOlaylar) sırayla uygulanır: açılış (sınır geriye
/// çekilir) (yeni sınır, önceki sınır] aralığını o açılış olayına bağlar; kapatma (sınır ileri) (önceki, yeni] aralığını
/// yeniden kilitler ve pencerenin o kısmını kapatır. Kalan aralıklar, açılışla açılmış ve henüz yeniden kilitlenmemiş
/// tarihlerdir: o tarihlere düşen değişikliğin olayı açılış olayının kimliğini taşır (<see cref="DenetimOlayEntity.KilitAcmaOlayiId"/>).
/// Hiç kilitlenmemiş tarih ya da yeniden kilitlenmiş tarih pencereye düşmez. Kilit geçmişi birkaç düzine satırdır.
/// </summary>
internal sealed class DenetimKilitPenceresi
{
    private readonly List<(DateOnly Bas, DateOnly Son, int Olay)> _acik;
    private DenetimKilitPenceresi(List<(DateOnly, DateOnly, int)> acik) => _acik = acik;

    internal static readonly DenetimKilitPenceresi Bos = new([]);

    internal static DenetimKilitPenceresi Oku(KasaDbContext db) => Kur(db.AyKilidiOlaylar.AsNoTracking().OrderBy(o => o.Id)
        .Select(o => new { o.Id, o.OncekiSonTarih, o.YeniSonTarih }).AsEnumerable()
        .Select(o => (o.Id, o.OncekiSonTarih, o.YeniSonTarih)));

    internal static DenetimKilitPenceresi Kur(IEnumerable<(int Id, DateOnly? Onceki, DateOnly? Yeni)> olaylar)
    {
        var acik = new List<(DateOnly, DateOnly, int)>();
        foreach (var (id, onceki, yeni) in olaylar)
        {
            if (onceki is { } p && (yeni is not { } n1 || n1 < p))
                Uygula(acik, Sonraki(yeni), p, id);
            else if (yeni is { } n && (onceki is not { } p2 || n > p2))
                Uygula(acik, Sonraki(onceki), n, null);
        }
        return new(acik);
    }

    /// <summary>Tarihin düştüğü açık pencerenin açılış olayı; yoksa null.</summary>
    internal int? Bul(DateOnly tarih)
    {
        foreach (var (bas, son, olay) in _acik)
            if (bas <= tarih && tarih <= son)
                return olay;
        return null;
    }

    /// <summary>Tarihsiz ama bütün dönemleri etkileyen değişiklik (kanal/genel kasa açılışı) için en son açılış.</summary>
    internal int? EnSon => _acik.Count == 0 ? null : _acik.Max(a => a.Olay);

    /// <summary>[bas, son] aralığıyla kesişen açık pencerelerin açılış olayları (kapatmanın kapattıkları).</summary>
    internal IReadOnlyList<int> Kesisen(DateOnly bas, DateOnly son) =>
        _acik.Where(a => a.Bas <= son && bas <= a.Son).Select(a => a.Olay).Distinct().Order().ToList();

    internal static DateOnly Sonraki(DateOnly? sinir) => sinir is { } s ? s.AddDays(1) : DateOnly.MinValue;

    private static void Uygula(List<(DateOnly Bas, DateOnly Son, int Olay)> acik, DateOnly bas, DateOnly son, int? olay)
    {
        if (bas > son)
            return;
        var kalan = new List<(DateOnly, DateOnly, int)>();
        foreach (var a in acik)
        {
            if (a.Son < bas || a.Bas > son)
            { kalan.Add(a); continue; }
            if (a.Bas < bas)
                kalan.Add((a.Bas, bas.AddDays(-1), a.Olay));
            if (a.Son > son)
                kalan.Add((son.AddDays(1), a.Son, a.Olay));
        }
        if (olay is { } id)
            kalan.Add((bas, son, id));
        acik.Clear();
        acik.AddRange(kalan);
    }
}
