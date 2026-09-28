using System.Text.Json.Serialization;

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
public record KartTakipDto(int Id, int Surum, string Ad, bool YeniTakip, bool Aktif, DateOnly? TakipBaslangic, int KesimGunu, int SonOdemeGunu, decimal Limit, decimal Borc, decimal EkstreBorc, IReadOnlyList<KartEkstreDto> Ekstreler, IReadOnlyList<KartHarcamaDto> Harcamalar, IReadOnlyList<KartTakipOdemeDto> Odemeler, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null, KartGecisDto? Gecis = null);
/// <summary>Eski kayıttan yeni takibe geçirilmiş kartın denetim izi ve ilk sürüm kalıntısı.</summary>
/// <param name="Kural">Başlangıçtan önceki eski giderlerin düşüş kuralı: "EtkiTarihi" (ilk sürüm) veya "IslemTarihi".</param>
/// <param name="Aciklama">Onayda girilen geçiş açıklaması; ilk sürüm geçişlerinde saklanmadığından null.</param>
/// <param name="Onizleme">Onay anındaki önizleme özeti ve girilen tutarlar; ilk sürüm geçişlerinde null.</param>
/// <param name="RaporDisiEskiDusumTutari">İlk sürüm kuralında raporlara hiç girmeyen eski ay sonu düşümü; yeni kuralda 0.</param>
/// <param name="TahminiKasaFarki">İlk sürüm geçişinde girilen tutarlara göre K − (min(kalan borç, sistem borcu) − rapor dışı düşüm):
/// artı kasadan hiç düşmeyecek (kasa fazla görünür), eksi ödemede ikinci kez düşecek; yeni kuralda 0.</param>
/// <param name="Uyari">İlk sürüm geçişinde rapor dışı düşüm ya da tahmini fark varsa banka/kasa kayıtlarıyla doğrulama uyarısı.</param>
public record KartGecisDto(string Kural, string? Aciklama, KartGecisKaydi? Onizleme, decimal RaporDisiEskiDusumTutari = 0,
    DateOnly? RaporDisiIlkDusumTarihi = null, DateOnly? RaporDisiSonDusumTarihi = null, decimal TahminiKasaFarki = 0, string? Uyari = null);
/// <summary>Onaylanan kart geçişinin önizleme özeti (TakipKartlar.GecisOzetiJson): girilen kalan borç ve kasada
/// önceden sayılan tutar ile sistemin o anki toplamları (<see cref="KartGecisOzeti"/>).</summary>
public record KartGecisKaydi(DateOnly OnayTarihi, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, decimal SistemKartBorcu,
    decimal EskiKuraldaIslenenTutar, decimal BekleyenEskiDusumTutari, DateOnly? SonBekleyenDusumTarihi, decimal OnerilenKasadaSayilanTutar);
public record KartEkstreDto(int Id, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Borc, decimal Odenen, decimal Kalan, decimal? AsgariOdeme, decimal? AsgariKalan = null);
/// <param name="KasadaSayilanDuzeltme">Eski borç devrine yapılan iadede, kasada önceden sayılan tutarın bu iadeyle düşen ve
/// iade tarihinde kasaya geri dönen kısmı (finance-2); yalnız sıfırdan farklıyken yazılır.</param>
public record KartHarcamaDto(int Id, int? IslemId, DateOnly Tarih, string Aciklama, decimal Tutar, int TaksitSayisi, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] decimal KasadaSayilanDuzeltme = 0);
/// <param name="AvansKaynakOdemeId">Kilitli avans dağıtımı kaydında (tutarı 0) avansı dağıtılan kilitli dönem ödemesi
/// (finance-8); ayrıca iptal edilemez. Yalnız doluyken yazılır.</param>
public record KartTakipOdemeDto(int Id, DateOnly Tarih, decimal Tutar, decimal KasaEtkisi, string? Not, bool Iptal, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? EkstreKayitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AvansKaynakOdemeId = null);
/// <summary>Geçişli kartın eski borç devrinin düzeltmesi (finance-2). Etkin devir (<paramref name="HarcamaId"/>; devir yoksa
/// null) iptal edilir, aynı tarih ve açıklamayla yeni tutar, kasada önceden sayılan tutar ve kanal paylarıyla yazılır.
/// Açıklama gerekçe olarak denetim izine yazılır.</summary>
public record KartDevirDuzeltYaz(Guid IstekId, int Surum, int? HarcamaId, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, IReadOnlyList<KanalPayYaz> Dagilimlar, string Aciklama);
/// <summary>Geçişli kartın eski borç devri ve düzeltme sınırları (finance-2).</summary>
/// <param name="HarcamaId">Etkin devir harcaması; devir yoksa (kalan borç 0 girilmiş ya da eski sürümde iptal edilmiş) null.</param>
/// <param name="Tarih">Devrin tarihi (takip başlangıcı); düzeltilen devir de bu tarihle yazılır.</param>
/// <param name="IadeDuzeltmesi">Devrin iadeleriyle kasaya geri dönen, önceden sayılmış tutar.</param>
/// <param name="Kural">Geçiş kuralı: "IslemTarihi" ya da ilk sürüm "EtkiTarihi".</param>
/// <param name="SistemKartBorcu">Açılış borcu + başlangıçtan önceki eski kart giderleri − eski kart ödemeleri.</param>
/// <param name="RaporDisiTutar">İlk sürüm kuralında raporlara girmeyen eski düşüm; işlem tarihi kuralında 0.</param>
/// <param name="OnerilenKasadaSayilanTutar">Bugünkü kalan borç için önerilen: max(0, min(kalan borç, sistem borcu) − rapor dışı).</param>
/// <param name="EnAzKasadaSayilanTutar">max(0, önerilen − max(0, açılış borcu)).</param>
/// <param name="Engel">Düzeltme yapılamıyorsa nedeni (ödeme, iade ya da kilitli dönem); yapılabiliyorsa null.</param>
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
/// <param name="GenelKasaAnlikFarki">Kart geçişinde kasada önceden sayılan tutar (K) eksi önerilen tutar: geçişin genel
/// kasaya kalıcı etkisi. 0: tutarlı; pozitif: kasadan hiçbir zaman düşmeyecek tutar; negatif: ödendiğinde ikinci kez
/// düşecek tutar. Geçiş anında bugünkü kasa değişmez; fark ödeme ve ay sonu düşümleriyle ortaya çıkar.</param>
/// <param name="KanalAnlikFarki">Aynı farkın kanallara yansıyan kısmı. Devir kanal payı taşımıyorsa ödeme
/// "Dağılım bekliyor" olur ve kanal farkı 0'dır.</param>
/// <param name="KabulEdilebilir">Kartta önerilen − max(0, açılış borcu) ≤ K ≤ önerilen; dışındaki geçiş 409 ile reddedilir.</param>
/// <param name="SistemKartBorcu">Kart açılış borcu + eski kart giderleri − eski kart ödemeleri. Kredide null.</param>
/// <param name="EskiKuraldaIslenenTutar">Başlangıçtan önce eski ay sonu kuralıyla kasadan düşen/düşecek eski gider toplamı
/// (başlangıç ileri tarihteyse bir kısmı henüz düşmemiş olabilir).</param>
/// <param name="BekleyenEskiDusumTutari">Başlangıçtan önceki ama eski ay sonu düşümü başlangıçta/sonrasında olan gider
/// toplamı; yeni kuralda SonBekleyenDusumTarihi'ne kadar ay sonlarında düşmeye devam eder.</param>
/// <param name="OnerilenKasadaSayilanTutar">max(0, min(kalan borç, sistem kart borcu)).</param>
/// <param name="EnAzKasadaSayilanTutar">max(0, önerilen − max(0, açılış borcu)): altı ödemede ikinci kez düşer ve reddedilir.</param>
public record TakipGecisDto(string Kaynak, int KaynakId, DateOnly Baslangic, decimal GenelKasaAnlikFarki, decimal KanalAnlikFarki, decimal EskiKasadaSayilanTutar, IReadOnlyList<string> Aciklamalar, bool KabulEdilebilir,
    decimal? SistemKartBorcu = null, decimal? EskiKuraldaIslenenTutar = null, decimal? BekleyenEskiDusumTutari = null, DateOnly? SonBekleyenDusumTarihi = null, decimal? OnerilenKasadaSayilanTutar = null,
    decimal? EnAzKasadaSayilanTutar = null);
public record TakipOzetDto(DateOnly Tarih, decimal KartBorcu, decimal KalanKrediPlani, IReadOnlyList<TakipOlayDto> Olaylar, IReadOnlyList<TakipKanalPayi>? KanalKartBorclari = null, decimal KartAlacakBakiyesi = 0);
public record TakipOlayDto(string Kaynak, int KaynakId, int KalemId, string Ad, DateOnly Tarih, decimal Tutar, string Tur, bool OtomatikKasa);
internal record KartTaksitPayi(int TaksitId, decimal Tutar, decimal OncedenOdenen = 0m);
