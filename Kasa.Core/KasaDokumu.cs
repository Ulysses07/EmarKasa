namespace Kasa.Core;

/// <summary>
/// "Kasa neden değişti?" adımlarının türü. Sıra, döküm tablosundaki sıradır: önce kasaya girenler,
/// sonra kasadan çıkanlar.
/// </summary>
public enum KasaKalemTuru
{
    /// <summary>Kanal geleni (haftalık gelen rakamı).</summary>
    Gelen,
    /// <summary>Tahsil edilen alınan çek (tahsil gününde kasaya girer).</summary>
    CekTahsilat,
    /// <summary>Kanalın Cari gideri (karta bağlı olmayan).</summary>
    CariGider,
    /// <summary>Kanalın sabit gideri (karta bağlı olmayan).</summary>
    SabitGider,
    /// <summary>Ortak (kanalsız) Cari + sabit gider.</summary>
    OrtakGider,
    /// <summary>Ödenen verilen çek (ödeme gününde kasadan çıkar; Ortak çek dahil).</summary>
    CekOdemesi,
    /// <summary>Kredi kartı borç ödemesi (gerçek ödeme gününde kasadan çıkar).</summary>
    KartOdemesi,
    /// <summary>Karta bağlı olmayan eski K.K: bir önceki ayın toplamı, ayın son döneminde çıkar.</summary>
    ErtelenenKk,
}

/// <summary>
/// Kasanın bir adımı. <see cref="Tutar"/> işaretlidir: kasaya giren +, kasadan çıkan −.
/// <see cref="Kanal"/> kart ödemesinde null'dır (ödemenin kanalı yoktur).
/// </summary>
public record KasaKalemi(KasaKalemTuru Tur, string? Kanal, decimal Tutar);

/// <summary>Dökümün bir satırı: adım ve adımdan sonraki kasa bakiyesi.</summary>
public record KasaDokumAdimi(KasaKalemTuru Tur, string? Kanal, decimal Tutar, decimal Bakiye);

/// <summary>
/// Bir aralığın kasa dökümü: açılış + Σ adımlar = kapanış. Açılış, aralığın ilk döneminden önceki
/// kasa devri; kapanış, son dönemin kasa devridir (haftalık raporun "Kasa devir" sütunu).
/// </summary>
public record KasaDokumu(
    DateOnly Baslangic,
    DateOnly Bitis,
    decimal Acilis,
    IReadOnlyList<KasaDokumAdimi> Adimlar,
    decimal Kapanis)
{
    public decimal ToplamGiren => Adimlar.Where(a => a.Tutar > 0).Sum(a => a.Tutar);
    public decimal ToplamCikan => Adimlar.Where(a => a.Tutar < 0).Sum(a => -a.Tutar);
}

/// <summary>
/// Kasa dökümü: motorun her dönem için ürettiği <see cref="HaftalikOzet.Kalemler"/>'i toplar.
/// Hiçbir para kuralı burada yeniden uygulanmaz; adımlar motorun kasa sonucunu oluşturan
/// kalemlerin kendisidir (dönem başına Σ Kalemler = KasaSonucu).
/// </summary>
public static class KasaDokumuHesap
{
    /// <summary>
    /// Ardışık dönem özetlerinden döküm üretir. Aynı (tür, kanal) adımları tek satırda toplanır;
    /// sıra: tür, sonra kanal sırası (<paramref name="kanallar"/> sırası, sonra Ortak, sonra diğer
    /// adlar alfabetik). Boş liste verilirse null döner.
    /// </summary>
    public static KasaDokumu? Olustur(IReadOnlyList<HaftalikOzet> ozetler, IReadOnlyList<Kanal> kanallar)
    {
        if (ozetler.Count == 0) return null;
        var sirali = ozetler.OrderBy(o => o.Donem.Start).ToList();
        var ilk = sirali[0];
        var son = sirali[^1];
        decimal acilis = ilk.KasaDevir - ilk.KasaSonucu;

        var toplam = new Dictionary<(KasaKalemTuru, string?), decimal>();
        foreach (var o in sirali)
            foreach (var k in o.Kalemler)
                toplam[(k.Tur, k.Kanal)] = toplam.GetValueOrDefault((k.Tur, k.Kanal)) + k.Tutar;

        var adimlar = new List<KasaDokumAdimi>(toplam.Count);
        decimal bakiye = acilis;
        foreach (var k in Sirala(toplam.Where(x => x.Value != 0m)
                     .Select(x => new KasaKalemi(x.Key.Item1, x.Key.Item2, x.Value)), kanallar))
        {
            bakiye += k.Tutar;
            adimlar.Add(new KasaDokumAdimi(k.Tur, k.Kanal, k.Tutar, bakiye));
        }
        return new KasaDokumu(ilk.Donem.Start, son.Donem.End, acilis, adimlar, son.KasaDevir);
    }

    /// <summary>Kalemleri döküm sırasına dizer (tür, kanal sırası, ad).</summary>
    public static IReadOnlyList<KasaKalemi> Sirala(IEnumerable<KasaKalemi> kalemler, IReadOnlyList<Kanal> kanallar)
    {
        var sira = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < kanallar.Count; i++) sira.TryAdd(kanallar[i].Ad, i);
        int KanalSira(string? ad) => ad is null ? -1
            : sira.TryGetValue(ad, out var s) ? s
            : ad == Kanallar.Ortak ? kanallar.Count : kanallar.Count + 1;
        return kalemler
            .OrderBy(k => k.Tur)
            .ThenBy(k => KanalSira(k.Kanal))
            .ThenBy(k => k.Kanal, StringComparer.Ordinal)
            .ToList();
    }
}
