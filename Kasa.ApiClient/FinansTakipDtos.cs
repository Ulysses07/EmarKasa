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
public record TakipGecisDto(string Kaynak, int KaynakId, DateOnly Baslangic, decimal GenelKasaAnlikFarki, decimal KanalAnlikFarki, decimal EskiKasadaSayilanTutar, IReadOnlyList<string> Aciklamalar, bool KabulEdilebilir);
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
