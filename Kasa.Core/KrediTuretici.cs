namespace Kasa.Core;

/// <summary>Krediyi sentetik <see cref="Gelen"/>/<see cref="Islem"/> kayıtlarına çevirir.
/// Saf: EF/I-O yok. Kayıtlar yalnız hesap motoruna beslenir, DB'ye yazılmaz.</summary>
public static class KrediTuretici
{
    /// <summary>Çekim gelirinin taşındığı sahte kanal etiketi. Gerçek bir kanal değildir,
    /// bu yüzden yalnız genel kasaya girer, hiçbir kanalın kârına yansımaz.</summary>
    public const string KrediKanal = "__KREDI__";

    /// <summary>Çekim tarihini içeren dönemi bulur; yoksa <c>null</c>.</summary>
    public static Gelen? CekimGeleni(Kredi k, IReadOnlyList<Donem> donemler)
    {
        foreach (var d in donemler)
            if (d.Icerir(k.CekimTarihi))
                return new Gelen(d.Start, KrediKanal, k.CekilenTutar);
        return null;
    }

    /// <summary><see cref="Kredi.TaksitSayisi"/> adet taksit gideri üretir.
    /// İlk taksit çekim tarihinden KESİN sonraki ilk <see cref="Kredi.OdemeGunu"/> tarihidir;
    /// sonrakiler birer takvim ayı ileri, kısa aylarda gün ay sonuna sabitlenir.</summary>
    public static IReadOnlyList<Islem> TaksitGiderleri(Kredi k)
    {
        var liste = new List<Islem>(k.TaksitSayisi);

        // İlk taksit ayı: çekimden kesin sonraki OdemeGunu tarihi hangi aya düşüyorsa o.
        int y = k.CekimTarihi.Year, m = k.CekimTarihi.Month;
        if (GunClamp(y, m, k.OdemeGunu) <= k.CekimTarihi)
        {
            m++; if (m > 12) { m = 1; y++; }
        }

        for (int i = 0; i < k.TaksitSayisi; i++)
        {
            var tarih = GunClamp(y, m, k.OdemeGunu);
            liste.Add(new Islem(tarih, k.Ad, k.AylikOdeme, k.Kanal, GiderTipi.Cari));
            m++; if (m > 12) { m = 1; y++; }
        }

        return liste;
    }

    /// <summary><paramref name="gun"/> günlü tarih; ay kısa ise ay sonuna kırpar.</summary>
    private static DateOnly GunClamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
