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
/// <remarks>Satirlar (Paket D) verilirse SayilanTutar satırların toplamına eşit olmalıdır.</remarks>
public record KasaSayimYazDto(DateOnly Tarih, decimal SayilanTutar, string? Not, IReadOnlyList<SayimSatiriDto>? Satirlar = null);

/// <summary>
/// Kasa sayımı. HesaplananTutar kayıt anındaki defter değeridir (değişmez); Fark = Sayılan − Hesaplanan.
/// GuncelHesaplanan aynı tarih için bugünkü defter değeridir: geçmiş kayıtlar sonradan düzeltildiyse
/// HesaplananTutar'dan farklı olur. Tarih artık takvim dışındaysa (takip başlangıcı ileri alındı) null.
/// </summary>
public record KasaSayimDto(
    int Id, DateOnly Tarih, decimal SayilanTutar, decimal HesaplananTutar, decimal Fark,
    decimal? GuncelHesaplanan, string? Not, DateTime KayitZamaniUtc,
    IReadOnlyList<SayimSatiriDto>? Satirlar = null,
    Kasa.Api.Data.SayimFarkDurumu FarkDurumu = Kasa.Api.Data.SayimFarkDurumu.Acik, string? FarkAciklamasi = null)
{
    public static KasaSayimDto Olustur(Kasa.Api.Data.KasaSayimEntity e, decimal? guncelHesaplanan) => new(
        e.Id, e.Tarih, e.SayilanTutar, e.HesaplananTutar, e.SayilanTutar - e.HesaplananTutar,
        guncelHesaplanan, e.Not, DateTime.SpecifyKind(e.KayitZamaniUtc, DateTimeKind.Utc),
        SayimSatirlari.Oku(e.SatirlarJson), e.FarkDurumu, e.FarkAciklamasi);
}

/// <summary>Bir tarihin gün sonundaki defter kasası (sayım formu önizlemesi).</summary>
public record KasaHesapDto(DateOnly Tarih, decimal HesaplananTutar);

/// <summary>Girilmesi (onaylanması ya da atlanması) bekleyen bir tekrarlayan gider ayı.</summary>
/// <remarks>Paket D: TutarDegisken ise tutar onaylarken girilir; KrediKartiId doluysa kayıt karta bağlı K.K işlemi olur.</remarks>
public record TekrarlayanBekleyenDto(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade,
    bool TutarDegisken = false, int? KrediKartiId = null,
    Kasa.Api.Data.TekrarSikligi Siklik = Kasa.Api.Data.TekrarSikligi.Aylik);
/// <summary>
/// Onay: ay zorunlu; tarih verilmezse vade, tutar verilmezse şablonun tutarı, kanal verilmezse
/// şablonun kanalı, not verilmezse "Tekrarlayan gider" (boş metin notu boşaltır).
/// </summary>
public record TekrarlayanOnayDto(DateOnly Ay, DateOnly? Tarih, decimal? Tutar, string? Kanal = null, string? Not = null);
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
