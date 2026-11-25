namespace Kasa.Api;

/// <summary>Satırda yazan, bakiye olmayan bir tutar. <paramref name="Yon"/>: yazıdan okunan yön (+1 giriş, -1 çıkış, 0 yok);
/// <paramref name="Ek"/>: yön B/A ekinden (işaretten değil).</summary>
internal readonly record struct BakiyeTutari(decimal Tutar, int Yon, bool Ek);

/// <summary>Belge sırasındaki bir bakiye: hareket satırı, devir satırı (<paramref name="Satir"/> null) ya da kopma.</summary>
/// <param name="Bakiye">Satırın bakiye adayı (yazılı işaretiyle); yoksa ya da işareti okunamadıysa null (zincir orada kopar).</param>
/// <param name="Ek">Bakiyeden sonraki B (-1) / A (+1) eki; yoksa 0. Ek bakiyenin işareti de işlemin yönü de olabilir.</param>
/// <param name="BakiyeKolonu">Bakiye, tablo başlığındaki "Bakiye" kolonundan okundu.</param>
/// <param name="Gun">Satırın tarihi (yazıldığı gibi); aynı günün satırları karışık basılmış olabilir.</param>
internal sealed record BakiyeDugumu(int? Satir, decimal? Bakiye, int Ek, IReadOnlyList<BakiyeTutari> Tutarlar, bool BakiyeKolonu, string? Gun = null)
{
    /// <summary>Arada okunamayan bir hareket ya da başka bir hesap var: iki yanındaki bakiyeler karşılaştırılmaz.</summary>
    public static readonly BakiyeDugumu Kopma = new(null, null, 0, [], false);
}

/// <summary>Satırın komşusuyla tutan bağı: satırda yazan tutar ve bakiyenin arttığı (giriş) ya da azaldığı bilgisi.</summary>
internal sealed record BakiyeBagi(BakiyeTutari Yazilan, bool Giris);

/// <param name="Baglar">Satır → yönü kanıtlayan bağ.</param>
/// <param name="YalnizTutar">Bakiyesi doğrulanan ama yönü bakiyeden bilinmeyen satırın tutarı.</param>
/// <param name="Kopuk">Komşusunun bakiyesi bilindiği hâlde tutmayan satırlar.</param>
internal sealed record BakiyeCozumu(IReadOnlyDictionary<int, BakiyeBagi> Baglar, IReadOnlyDictionary<int, BakiyeTutari> YalnizTutar, IReadOnlyList<int> Kopuk)
{
    public static readonly BakiyeCozumu Bos = new(new Dictionary<int, BakiyeBagi>(), new Dictionary<int, BakiyeTutari>(), []);
}

/// <summary>
/// Banka hareketlerinde tutar ve yönü bakiyeden doğrular (docs/specs/2026-11-25-pdf-ekrani.md §3). Bir bağ, komşu satırların bakiye
/// farkı satırda yazan tutarlardan birine kuruşu kuruşuna eşitse tutar; satırda yazmayan tutar farktan üretilmez. Yanlış yön
/// üretmemek çözmekten önce gelir: kanıt yetersiz ya da iki anlamlıysa hiçbir satır çözülmez.
/// </summary>
internal static class EkstreBakiyeZinciri
{
    private sealed record Deneme(BakiyeCozumu Cozum, int Bag, int Olasi);

    public static BakiyeCozumu Coz(IReadOnlyList<BakiyeDugumu> dugumler)
    {
        var eksiz = CozTek(dugumler);
        if (dugumler.All(d => d.Ek == 0))
            return eksiz.Cozum;
        // B/A eki bakiyenin işareti (borç bakiye) ya da işlemin yönü olabilir; ikisinde bütün yönler birbirinin tersidir. Yalnız
        // birinin açıkça tuttuğu belge çözülür: bütün satırları B olan belge iki anlamda da tutar ve çözülmez.
        var ekli = CozTek(dugumler.Select(d => d is { Ek: not 0, Bakiye: { } b } ? d with { Bakiye = d.Ek * Math.Abs(b) } : d).ToList());
        var secilen = ekli.Bag > eksiz.Bag ? ekli : eksiz.Bag > ekli.Bag ? eksiz : null;
        return secilen is not null && secilen.Bag * 5 >= secilen.Olasi * 4 ? secilen.Cozum : BakiyeCozumu.Bos;
    }

    private static Deneme CozTek(IReadOnlyList<BakiyeDugumu> dugumler)
    {
        // Eskiden yeniye sırada satırın önceki bakiyesi bir üst, yeniden eskiye sırada bir alt satırdadır.
        var artan = Baglar(dugumler, -1);
        var azalan = Baglar(dugumler, +1);
        if (artan.Count == azalan.Count)
            return new(BakiyeCozumu.Bos, artan.Count, 0);
        var adim = artan.Count > azalan.Count ? -1 : +1;
        var baglar = adim < 0 ? artan : azalan;
        var komsulu = Enumerable.Range(0, dugumler.Count).Where(i => dugumler[i] is { Satir: not null, Bakiye: not null } && Komsu(dugumler, i, adim)?.Bakiye is not null).ToList();
        var olasi = komsulu.Count(i => dugumler[i].Tutarlar.Count > 0);
        // Bakiyenin hangi tutar olduğu başlıktan bilinmiyorsa tek rastlantı kanıt sayılmaz; hiçbir belgede tutmayan çoğunluk yok sayılmaz.
        var kolonlu = dugumler.All(d => d.Satir is null || d.Bakiye is null || d.BakiyeKolonu);
        if (baglar.Count < (kolonlu ? 1 : 2) || baglar.Count * 3 < olasi * 2)
            return new(BakiyeCozumu.Bos, baglar.Count, olasi);
        var kopuk = komsulu.Where(i => !baglar.ContainsKey(i) && (dugumler[i].Bakiye != Komsu(dugumler, i, adim)!.Bakiye || dugumler[i].Tutarlar.Count > 0)).ToList();
        // Kopuk satır olan günün satırları karışık sırada basılmış olabilir: rastlantıyla tutan bağ yön kanıtı sayılmaz.
        var karisikGunler = kopuk.Select(i => dugumler[i].Gun).ToHashSet();
        // Komşusuyla aynı tutarlı satır ters sırada da tutar (aynı gün iki eşit çekim): yönü, sırası kanıtlanmış bir komşuya dayanıyorsa
        // bellidir. Komşu önce işlenir.
        var guvenilir = new HashSet<int>();
        foreach (var i in adim < 0 ? baglar.Keys.Order() : baglar.Keys.OrderDescending())
            if (!karisikGunler.Contains(dugumler[i].Gun)
                && (guvenilir.Contains(i + adim) || !dugumler[i + adim].Tutarlar.Any(t => t.Tutar == baglar[i].Yazilan.Tutar)))
                guvenilir.Add(i);
        var yalnizTutar = new Dictionary<int, BakiyeTutari>();
        foreach (var i in baglar.Keys.SelectMany(i => new[] { i, i + adim }).Distinct())
        {
            if (guvenilir.Contains(i) || dugumler[i].Satir is not { } satir)
                continue;
            if (baglar.TryGetValue(i, out var bag))
                yalnizTutar.Add(satir, bag.Yazilan);
            else if (dugumler[i].Tutarlar.Count == 1)
                yalnizTutar.Add(satir, dugumler[i].Tutarlar[0]);
        }
        return new(new(guvenilir.ToDictionary(i => dugumler[i].Satir!.Value, i => baglar[i]), yalnizTutar, kopuk.Select(i => dugumler[i].Satir!.Value).ToList()),
            baglar.Count, olasi);
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
}
