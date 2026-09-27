namespace Kasa.Api;

public record KanalPayYaz(int KanalId, decimal Tutar);
public record TakipKanalPayi(int? KanalId, string Kanal, decimal Tutar);
public record TakipDurumYaz(Guid IstekId, int Surum, bool Aktif, string Aciklama);
public record TakipIptalYaz(Guid IstekId, int Surum, string Aciklama);
public record KartTakipYaz(Guid IstekId, int Surum, string Ad, decimal Limit, int KesimGunu, int SonOdemeGunu, DateOnly AcilisTarihi, decimal AcilisBorc, IReadOnlyList<KanalPayYaz> AcilisDagilimlari);
public record KartHarcamaYaz(Guid IstekId, int Surum, DateOnly Tarih, string Aciklama, decimal Tutar, int TaksitSayisi, DateOnly? IlkKesimTarihi, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KaynakHarcamaId = null);
public record KartEkstreYaz(Guid IstekId, int Surum, DateOnly SonOdemeTarihi, decimal? AsgariOdeme, string Aciklama);
public record KartTakipOdemeYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, int? EkstreId = null, string? Not = null);
public record KartGecisYaz(Guid IstekId, int Surum, DateOnly Baslangic, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, IReadOnlyList<KanalPayYaz> Dagilimlar, string Aciklama, bool Onay);
public record KartTakipDto(int Id, int Surum, string Ad, bool YeniTakip, bool Aktif, DateOnly? TakipBaslangic, int KesimGunu, int SonOdemeGunu, decimal Limit, decimal Borc, decimal EkstreBorc, IReadOnlyList<KartEkstreDto> Ekstreler, IReadOnlyList<KartHarcamaDto> Harcamalar, IReadOnlyList<KartTakipOdemeDto> Odemeler, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null);
public record KartEkstreDto(int Id, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Borc, decimal Odenen, decimal Kalan, decimal? AsgariOdeme, decimal? AsgariKalan = null);
public record KartHarcamaDto(int Id, int? IslemId, DateOnly Tarih, string Aciklama, decimal Tutar, int TaksitSayisi, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null);
public record KartTakipOdemeDto(int Id, DateOnly Tarih, decimal Tutar, decimal KasaEtkisi, string? Not, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null);
public record KartOdemeOnizlemeDto(decimal Tutar, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<KartEkstreOdemePayi> Ekstreler);
public record KartEkstreOdemePayi(int EkstreId, decimal Tutar);
public record KrediTakipYaz(Guid IstekId, string Ad, decimal CekilenTutar, DateOnly CekimTarihi, DateOnly IlkTaksitTarihi, int TaksitSayisi, decimal AylikOdeme, IReadOnlyList<int> KanalIdleri, bool MevcutKredi = false);
public record KrediTaksitYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, string? Not, bool Iptal, string Aciklama);
public record KrediKapatYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, string Aciklama);
public record KrediGecisYaz(Guid IstekId, int Surum, DateOnly Baslangic, IReadOnlyList<int> KanalIdleri, string Aciklama, bool Onay);
public record KrediTakipDto(int Id, int Surum, string Ad, bool YeniTakip, bool Aktif, DateOnly? TakipBaslangic, decimal CekilenTutar, DateOnly CekimTarihi, decimal KalanPlanliOdeme, IReadOnlyList<TakipKanalPayi> KanalPaylari, IReadOnlyList<KrediPlanTaksitDto> Taksitler);
public record KrediPlanTaksitDto(int Id, int No, DateOnly Tarih, decimal Tutar, string Durum, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar);
/// <summary>Eski kayıttan yeni takibe geçiş önizlemesi.</summary>
/// <param name="GenelKasaAnlikFarki">Kart geçişinde kasada önceden sayılan tutar (K) eksi önerilen tutar: geçişin genel
/// kasaya kalıcı etkisi. 0: tutarlı; pozitif: kasadan hiçbir zaman düşmeyecek tutar; negatif: ödendiğinde ikinci kez
/// düşecek tutar. Geçiş anında bugünkü kasa değişmez; fark ödeme ve ay sonu düşümleriyle ortaya çıkar.</param>
/// <param name="KanalAnlikFarki">Aynı farkın kanallara yansıyan kısmı. Devir kanal payı taşımıyorsa ödeme
/// "Dağılım bekliyor" olur ve kanal farkı 0'dır.</param>
/// <param name="KabulEdilebilir">Kartta K ≤ önerilen tutar; aşan geçiş 409 ile reddedilir.</param>
/// <param name="SistemKartBorcu">Kart açılış borcu + eski kart giderleri − eski kart ödemeleri. Kredide null.</param>
/// <param name="EskiKuraldaIslenenTutar">Başlangıçtan önce, eski ay sonu kuralıyla kasaya işlenmiş eski gider toplamı.</param>
/// <param name="BekleyenEskiDusumTutari">Başlangıçtan önceki ama eski ay sonu düşümü başlangıçta/sonrasında olan gider
/// toplamı; yeni kuralda SonBekleyenDusumTarihi'ne kadar ay sonlarında düşmeye devam eder.</param>
/// <param name="OnerilenKasadaSayilanTutar">max(0, min(kalan borç, sistem kart borcu)).</param>
public record TakipGecisDto(string Kaynak, int KaynakId, DateOnly Baslangic, decimal GenelKasaAnlikFarki, decimal KanalAnlikFarki, decimal EskiKasadaSayilanTutar, IReadOnlyList<string> Aciklamalar, bool KabulEdilebilir,
    decimal? SistemKartBorcu = null, decimal? EskiKuraldaIslenenTutar = null, decimal? BekleyenEskiDusumTutari = null, DateOnly? SonBekleyenDusumTarihi = null, decimal? OnerilenKasadaSayilanTutar = null);
public record TakipOzetDto(DateOnly Tarih, decimal KartBorcu, decimal KalanKrediPlani, IReadOnlyList<TakipOlayDto> Olaylar, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null, decimal KartAlacakBakiyesi = 0);
public record TakipOlayDto(string Kaynak, int KaynakId, int KalemId, string Ad, DateOnly Tarih, decimal Tutar, string Tur, bool OtomatikKasa);
internal record KartTaksitPayi(int TaksitId, decimal Tutar, decimal OncedenOdenen = 0m);
