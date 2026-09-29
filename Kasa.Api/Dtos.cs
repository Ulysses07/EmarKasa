using Kasa.Core;

namespace Kasa.Api;

public record LoginDto(string? Kullanici, string Sifre);
/// <param name="Surum">contract-6: istemcinin gördüğü satırın sürümü (satır yoksa 0). Uyuşmazsa 409; eski istemci göndermez (null),
/// denetlenmez ve son yazan kazanır, sürüm yine artar.</param>
public record GelenUpsertDto(DateOnly DonemStart, string Kanal, decimal TutarTl, int? Surum = null);
public record IzleyiciSifreDto(string YeniSifre);

// Yazma istekleri veritabanı kimliklerini ve ilişki nesnelerini değiştiremez.
// Surum (contract-6): düzenlemede (PUT) istemcinin okuduğu kaydın sürümü; uyuşmazsa 409. Eski istemci göndermez (null): denetlenmez,
// son yazan kazanır, sürüm yine artar. Oluşturmada (POST) yok sayılır.
public record KanalYazDto(string Ad, bool Aktif = true, int Sira = 0, decimal AcilisDevri = 0, int? Surum = null);
public record CariYazDto(string Ad, bool Aktif = true);
/// <param name="IstekId">Yalnız oluşturmada (POST) tekrar anahtarı (appcore-5); eski istemci göndermez, düzenlemede yok sayılır.</param>
/// <param name="TaksitSayisi">Yalnız oluşturmada, yeni takipteki kartla girilen kart giderinde taksit sayısı (1–60); boşsa tek taksit
/// (gap-coklu-giris-cift-sayim-mutabakat-6). Eski istemci göndermez; düzenlemede gönderilmez.</param>
/// <param name="IlkKesimTarihi">Yalnız oluşturmada, isteğe bağlı ilk taksidin ekstre kesimi (<see cref="AlisOdemeYaz.IlkKesimTarihi"/>).</param>
/// <param name="Surum">Yalnız düzenlemede (PUT) istemcinin okuduğu giderin sürümü (contract-6; bkz. <see cref="KanalYazDto"/>).</param>
public record IslemYazDto(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal,
    GiderTipi Tip, string? Not = null, int? KrediKartiId = null, Guid? IstekId = null, int? TaksitSayisi = null, DateOnly? IlkKesimTarihi = null,
    int? Surum = null);
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

/// <param name="YeniTakip">Kart yeni takipte (TakipKartlar); eski (takipsiz) kartta false.</param>
/// <param name="Aktif">Takipli kart yeni kullanıma açık; eski kartta true (kendi başına seçilebilirlik göstermez).
/// Yeni kredi kartı gideri yalnız YeniTakip ve Aktif karta bağlanabilir (K3).</param>
public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc, bool YeniTakip = false, bool Aktif = true);
