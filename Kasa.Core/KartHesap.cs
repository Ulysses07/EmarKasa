namespace Kasa.Core;

/// <summary>Kredi kartı güncel borcunu türetir (saf, yan-etkisiz).
/// GüncelBorç = açılış + harcamalar − ödemeler. HesapMotoru'na dokunmaz.</summary>
public static class KartHesap
{
    public static decimal GuncelBorc(decimal acilisBorc, decimal harcamaToplam, decimal odemeToplam)
        => acilisBorc + harcamaToplam - odemeToplam;
}
