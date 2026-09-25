using System.Numerics;

namespace Kasa.Core;

public record AlisKanalPayi(int KanalId, decimal Tutar);

/// <summary>Alış ödemesini, onaylı kanal tutarlarına göre kuruş kaybetmeden dağıtır.</summary>
public static class AlisDagitici
{
    // Bu aralıkta bütün kuruş değerleri decimal ile tam temsil edilebilir.
    private const decimal EnBuyukTutar = decimal.MaxValue / 100m;
    private static readonly BigInteger EnBuyukKurus = new(decimal.MaxValue);

    /// <summary>
    /// Kümülatif D'Hondt paylarının farkını döndürür. Paylar ödeme büyüdükçe azalmaz;
    /// son ödeme alışın her kanal tutarını tam olarak tamamlar. Eşitlikte küçük KanalId
    /// önceliklidir. Aynı kanal önceden toplanmalı; çıktı sıfır payları da içerir.
    /// </summary>
    public static IReadOnlyList<AlisKanalPayi> Dagit(
        IReadOnlyList<AlisKanalPayi> paylar, decimal oncekiOdeme, decimal odeme)
    {
        ArgumentNullException.ThrowIfNull(paylar);
        if (paylar.Count == 0)
            throw new ArgumentException("En az bir kanal payı gereklidir.", nameof(paylar));

        var kimlikler = new HashSet<int>();
        foreach (var pay in paylar)
        {
            if (pay is null || pay.KanalId <= 0)
                throw new ArgumentException("Her kanalın geçerli bir kimliği olmalıdır.", nameof(paylar));
            if (!kimlikler.Add(pay.KanalId))
                throw new ArgumentException("Aynı kanalın payları önce tek satırda toplanmalıdır.", nameof(paylar));
        }

        var sirali = paylar.OrderBy(p => p.KanalId).ToArray();
        var agirliklar = sirali.Select(p => KurusaCevir(p.Tutar, nameof(paylar))).ToArray();
        var toplam = agirliklar.Aggregate(BigInteger.Zero, (a, b) => a + b);
        if (toplam > EnBuyukKurus)
            throw new ArgumentOutOfRangeException(nameof(paylar), "Alış toplamı kuruş hassasiyeti sınırını aşıyor.");

        var onceki = KurusaCevir(oncekiOdeme, nameof(oncekiOdeme), sifirOlabilir: true);
        var simdiki = KurusaCevir(odeme, nameof(odeme));
        if (onceki + simdiki > toplam)
            throw new ArgumentOutOfRangeException(nameof(odeme), "Ödemeler toplamı alış tutarını aşamaz.");

        var eskiPaylar = KumulatifPaylar(sirali, agirliklar, toplam, onceki);
        var yeniPaylar = KumulatifPaylar(sirali, agirliklar, toplam, onceki + simdiki);
        return sirali.Select((p, i) => new AlisKanalPayi(p.KanalId,
            (decimal)(yeniPaylar[i] - eskiPaylar[i]) / 100m)).ToArray();
    }

    private static BigInteger KurusaCevir(decimal tutar, string parametre, bool sifirOlabilir = false)
    {
        if (tutar < 0 || (!sifirOlabilir && tutar == 0) || tutar > EnBuyukTutar
            || decimal.Round(tutar, 2) != tutar)
            throw new ArgumentOutOfRangeException(parametre,
                "Tutar kuruş hassasiyetinde, izin verilen aralıkta ve pozitif olmalıdır; önceki ödeme sıfır olabilir.");
        return new BigInteger(tutar * 100m);
    }

    private static BigInteger[] KumulatifPaylar(
        AlisKanalPayi[] kanallar, BigInteger[] agirliklar, BigInteger toplam, BigInteger odenen)
    {
        var sonuc = new BigInteger[kanallar.Length];
        if (odenen.IsZero) return sonuc;

        var oncelikler = new PriorityQueue<int, Oncelik>(OncelikKarsilastirici.Instance);
        var kalan = odenen;
        for (int i = 0; i < kanallar.Length; i++)
        {
            // Bu taban paylardaki her kuruşun önceliği toplam/odenen'den az değildir;
            // geriye kalanların önceliği daha küçüktür. Dolayısıyla sıralamayı bozmadan
            // tabanları atlayabiliriz. Dağıtılacak artık kuruş sayısı kanal sayısından azdır.
            sonuc[i] = odenen * agirliklar[i] / toplam;
            kalan -= sonuc[i];
            oncelikler.Enqueue(i, new Oncelik(agirliklar[i], sonuc[i] + 1, kanallar[i].KanalId));
        }

        for (int i = 0; i < (int)kalan; i++)
        {
            int kanal = oncelikler.Dequeue();
            sonuc[kanal]++;
            oncelikler.Enqueue(kanal,
                new Oncelik(agirliklar[kanal], sonuc[kanal] + 1, kanallar[kanal].KanalId));
        }
        return sonuc;
    }

    private readonly record struct Oncelik(BigInteger Agirlik, BigInteger Bolen, int KanalId);

    private sealed class OncelikKarsilastirici : IComparer<Oncelik>
    {
        public static readonly OncelikKarsilastirici Instance = new();

        public int Compare(Oncelik x, Oncelik y)
        {
            // PriorityQueue küçük değeri önce çıkarır: büyük oran önce gelsin.
            // Kesirleri çapraz çarpmak hem taşmayı hem kayan nokta yuvarlamasını önler.
            int oran = (y.Agirlik * x.Bolen).CompareTo(x.Agirlik * y.Bolen);
            return oran != 0 ? oran : x.KanalId.CompareTo(y.KanalId);
        }
    }
}
