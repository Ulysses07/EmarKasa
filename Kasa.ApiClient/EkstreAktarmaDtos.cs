namespace Kasa.ApiClient;

public record EkstreBelgeOzetDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, int SatirSayisi, int KayitSayisi);
public record EkstreBelgeDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, IReadOnlyList<string> Uyarilar, IReadOnlyList<EkstreOkunanSatir> Satirlar, IReadOnlyList<EkstreKayitDto> Kayitlar);
public record EkstreOkunanSatir(int No, int Sayfa, string KaynakSatir, DateOnly? Tarih, string Aciklama, decimal? Tutar, string Yon, string OnerilenIslem, string Sinif, string ParaBirimi, IReadOnlyList<string> Uyarilar);
public record EkstreKayitDto(int Id, int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? KrediKartiId, int? IslemId, int? KartHarcamaId, int? KartOdemeId, bool Iptal);
public record EkstreSatirYaz(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KrediKartiId = null, int? KaynakHarcamaId = null);
public record EkstreKaydetYaz(Guid IstekId, int Surum, IReadOnlyList<EkstreSatirYaz> Satirlar, string? OnizlemeOzeti = null, bool TekrarOnay = false);
public record EkstreSatirOnizleme(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti, decimal KasaEtkisi, IReadOnlyList<EkstreSatirOnizleme> Satirlar, IReadOnlyList<string> Uyarilar, bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId, string Aciklama);

public interface IEkstreAktarmaApi
{
    Task<IReadOnlyList<EkstreBelgeOzetDto>> EkstreBelgelerAsync(int? beforeId = null);
    Task<EkstreBelgeDto> EkstreBelgeAsync(int id);
    Task<EkstreBelgeDto> EkstreKaynakBelgeAsync(int kayitId);
    Task<EkstreBelgeDto> EkstreYukleAsync(byte[] icerik, string dosyaAdi, string kaynak, string banka, string hesapAdi, int? kartId, CancellationToken cancellationToken = default);
    Task<IndirilenDosya> EkstreDosyaAsync(int id);
    Task<EkstreOnizlemeDto> EkstreOnizlemeAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKaydetAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKayitIptalAsync(int id, int kayitId, EkstreIptalYaz g);
}
