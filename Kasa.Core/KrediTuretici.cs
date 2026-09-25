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
        Dogrula(k);
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
        Dogrula(k);
        var liste = new List<Islem>(k.TaksitSayisi);
        int ilkAy = IlkTaksitAyi(k);

        for (int i = 0; i < k.TaksitSayisi; i++)
        {
            int taksitAyi = ilkAy + i;
            var tarih = GunClamp(taksitAyi / 12 + 1, taksitAyi % 12 + 1, k.OdemeGunu);
            liste.Add(new Islem(tarih, k.Ad, k.AylikOdeme, k.Kanal, GiderTipi.Cari));
        }

        return liste;
    }

    /// <summary>Kredi tutarlarını ve üretilecek takvimi doğrular.
    /// Ödeme günü 1–31, taksit sayısı 1–600; tutarlar negatif olamaz ve kuruş hassasiyetindedir.</summary>
    public static void Dogrula(Kredi k)
    {
        ArgumentNullException.ThrowIfNull(k);
        if (k.OdemeGunu is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(k.OdemeGunu), "Ödeme günü 1 ile 31 arasında olmalıdır.");
        if (k.TaksitSayisi is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(k.TaksitSayisi), "Taksit sayısı 1 ile 600 arasında olmalıdır.");
        TutarDogrula(k.CekilenTutar, nameof(k.CekilenTutar));
        TutarDogrula(k.AylikOdeme, nameof(k.AylikOdeme));
        if (IlkTaksitAyi(k) + k.TaksitSayisi > 9999 * 12)
            throw new ArgumentOutOfRangeException(nameof(k.CekimTarihi), "Son taksit tarihi 31.12.9999 tarihini aşamaz.");
    }

    private static void TutarDogrula(decimal tutar, string parametre)
    {
        if (tutar < 0 || decimal.Round(tutar, 2) != tutar)
            throw new ArgumentOutOfRangeException(parametre, "Tutar negatif olamaz ve en fazla iki ondalık basamak içerebilir.");
    }

    // 0001 Ocak = 0. Ay indeksiyle sınırı tarih üretmeden doğrulayabiliriz.
    private static int IlkTaksitAyi(Kredi k)
    {
        int yil = k.CekimTarihi.Year, ay = k.CekimTarihi.Month;
        int ilkAy = (yil - 1) * 12 + ay - 1;
        return GunClamp(yil, ay, k.OdemeGunu) <= k.CekimTarihi ? ilkAy + 1 : ilkAy;
    }

    /// <summary><paramref name="gun"/> günlü tarih; ay kısa ise ay sonuna kırpar.</summary>
    private static DateOnly GunClamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
