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

/// <summary>
/// Çek özeti: portföydeki alınan ve ödenecek (portföydeki) verilen çeklerin toplamı/adedi, vadesi
/// <see cref="YaklasanGun"/> gün içinde gelen ve vadesi geçtiği hâlde portföyde bekleyen çekler
/// (iki yön birlikte, vadeye göre artan).
/// </summary>
public record CekOzetDto(
    decimal PortfoydekiAlinanToplam,
    int PortfoydekiAlinanAdet,
    decimal OdenecekVerilenToplam,
    int OdenecekVerilenAdet,
    int YaklasanGun,
    IReadOnlyList<Kasa.Api.Data.CekEntity> Yaklasanlar,
    IReadOnlyList<Kasa.Api.Data.CekEntity> VadesiGecenler);

public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);
