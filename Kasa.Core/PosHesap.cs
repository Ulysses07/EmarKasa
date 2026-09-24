namespace Kasa.Core;

/// <summary>
/// POS (sanal/fiziki POS, iyzico, PayTR…) satışlarının komisyon, net ve valör hesabı.
/// <para>
/// BİLGİ AMAÇLIDIR: kasa, kanal devri ve kârlılık hesaplarına (<see cref="HesapMotoru"/>) girmez.
/// Hesap motoru POS kayıtlarını hiç görmez; POS verisi yalnız POS sayfasında ve POS özetinde okunur.
/// </para>
/// Kurallar:
/// <list type="bullet">
/// <item>Komisyon = brüt × oran / 100, kuruşa yuvarlanır (<see cref="Para.Yuvarla"/>: ,5 sıfırdan uzağa).</item>
/// <item>Net = brüt − komisyon (net ile komisyonun toplamı her zaman brüte eşittir).</item>
/// <item>Valör = satış tarihi + blokaj günü (takvim günü; hafta sonu/tatil kaydırması yapılmaz).</item>
/// <item>Bugün itibarıyla bankada bloke: satış günü ≤ bugün &lt; valör. Blokajı 0 olan satış hiç bloke olmaz;
///       ileri tarihli satış henüz gerçekleşmediği için bloke sayılmaz.</item>
/// </list>
/// </summary>
public static class PosHesap
{
    /// <summary>Komisyon (kuruşa yuvarlı). Oran yüzde olarak verilir (1,79 → %1,79).</summary>
    public static decimal Komisyon(decimal brut, decimal oranYuzde) => Para.Yuvarla(Para.Yuvarla(brut) * oranYuzde / 100m);

    /// <summary>Net = brüt − komisyon (kuruşa yuvarlı brütten).</summary>
    public static decimal Net(decimal brut, decimal oranYuzde) => Para.Yuvarla(brut) - Komisyon(brut, oranYuzde);

    /// <summary>Paranın hesaba geçeceği gün: satış tarihi + blokaj günü (takvim günü).</summary>
    public static DateOnly Valor(DateOnly tarih, int blokajGunu) => tarih.AddDays(blokajGunu);

    /// <summary>Satış bugün itibarıyla bankada bloke mi (satış günü ≤ bugün &lt; valör).</summary>
    public static bool BlokeMi(DateOnly tarih, int blokajGunu, DateOnly bugun) => tarih <= bugun && bugun < Valor(tarih, blokajGunu);

    /// <summary>Bugün itibarıyla bankada bloke duran net tutarların toplamı ve adedi.</summary>
    public static (decimal Net, int Adet) Bloke(IEnumerable<PosKalem> kalemler, DateOnly bugun)
    {
        decimal net = 0m; int adet = 0;
        foreach (var k in kalemler)
        {
            if (!BlokeMi(k.Tarih, k.BlokajGunu, bugun)) continue;
            net += Net(k.Brut, k.KomisyonOrani);
            adet++;
        }
        return (net, adet);
    }

    /// <summary>
    /// Bugün itibarıyla bankada bloke duran netin kanal başına dökümü (satış ayından bağımsız: geçen ay
    /// girilip valörü bu aya kalan satış da burada görünür). Kanal sırası ilk görülen önce; toplamı
    /// <see cref="Bloke"/> ile aynıdır.
    /// </summary>
    public static IReadOnlyList<PosKanalBloke> BlokeKanallar(IEnumerable<PosKalem> kalemler, DateOnly bugun)
    {
        var sonuc = new List<PosKanalBloke>();
        var sira = new Dictionary<string, int>();
        foreach (var k in kalemler)
        {
            if (!BlokeMi(k.Tarih, k.BlokajGunu, bugun)) continue;
            var net = Net(k.Brut, k.KomisyonOrani);
            if (sira.TryGetValue(k.Kanal, out var i))
                sonuc[i] = sonuc[i] with { Net = sonuc[i].Net + net, Adet = sonuc[i].Adet + 1 };
            else
            {
                sira[k.Kanal] = sonuc.Count;
                sonuc.Add(new PosKanalBloke(k.Kanal, net, 1));
            }
        }
        return sonuc;
    }

    /// <summary>Bloke kalemlerin valör gününe göre dökümü (en yakın valör önce).</summary>
    public static IReadOnlyList<PosValorGunu> BekleyenValorler(IEnumerable<PosKalem> kalemler, DateOnly bugun)
        => kalemler.Where(k => BlokeMi(k.Tarih, k.BlokajGunu, bugun))
            .GroupBy(k => Valor(k.Tarih, k.BlokajGunu))
            .OrderBy(g => g.Key)
            .Select(g => new PosValorGunu(g.Key, g.Sum(k => Net(k.Brut, k.KomisyonOrani)), g.Count()))
            .ToList();

    /// <summary>
    /// Bir ayın (satış tarihine göre) kanal başına brüt / komisyon / net toplamları. Kanal sırası
    /// verilen sırayı izler (ilk görülen önce); satış yoksa liste boştur.
    /// </summary>
    public static IReadOnlyList<PosKanalOzeti> AylikOzet(IEnumerable<PosKalem> kalemler, int yil, int ay)
    {
        var sonuc = new List<PosKanalOzeti>();
        var sira = new Dictionary<string, int>();
        foreach (var k in kalemler)
        {
            if (k.Tarih.Year != yil || k.Tarih.Month != ay) continue;
            var komisyon = Komisyon(k.Brut, k.KomisyonOrani);
            var brut = Para.Yuvarla(k.Brut);
            if (sira.TryGetValue(k.Kanal, out var i))
            {
                var o = sonuc[i];
                sonuc[i] = o with { Brut = o.Brut + brut, Komisyon = o.Komisyon + komisyon, Net = o.Net + brut - komisyon, Adet = o.Adet + 1 };
            }
            else
            {
                sira[k.Kanal] = sonuc.Count;
                sonuc.Add(new PosKanalOzeti(k.Kanal, brut, komisyon, brut - komisyon, 1));
            }
        }
        return sonuc;
    }
}

/// <summary>Tek POS satışı (hesap için gereken alanlar). Oran ve blokaj satış anındaki değerlerdir.</summary>
public sealed record PosKalem(DateOnly Tarih, string Kanal, decimal Brut, decimal KomisyonOrani, int BlokajGunu);

/// <summary>Bir kanalın aylık POS toplamı.</summary>
public sealed record PosKanalOzeti(string Kanal, decimal Brut, decimal Komisyon, decimal Net, int Adet);

/// <summary>Bir kanalın bugün bankada bloke duran neti.</summary>
public sealed record PosKanalBloke(string Kanal, decimal Net, int Adet);

/// <summary>Aynı gün hesaba geçecek bloke tutar.</summary>
public sealed record PosValorGunu(DateOnly Valor, decimal Net, int Adet);
