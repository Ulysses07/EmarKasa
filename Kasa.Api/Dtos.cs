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

/// <summary>Kasa sayımı girişi (editör). Defter değeri sunucuda hesaplanır.</summary>
public record KasaSayimYazDto(DateOnly Tarih, decimal SayilanTutar, string? Not);

/// <summary>
/// Kasa sayımı. HesaplananTutar kayıt anındaki defter değeridir (değişmez); Fark = Sayılan − Hesaplanan.
/// GuncelHesaplanan aynı tarih için bugünkü defter değeridir: geçmiş kayıtlar sonradan düzeltildiyse
/// HesaplananTutar'dan farklı olur. Tarih artık takvim dışındaysa (takip başlangıcı ileri alındı) null.
/// </summary>
public record KasaSayimDto(
    int Id, DateOnly Tarih, decimal SayilanTutar, decimal HesaplananTutar, decimal Fark,
    decimal? GuncelHesaplanan, string? Not, DateTime KayitZamaniUtc)
{
    public static KasaSayimDto Olustur(Kasa.Api.Data.KasaSayimEntity e, decimal? guncelHesaplanan) => new(
        e.Id, e.Tarih, e.SayilanTutar, e.HesaplananTutar, e.SayilanTutar - e.HesaplananTutar,
        guncelHesaplanan, e.Not, DateTime.SpecifyKind(e.KayitZamaniUtc, DateTimeKind.Utc));
}

/// <summary>Bir tarihin gün sonundaki defter kasası (sayım formu önizlemesi).</summary>
public record KasaHesapDto(DateOnly Tarih, decimal HesaplananTutar);

/// <summary>Girilmesi (onaylanması ya da atlanması) bekleyen bir tekrarlayan gider ayı.</summary>
public record TekrarlayanBekleyenDto(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade);
/// <summary>Onay: ay zorunlu; tarih verilmezse vade, tutar verilmezse şablonun tutarı.</summary>
public record TekrarlayanOnayDto(DateOnly Ay, DateOnly? Tarih, decimal? Tutar);
public record TekrarlayanAtlaDto(DateOnly Ay);

public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);

/// <summary>Değişiklik geçmişi satırı; <see cref="GeriAlinabilir"/> sunucu kurallarıyla hesaplanır (silindi + desteklenen tür + 30 gün + geri alınmamış).
/// <see cref="GecmiseDonuk"/>: önceki bir ayın rakamını değiştiriyor (<see cref="Kasa.Api.Data.GecmiseDonukKurali"/>).</summary>
public record DegisiklikDto(
    int Id, DateTime ZamanUtc, string Rol, string Tur, int? KayitId, string Eylem, string Ozet,
    string? EskiJson, string? YeniJson, bool GeriAlindi, DateTime? GeriAlmaZamaniUtc, bool GeriAlinabilir,
    bool GecmiseDonuk = false);
