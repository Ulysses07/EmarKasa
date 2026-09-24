namespace Kasa.Core;

/// <summary>
/// Para (TL) yardımcıları. Tüm tutarlar kuruşa (2 ondalık) <see cref="MidpointRounding.AwayFromZero"/>
/// ile yuvarlanır; hesap motoru her tutarı içeri alırken bu yuvarlamayı uygular, böylece
/// haftalık ve aylık rapor aynı kuruş değerleriyle çalışır.
/// </summary>
public static class Para
{
    /// <summary>Tutarı kuruşa (2 ondalık) yuvarlar; ,5 sıfırdan uzağa gider (100,005 → 100,01).</summary>
    public static decimal Yuvarla(decimal tutar) => decimal.Round(tutar, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// <paramref name="toplam"/>'ı (önce kuruşa yuvarlanır) <paramref name="parca"/> eşit paya kuruş
    /// bazında böler. Tam bölünmeyen artık kuruşlar sırayla ilk paylara (+0,01; negatifte −0,01) verilir;
    /// payların toplamı her zaman yuvarlanmış toplama eşittir. Yalnız decimal aritmetik kullanılır
    /// (long dönüşümü yok) — çok büyük tutarlarda taşma olmaz; negatif toplam simetrik bölünür.
    /// </summary>
    public static decimal[] KurusBol(decimal toplam, int parca)
    {
        if (parca <= 0) throw new ArgumentOutOfRangeException(nameof(parca), "Parça sayısı pozitif olmalı.");
        var yuvarli = Yuvarla(toplam);
        int isaret = yuvarli < 0 ? -1 : 1;
        var mutlak = Math.Abs(yuvarli);

        // mutlak = lira + kurus/100 → (lira*100 + kurus) kuruşu parca'ya böl; lira*100 çarpımı
        // taşmasın diye lira kısmı ayrı bölünür, kalanı küçük bir kuruş sayısı olarak işlenir.
        var lira = decimal.Truncate(mutlak);
        var kurus = (mutlak - lira) * 100m;                       // 0..99, tam sayı
        var liraTaban = decimal.Truncate(lira / parca);
        var liraKalan = lira - liraTaban * parca;
        // Çok büyük sayılarda bölme yuvarlaması tabanı ±1 kaydırabilir; düzelt.
        while (liraKalan < 0) { liraTaban -= 1; liraKalan += parca; }
        while (liraKalan >= parca) { liraTaban += 1; liraKalan -= parca; }

        var kalanKurus = liraKalan * 100m + kurus;                 // < 100 * parca
        var kurusTaban = decimal.Truncate(kalanKurus / parca);
        var artan = (int)(kalanKurus - kurusTaban * parca);       // 0..parca-1

        var paylar = new decimal[parca];
        for (int i = 0; i < parca; i++)
        {
            var pay = liraTaban + (kurusTaban + (i < artan ? 1 : 0)) / 100m;
            paylar[i] = isaret * pay;
        }
        return paylar;
    }
}
