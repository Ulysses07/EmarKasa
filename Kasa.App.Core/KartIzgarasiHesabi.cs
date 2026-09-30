namespace Kasa.App.Core;

/// <summary>Izgaradaki bir öğenin yeri (ızgaranın iç alanına göre, piksel).</summary>
public readonly record struct Dikdortgen(double X, double Y, double Genislik, double Yukseklik);

/// <summary>Izgaranın yerleşimi: sütun sayısı, kullanılan genişlik, kutu genişliği, görünen kutuların ve (açıksa) ayrıntının
/// dikdörtgenleri, toplam yükseklik.</summary>
public sealed record KartIzgarasiYerlesimi(int Sutun, double Genislik, double KutuGenisligi, IReadOnlyList<Dikdortgen> Kutular,
    Dikdortgen? Ayrinti, double Yukseklik);

/// <summary>
/// Kart ızgarasının saf yerleşim hesabı (Kasa.App/Controls/KartIzgarasi yalnız bunu kullanır; Windows'tan bağımsız sınanır).
/// Satırdaki kutu sayısı genişliğe sığan en çok kutudur (kutu en az <see cref="EnAzKutuGenisligi"/>); kutular satıra eşit
/// genişlikte yayılır, satırın yüksekliği satırdaki en yüksek kutudur ve satırdaki bütün kutular o yüksekliği alır. Açık kutu
/// varsa ayrıntı, o kutunun satırından bir aralık sonra tam genişlikte yer alır; sonraki satırlar onun altına kayar.
/// </summary>
public static class KartIzgarasiHesabi
{
    public const double EnAzKutuGenisligi = 220;

    /// <summary>Genişlik ölçüsü: sütun sayısı, kullanılan genişlik (sonsuz ya da sıfırsa tek kutu genişliği) ve kutu genişliği.</summary>
    public static (int Sutun, double Genislik, double KutuGenisligi) Olcu(double genislik, double aralik)
    {
        var g = double.IsFinite(genislik) && genislik > 0 ? genislik : EnAzKutuGenisligi;
        var sutun = Math.Max(1, (int)Math.Floor((g + aralik) / (EnAzKutuGenisligi + aralik)));
        return (sutun, g, (g - (sutun - 1) * aralik) / sutun);
    }

    /// <param name="kutuYukseklikleri">Görünen kutuların ölçülmüş yükseklikleri, sırayla.</param>
    /// <param name="acikIndeks">Açık kutunun sırası (<see cref="AcikIndeks"/>); yoksa -1.</param>
    public static KartIzgarasiYerlesimi Hesapla(double genislik, double aralik, IReadOnlyList<double> kutuYukseklikleri, int acikIndeks,
        double ayrintiYuksekligi)
    {
        var (sutun, g, kutuGenisligi) = Olcu(genislik, aralik);
        var kutular = new Dikdortgen[kutuYukseklikleri.Count];
        Dikdortgen? ayrinti = null;
        var y = 0d;
        for (var bas = 0; bas < kutular.Length; bas += sutun)
        {
            if (bas > 0)
                y += aralik;
            var son = Math.Min(bas + sutun, kutular.Length);
            var satir = 0d;
            for (var i = bas; i < son; i++)
                satir = Math.Max(satir, kutuYukseklikleri[i]);
            for (var i = bas; i < son; i++)
                kutular[i] = new Dikdortgen((i - bas) * (kutuGenisligi + aralik), y, kutuGenisligi, satir);
            y += satir;
            if (acikIndeks >= bas && acikIndeks < son)
            {
                y += aralik;
                ayrinti = new Dikdortgen(0, y, g, ayrintiYuksekligi);
                y += ayrintiYuksekligi;
            }
        }
        return new KartIzgarasiYerlesimi(sutun, g, kutuGenisligi, kutular, ayrinti, y);
    }

    /// <summary>Açık kutunun sırası: yeni kart formu açıksa son kutu ("Yeni kart ekle", sırası kart sayısı), değilse açık kartın
    /// sırası; açık kart listede yoksa ya da yoksa -1.</summary>
    public static int AcikIndeks(IReadOnlyList<int> kartKimlikleri, int? acikKartId, bool yeniKartAcik)
    {
        if (yeniKartAcik)
            return kartKimlikleri.Count;
        if (acikKartId is not { } kimlik)
            return -1;
        for (var i = 0; i < kartKimlikleri.Count; i++)
            if (kartKimlikleri[i] == kimlik)
                return i;
        return -1;
    }
}
