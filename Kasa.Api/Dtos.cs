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

public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);

/// <summary>Değişiklik geçmişi satırı; <see cref="GeriAlinabilir"/> sunucu kurallarıyla hesaplanır (silindi + desteklenen tür + 30 gün + geri alınmamış).</summary>
public record DegisiklikDto(
    int Id, DateTime ZamanUtc, string Rol, string Tur, int? KayitId, string Eylem, string Ozet,
    string? EskiJson, string? YeniJson, bool GeriAlindi, DateTime? GeriAlmaZamaniUtc, bool GeriAlinabilir);
