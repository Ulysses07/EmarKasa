namespace Kasa.ApiClient;

public record AylikGiderSablonYaz(Guid IstekId, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, DateOnly GecerliAy, bool Aktif = true);
public record AylikGiderSablonDto(int Id, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, DateOnly GecerliAy, bool Aktif);
public record AylikGiderAyDto(int Yil, int Ay, decimal PlanlananToplam, decimal OdenenToplam, IReadOnlyList<AylikGiderSatirDto> Kayitlar);
public record AylikGiderSatirDto(int SablonId, int SablonSurum, string Ad, string Tur, decimal Tutar, DateOnly PlanlananTarih, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Durum, int? OdemeId = null, DateOnly? OdemeTarihi = null, int? IslemId = null);
public record AylikGiderOdemeYaz(Guid IstekId, int Surum, int Yil, int Ay, DateOnly Tarih, string? Not = null);
public record AylikGiderIptalYaz(Guid IstekId, string Aciklama);
public record AyKilidiYaz(Guid IstekId, int Surum, int Yil, int Ay, string Aciklama);
public record AyKilidiDto(int Surum, DateOnly? KilitliSonTarih, IReadOnlyList<AyKilidiOlayDto> Gecmis);
public record AyKilidiOlayDto(int Id, DateOnly? OncekiSonTarih, DateOnly? YeniSonTarih, string Aciklama, DateTimeOffset Zaman);

public interface IAylikGiderApi
{
    Task<IReadOnlyList<AylikGiderSablonDto>> AylikGiderSablonlariAsync();
    Task<AylikGiderSablonDto> AylikGiderSablonKaydetAsync(int? id, AylikGiderSablonYaz girdi);
    Task<AylikGiderAyDto> AylikGiderlerAsync(int yil, int ay);
    Task<AylikGiderSatirDto> AylikGiderOdeAsync(int sablonId, AylikGiderOdemeYaz girdi);
    Task<AylikGiderSatirDto> AylikGiderIptalAsync(int odemeId, AylikGiderIptalYaz girdi);
    Task<AyKilidiDto> AyKilidiAsync();
    Task<AyKilidiDto> AyKilidiDegistirAsync(bool kapat, AyKilidiYaz girdi);
}
