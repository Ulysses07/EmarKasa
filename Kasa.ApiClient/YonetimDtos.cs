namespace Kasa.ApiClient;

public record SifreDegistirYaz(string MevcutSifre, string YeniSifre);
public record SifreKurtarYaz(string Kullanici, string Kod, string YeniSifre);
public record KurtarmaKoduDto(string Kod);
public record SurumDto(string Surum, string MinimumIstemci, string? IndirmeAdresi, string? Notlar);
public record YedekDurumuDto(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata);
public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi);
public record IndirilenDosya(byte[] Icerik, string DosyaAdi, string IcerikTuru);

public interface IYonetimApi
{
    Task SifreDegistirAsync(SifreDegistirYaz g);
    Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string mevcutSifre);
    Task SifreKurtarAsync(SifreKurtarYaz g);
    Task<SurumDto> SurumAsync();
    Task<YedekDurumuDto> YedekDurumuAsync();
    Task<IndirilenDosya> YedekIndirAsync();
    Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int alisId);
    Task<BelgeDto> BelgeYukleAsync(int alisId, string dosyaAdi, string icerikTuru, byte[] icerik, int? odemeId = null);
    Task<IndirilenDosya> BelgeIndirAsync(int belgeId);
    Task BelgeSilAsync(int belgeId);
    Task<IndirilenDosya> DisariAktarAsync(DateOnly baslangic, DateOnly bitis, string? kanal, string bicim);
}
