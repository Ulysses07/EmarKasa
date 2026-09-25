using Kasa.Core;

namespace Kasa.Api;

public record LoginDto(string? Kullanici, string Sifre);
public record GelenUpsertDto(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record IzleyiciSifreDto(string YeniSifre);

// Yazma istekleri veritabanı kimliklerini ve ilişki nesnelerini değiştiremez.
public record KanalYazDto(string Ad, bool Aktif = true, int Sira = 0, decimal AcilisDevri = 0);
public record CariYazDto(string Ad, bool Aktif = true);
public record IslemYazDto(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal,
    GiderTipi Tip, string? Not = null, int? KrediKartiId = null);
public record KrediKartiYazDto(string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi,
    decimal Limit, decimal Borc);
public record KrediYazDto(string Ad, decimal CekilenTutar, DateOnly CekimTarihi,
    int TaksitSayisi, decimal AylikOdeme, int OdemeGunu, string Kanal);
public record KartOdemeYazDto(int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not = null);

public record KanalBakiye(string Kanal, decimal Bakiye, int? KanalId = null);
public record PanelDto(
    decimal GuncelKasa,
    IReadOnlyList<KanalBakiye> Kanallar,
    decimal BuHaftaSonucu,
    decimal BuAySonucu,
    decimal DagilimBekleyenTutar = 0m);

public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);
