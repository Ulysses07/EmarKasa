namespace Kasa.Api;

/// <summary>Satırda yazan, bakiye olmayan bir tutar. <paramref name="Yon"/>: yazıdan okunan yön (+1 giriş, -1 çıkış, 0 yok);
/// <paramref name="Ek"/>: yön B/A ekinden (işaretten değil).</summary>
internal readonly record struct BakiyeTutari(decimal Tutar, int Yon, bool Ek);

/// <summary>Belge sırasındaki bir bakiye: hareket satırı ya da devir satırı (<paramref name="Satir"/> null).</summary>
/// <param name="Bakiye">Satırın bakiye adayı; yoksa null (zincir o satırda kopar).</param>
/// <param name="BakiyeKolonu">Bakiye, tablo başlığındaki "Bakiye" kolonundan okundu.</param>
internal sealed record BakiyeDugumu(int? Satir, decimal? Bakiye, IReadOnlyList<BakiyeTutari> Tutarlar, bool BakiyeKolonu, DateOnly? Tarih);

/// <summary>Satırın komşusuyla tutan bağı: satırda yazan tutar ve bakiyenin arttığı (giriş) ya da azaldığı bilgisi.</summary>
internal sealed record BakiyeBagi(BakiyeTutari Yazilan, bool Giris);

/// <param name="Baglar">Satır → tutan bağ.</param>
/// <param name="YalnizTutar">Kendi bağı olmayan ama bakiyesi komşusunca doğrulanan satırın tek tutarı (yön bakiyeden bilinmez).</param>
/// <param name="Kopuk">Komşusunun bakiyesi bilindiği hâlde tutmayan satırlar.</param>
internal sealed record BakiyeCozumu(IReadOnlyDictionary<int, BakiyeBagi> Baglar, IReadOnlyDictionary<int, BakiyeTutari> YalnizTutar, IReadOnlyList<int> Kopuk)
{
    public static readonly BakiyeCozumu Bos = new(new Dictionary<int, BakiyeBagi>(), new Dictionary<int, BakiyeTutari>(), []);
}

/// <summary>
/// Banka hareketlerinde tutar ve yönü bakiyeden doğrular (docs/specs/2026-11-25-pdf-ekrani.md §3). Bir bağ, komşu satırların bakiye
/// farkı satırda yazan tutarlardan birine kuruşu kuruşuna eşitse tutar; satırda yazmayan tutar farktan üretilmez.
/// </summary>
internal static class EkstreBakiyeZinciri
{
    public static BakiyeCozumu Coz(IReadOnlyList<BakiyeDugumu> dugumler)
    {
        // Eskiden yeniye sırada satırın önceki bakiyesi bir üst, yeniden eskiye sırada bir alt satırdadır.
        var artan = Baglar(dugumler, -1);
        var azalan = Baglar(dugumler, +1);
        var adim = artan.Count > azalan.Count ? -1 : azalan.Count > artan.Count ? +1 : artan.Count == 0 ? 0 : TarihSirasi(dugumler);
        if (adim == 0)
            return BakiyeCozumu.Bos;
        var baglar = adim < 0 ? artan : azalan;
        var olasi = Enumerable.Range(0, dugumler.Count).Where(i => dugumler[i] is { Satir: not null, Bakiye: not null } && Komsu(dugumler, i, adim)?.Bakiye is not null).ToList();
        // Bakiyenin hangi tutar olduğu başlıktan bilinmiyorsa tek rastlantı kanıt sayılmaz.
        var kolonlu = dugumler.All(d => d.Satir is null || d.Bakiye is null || d.BakiyeKolonu);
        if (!kolonlu && (baglar.Count < 2 || baglar.Count * 2 < olasi.Count))
            return BakiyeCozumu.Bos;
        var dogrulanan = baglar.Keys.SelectMany(i => new[] { i, i + adim }).ToHashSet();
        var yalnizTutar = dogrulanan.Where(i => !baglar.ContainsKey(i) && dugumler[i] is { Satir: not null, Tutarlar.Count: 1 })
            .ToDictionary(i => dugumler[i].Satir!.Value, i => dugumler[i].Tutarlar[0]);
        var kopuk = olasi.Where(i => !baglar.ContainsKey(i) && (dugumler[i].Bakiye != Komsu(dugumler, i, adim)!.Bakiye || dugumler[i].Tutarlar.Count > 0))
            .Select(i => dugumler[i].Satir!.Value).ToList();
        return new(baglar.ToDictionary(b => dugumler[b.Key].Satir!.Value, b => b.Value), yalnizTutar, kopuk);
    }

    private static Dictionary<int, BakiyeBagi> Baglar(IReadOnlyList<BakiyeDugumu> dugumler, int adim)
    {
        var sonuc = new Dictionary<int, BakiyeBagi>();
        for (var i = 0; i < dugumler.Count; i++)
        {
            if (dugumler[i] is not { Satir: not null, Bakiye: { } bakiye } d || Komsu(dugumler, i, adim)?.Bakiye is not { } onceki)
                continue;
            var fark = bakiye - onceki;
            if (fark == 0)
                continue;
            foreach (var t in d.Tutarlar)
                if (t.Tutar == Math.Abs(fark))
                {
                    sonuc.Add(i, new(t, fark > 0));
                    break;
                }
        }
        return sonuc;
    }

    private static BakiyeDugumu? Komsu(IReadOnlyList<BakiyeDugumu> dugumler, int i, int adim) =>
        i + adim >= 0 && i + adim < dugumler.Count ? dugumler[i + adim] : null;

    private static int TarihSirasi(IReadOnlyList<BakiyeDugumu> dugumler)
    {
        var tarihler = dugumler.Where(d => d.Tarih is not null).Select(d => d.Tarih!.Value).ToList();
        return tarihler.Count < 2 || tarihler[0] == tarihler[^1] ? 0 : tarihler[0] < tarihler[^1] ? -1 : +1;
    }
}
