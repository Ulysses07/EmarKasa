namespace Kasa.ApiClient;

public record KanalPayYaz(int KanalId, decimal Tutar);
public record TakipKanalPayi(int? KanalId, string Kanal, decimal Tutar);
public record TakipDurumYaz(Guid IstekId, int Surum, bool Aktif, string Aciklama);
public record TakipIptalYaz(Guid IstekId, int Surum, string Aciklama);
public record KartTakipYaz(Guid IstekId, int Surum, string Ad, decimal Limit, int KesimGunu, int SonOdemeGunu, DateOnly AcilisTarihi, decimal AcilisBorc, IReadOnlyList<KanalPayYaz> AcilisDagilimlari);
public record KartHarcamaYaz(Guid IstekId, int Surum, DateOnly Tarih, string Aciklama, decimal Tutar, int TaksitSayisi, DateOnly? IlkKesimTarihi, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KaynakHarcamaId = null);
public record KartEkstreYaz(Guid IstekId, int Surum, DateOnly SonOdemeTarihi, decimal? AsgariOdeme, string Aciklama);
public record KartTakipOdemeYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, int? EkstreId, string? Not);
public record KartGecisYaz(Guid IstekId, int Surum, DateOnly Baslangic, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, IReadOnlyList<KanalPayYaz> Dagilimlar, string Aciklama, bool Onay);
public record KartTakipDto(int Id, int Surum, string Ad, bool YeniTakip, bool Aktif, DateOnly? TakipBaslangic, int KesimGunu, int SonOdemeGunu, decimal Limit, decimal Borc, decimal EkstreBorc, IReadOnlyList<KartEkstreDto> Ekstreler, IReadOnlyList<KartHarcamaDto> Harcamalar, IReadOnlyList<KartTakipOdemeDto> Odemeler, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null, KartGecisDto? Gecis = null);
/// <summary>Eski kayıttan geçirilmiş kartın denetim izi ve ilk sürüm kalıntısı (Kural: "EtkiTarihi" ilk sürüm, "IslemTarihi").</summary>
public record KartGecisDto(string Kural, string? Aciklama, KartGecisKaydi? Onizleme, decimal RaporDisiEskiDusumTutari = 0,
    DateOnly? RaporDisiIlkDusumTarihi = null, DateOnly? RaporDisiSonDusumTarihi = null, decimal TahminiKasaFarki = 0, string? Uyari = null);
/// <summary>Onaylanan kart geçişinin önizleme özeti ve girilen tutarlar.</summary>
public record KartGecisKaydi(DateOnly OnayTarihi, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, decimal SistemKartBorcu,
    decimal EskiKuraldaIslenenTutar, decimal BekleyenEskiDusumTutari, DateOnly? SonBekleyenDusumTarihi, decimal OnerilenKasadaSayilanTutar);
public record KartEkstreDto(int Id, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Borc, decimal Odenen, decimal Kalan, decimal? AsgariOdeme, decimal? AsgariKalan = null);
/// <param name="KasadaSayilanDuzeltme">Eski borç devrine yapılan iadede kasada önceden sayılan tutarın iade tarihinde kasaya
/// geri dönen kısmı; diğer satırlarda (ve eski sunucuda) 0.</param>
public record KartHarcamaDto(int Id, int? IslemId, DateOnly Tarih, string Aciklama, decimal Tutar, int TaksitSayisi, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null, decimal KasadaSayilanDuzeltme = 0);
/// <param name="AvansKaynakOdemeId">Kilitli döneme düşen avansın dağıtım kaydında (tutarı 0) avansı yatıran ödeme; bu kayıt
/// ayrıca iptal edilemez. Diğer ödemelerde null.</param>
public record KartTakipOdemeDto(int Id, DateOnly Tarih, decimal Tutar, decimal KasaEtkisi, string? Not, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null, int? AvansKaynakOdemeId = null);
/// <summary>Geçişli kartın eski borç devrinin gerekçeli düzeltmesi: etkin devir (<paramref name="HarcamaId"/>) iptal edilir,
/// aynı tarihle yeni kalan borç, kasada önceden sayılan tutar ve kanal paylarıyla yazılır.</summary>
public record KartDevirDuzeltYaz(Guid IstekId, int Surum, int? HarcamaId, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, IReadOnlyList<KanalPayYaz> Dagilimlar, string Aciklama);
/// <summary>Geçişli kartın eski borç devri ve düzeltme sınırları. Önerilen = max(0, min(kalan borç, sistem borcu) − rapor
/// dışı); en az = max(0, önerilen − max(0, açılış borcu)). Engel doluysa düzeltme yapılamaz.</summary>
public record KartDevirDto(int? HarcamaId, DateOnly Tarih, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, decimal IadeDuzeltmesi, IReadOnlyList<TakipKanalPayi> Dagilimlar,
    string Kural, decimal SistemKartBorcu, decimal RaporDisiTutar, decimal AcilisBorcu, decimal OnerilenKasadaSayilanTutar, decimal EnAzKasadaSayilanTutar, bool Duzeltilebilir, string? Engel);
public record KartOdemeOnizlemeDto(decimal Tutar, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<KartEkstreOdemePayi> Ekstreler);
public record KartEkstreOdemePayi(int EkstreId, decimal Tutar);
public record KrediTakipYaz(Guid IstekId, string Ad, decimal CekilenTutar, DateOnly CekimTarihi, DateOnly IlkTaksitTarihi, int TaksitSayisi, decimal AylikOdeme, IReadOnlyList<int> KanalIdleri, bool MevcutKredi = false);
public record KrediTaksitYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, string? Not, bool Iptal, string Aciklama);
public record KrediKapatYaz(Guid IstekId, int Surum, DateOnly Tarih, decimal Tutar, string Aciklama);
public record KrediGecisYaz(Guid IstekId, int Surum, DateOnly Baslangic, IReadOnlyList<int> KanalIdleri, string Aciklama, bool Onay);
public record KrediTakipDto(int Id, int Surum, string Ad, bool YeniTakip, bool Aktif, DateOnly? TakipBaslangic, decimal CekilenTutar, DateOnly CekimTarihi, decimal KalanPlanliOdeme, IReadOnlyList<TakipKanalPayi> KanalPaylari, IReadOnlyList<KrediPlanTaksitDto> Taksitler);
public record KrediPlanTaksitDto(int Id, int No, DateOnly Tarih, decimal Tutar, string Durum, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar);
/// <summary>Eski kayıttan yeni takibe geçiş önizlemesi.</summary>
/// <param name="GenelKasaAnlikFarki">Kart geçişinde kasada önceden sayılan tutar eksi önerilen tutar: genel kasaya
/// kalıcı etki. 0: tutarlı; pozitif: kasadan hiç düşmeyecek tutar; negatif: ödendiğinde ikinci kez düşecek tutar.</param>
/// <param name="KanalAnlikFarki">Aynı farkın kanallara yansıyan kısmı (devir kanal payı yoksa 0).</param>
/// <param name="KabulEdilebilir">Kartta önceden sayılan tutar EnAzKasadaSayilanTutar ile önerilen arasında; dışındaysa onay 409 döner.</param>
/// <param name="SistemKartBorcu">Açılış borcu + eski kart giderleri − eski kart ödemeleri. Kredi geçişinde ve
/// eski sunucuda null; aşağıdaki kart alanları da öyle.</param>
/// <param name="EskiKuraldaIslenenTutar">Başlangıçtan önce eski ay sonu kuralıyla kasadan düşen/düşecek eski gider toplamı.</param>
/// <param name="BekleyenEskiDusumTutari">SonBekleyenDusumTarihi'ne kadar ay sonlarında düşmeye devam edecek eski gider toplamı.</param>
/// <param name="OnerilenKasadaSayilanTutar">max(0, min(kalan borç, sistem kart borcu)).</param>
/// <param name="EnAzKasadaSayilanTutar">max(0, önerilen − max(0, açılış borcu)); altı ödemede ikinci kez düşer.</param>
public record TakipGecisDto(string Kaynak, int KaynakId, DateOnly Baslangic, decimal GenelKasaAnlikFarki, decimal KanalAnlikFarki, decimal EskiKasadaSayilanTutar, IReadOnlyList<string> Aciklamalar, bool KabulEdilebilir,
    decimal? SistemKartBorcu = null, decimal? EskiKuraldaIslenenTutar = null, decimal? BekleyenEskiDusumTutari = null, DateOnly? SonBekleyenDusumTarihi = null, decimal? OnerilenKasadaSayilanTutar = null,
    decimal? EnAzKasadaSayilanTutar = null);
public record TakipOzetDto(DateOnly Tarih, decimal KartBorcu, decimal KalanKrediPlani, IReadOnlyList<TakipOlayDto> Olaylar, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null, decimal KartAlacakBakiyesi = 0);
public record TakipOlayDto(string Kaynak, int KaynakId, int KalemId, string Ad, DateOnly Tarih, decimal Tutar, string Tur, bool OtomatikKasa);

public interface IFinansTakipApi
{
    Task<IReadOnlyList<KartTakipDto>> TakipKartlarAsync();
    Task<KartTakipDto> TakipKartAsync(int id);
    Task<KartTakipDto> TakipKartKaydetAsync(int? id, KartTakipYaz g);
    Task<KartTakipDto> TakipKartDurumAsync(int id, TakipDurumYaz g);
    Task<KartTakipDto> TakipHarcamaKaydetAsync(int id, KartHarcamaYaz g);
    Task<KartTakipDto> TakipHarcamaIptalAsync(int id, int harcamaId, TakipIptalYaz g);
    Task<KartTakipDto> TakipEkstreKaydetAsync(int id, int ekstreId, KartEkstreYaz g);
    Task<KartOdemeOnizlemeDto> TakipOdemeOnizlemeAsync(int id, KartTakipOdemeYaz g);
    Task<KartTakipDto> TakipOdemeKaydetAsync(int id, KartTakipOdemeYaz g);
    Task<KartTakipDto> TakipOdemeIptalAsync(int id, int odemeId, TakipIptalYaz g);
    Task<TakipGecisDto> TakipKartGecisOnizlemeAsync(int id, KartGecisYaz g);
    Task<KartTakipDto> TakipKartGecisAsync(int id, KartGecisYaz g);
    Task<KartDevirDto> TakipKartDevirAsync(int id);
    Task<KartTakipDto> TakipKartDevirDuzeltAsync(int id, KartDevirDuzeltYaz g);
    Task<IReadOnlyList<KrediTakipDto>> TakipKredilerAsync();
    Task<KrediTakipDto> TakipKrediAsync(int id);
    Task<KrediTakipDto> TakipKrediKaydetAsync(KrediTakipYaz g);
    Task<KrediTakipDto> TakipKrediDurumAsync(int id, TakipDurumYaz g);
    Task<KrediTakipDto> TakipTaksitKaydetAsync(int id, int taksitId, KrediTaksitYaz g);
    Task<KrediTakipDto> TakipKrediKapatAsync(int id, KrediKapatYaz g);
    Task<TakipGecisDto> TakipKrediGecisOnizlemeAsync(int id, KrediGecisYaz g);
    Task<KrediTakipDto> TakipKrediGecisAsync(int id, KrediGecisYaz g);
    Task<TakipOzetDto> TakipOzetAsync(int gun = 30);
}
