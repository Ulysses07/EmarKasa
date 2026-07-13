namespace Kasa.Core;

public record KanalHaftalik(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);

public record HaftalikOzet(
    Donem Donem,
    IReadOnlyList<KanalHaftalik> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir);

public static class HesapMotoru
{
    /// <summary>
    /// Dönem dönem haftalık özet üretir. Kanal devri yalnız o kanalın Cari tipli
    /// gidenini sayar; kasa devri TÜM gidenleri (her kanal + Ortak, her tip) sayar.
    /// Devirler tarih sırasına göre zincirlenir; ilk dönemin girişi açılış bakiyeleridir.
    /// </summary>
    public static IReadOnlyList<HaftalikOzet> HaftalikHesapla(
        decimal kasaAcilisDevri,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>();

        foreach (var donem in sirali)
        {
            var donemIslem = islemler.Where(i => donem.Icerir(i.Tarih)).ToList();
            var donemGelen = gelenler.Where(g => g.DonemStart == donem.Start).ToList();

            var kanalSatirlari = new List<KanalHaftalik>();
            foreach (var kanal in kanallar)
            {
                decimal gelen = donemGelen.Where(g => g.Kanal == kanal.Ad).Sum(g => g.TutarTl);
                decimal gidenCari = donemIslem
                    .Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari)
                    .Sum(i => i.TutarTl);
                decimal kanalSonuc = gelen - gidenCari;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad]));
            }

            decimal toplamGelen = donemGelen.Sum(g => g.TutarTl);
            decimal toplamGiden = donemIslem.Sum(i => i.TutarTl);
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir));
        }
        return sonuc;
    }
}
