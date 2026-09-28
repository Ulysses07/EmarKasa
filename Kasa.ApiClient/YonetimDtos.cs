namespace Kasa.ApiClient;

public record SifreDegistirYaz(string MevcutSifre, string YeniSifre);
public record SifreKurtarYaz(string Kullanici, string Kod, string YeniSifre);
public record KurtarmaKoduDto(string Kod);
public record SurumDto(string Surum, string MinimumIstemci, string? IndirmeAdresi, string? Notlar);
/// <summary><see cref="RotasyonUyarisi"/>: sunucunun son rotasyonda silemediği eski yedek; yedeğin kendisi başarılıdır.
/// Disk alanları (bayt; okunamazsa ya da eski sunucuda null): yedek dizininin ve belge deposunun (veri) diskindeki boş alan, yedeklerin
/// ve yedek aynasının toplam boyutu, yedekten sonra kalması gereken asgari boş alan. <see cref="DiskUyarisi"/>: boş alan asgarinin
/// altında ya da toplam boyut sınırı aşıldı; <see cref="BelgeUyarisi"/>: son yedekte bulunamayan belge içerikleri.
/// Alanlar yeni sunucularda gelir, eski sunucuda boş kalır.</summary>
public record YedekDurumuDto(bool OtomatikEtkin, DateTimeOffset? SonYedek, DateTimeOffset? SonDogrulama, string? Hata, string? RotasyonUyarisi = null,
    long? YedekDiskiBosAlanBayt = null, long? VeriDiskiBosAlanBayt = null, long? ToplamYedekBayt = null, long? AsgariBosAlanBayt = null,
    string? DiskUyarisi = null, string? BelgeUyarisi = null);
/// <summary>Alış belgesi. Yükleyen: rol ('editor'/'alici') ve görünen ad; eski belgelerde ve eski sunucuda null. Silinen belgeler yalnız
/// editörün silinenleri de isteyen listesinde (<see cref="IYonetimApi.BelgelerAsync"/>) silen ve gerekçesiyle gelir.</summary>
public record BelgeDto(int Id, int AlisId, int? OdemeId, string DosyaAdi, string IcerikTuru, long Boyut, DateTimeOffset Yuklendi,
    string? YukleyenRol = null, string? Yukleyen = null, bool Silindi = false, DateTimeOffset? SilinmeZamani = null,
    string? SilenRol = null, string? Silen = null, string? SilmeGerekcesi = null);
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
    /// <param name="silinenler">Yalnız editör: kaldırılmış belgeleri de (silen ve gerekçesiyle) getir.</param>
    Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int alisId, bool silinenler = false);
    Task<BelgeDto> BelgeYukleAsync(int alisId, string dosyaAdi, string icerikTuru, byte[] icerik, int? odemeId = null, CancellationToken cancellationToken = default);
    Task<IndirmeBilgisi> BelgeIndirAsync(int belgeId, Stream hedef, CancellationToken cancellationToken = default);
    /// <summary>Belgeyi kaldırır (yumuşak silme: içerik ve iz korunur). Editör için gerekçe zorunludur.</summary>
    Task BelgeSilAsync(int belgeId, string? gerekce = null);
    Task<IndirmeBilgisi> DisariAktarAsync(DateOnly baslangic, DateOnly bitis, string? kanal, string bicim, Stream hedef, CancellationToken cancellationToken = default);
}
