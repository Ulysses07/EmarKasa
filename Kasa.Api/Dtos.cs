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
