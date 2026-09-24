namespace Kasa.Api;

public record LoginDto(string? Kullanici, string Sifre);
public record GelenUpsertDto(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record IzleyiciSifreDto(string YeniSifre);

public record KanalBakiye(string Kanal, decimal Bakiye);
public record PanelDto(
    decimal GuncelKasa,
    IReadOnlyList<KanalBakiye> Kanallar,
    decimal BuHaftaSonucu,
    decimal BuAySonucu);

/// <summary>Girilmesi (onaylanması ya da atlanması) bekleyen bir tekrarlayan gider ayı.</summary>
public record TekrarlayanBekleyenDto(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade);
/// <summary>Onay: ay zorunlu; tarih verilmezse vade, tutar verilmezse şablonun tutarı.</summary>
public record TekrarlayanOnayDto(DateOnly Ay, DateOnly? Tarih, decimal? Tutar);
public record TekrarlayanAtlaDto(DateOnly Ay);

public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);
