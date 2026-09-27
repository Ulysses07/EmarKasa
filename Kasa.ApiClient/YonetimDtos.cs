namespace Kasa.ApiClient;

public record SifreDegistirYaz(string MevcutSifre, string YeniSifre);
public record SifreKurtarYaz(string Kullanici, string Kod, string YeniSifre);
public record KurtarmaKoduDto(string Kod);
public record SurumDto(string Surum, string MinimumIstemci, string? IndirmeAdresi, string? Notlar);
/// <summary><see cref="RotasyonUyarisi"/>: sunucunun son rotasyonda silemediği eski yedek; yedeğin kendisi başarılıdır.
/// Alan yeni sunucularda gelir, eski sunucuda boş kalır.</summary>
public record YedekDurumuDto(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata, string? RotasyonUyarisi = null);
public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi);
/// <summary>Akışla hedefe yazılan dosyanın sunucudan gelen (temizlenmiş) adı, türü ve yazılan bayt sayısı.</summary>
public record IndirmeBilgisi(string DosyaAdi, string IcerikTuru, long Boyut);

public interface IYonetimApi
{
    Task SifreDegistirAsync(SifreDegistirYaz g);
    Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string mevcutSifre);
    Task SifreKurtarAsync(SifreKurtarYaz g);
    Task<SurumDto> SurumAsync();
    Task<YedekDurumuDto> YedekDurumuAsync();
    // Dosya uçları uzun süre sınırıyla çalışır; indirmeler belleğe alınmadan verilen akışa yazılır.
    Task<IndirmeBilgisi> YedekIndirAsync(Stream hedef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int alisId);
    Task<BelgeDto> BelgeYukleAsync(int alisId, string dosyaAdi, string icerikTuru, byte[] icerik, int? odemeId = null, CancellationToken cancellationToken = default);
    Task<IndirmeBilgisi> BelgeIndirAsync(int belgeId, Stream hedef, CancellationToken cancellationToken = default);
    Task BelgeSilAsync(int belgeId);
    Task<IndirmeBilgisi> DisariAktarAsync(DateOnly baslangic, DateOnly bitis, string? kanal, string bicim, Stream hedef, CancellationToken cancellationToken = default);
}
