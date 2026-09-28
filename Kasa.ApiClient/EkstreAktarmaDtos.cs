namespace Kasa.ApiClient;

public record EkstreBelgeOzetDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, int SatirSayisi, int KayitSayisi);
public record EkstreBelgeDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, IReadOnlyList<string> Uyarilar, IReadOnlyList<EkstreOkunanSatir> Satirlar, IReadOnlyList<EkstreKayitDto> Kayitlar);
public record EkstreOkunanSatir(int No, int Sayfa, string KaynakSatir, DateOnly? Tarih, string Aciklama, decimal? Tutar, string Yon, string OnerilenIslem, string Sinif, string ParaBirimi, IReadOnlyList<string> Uyarilar);
/// <summary><paramref name="IptalAciklamasi"/> ve <paramref name="IptalZamani"/> yalnız iptal edilmiş satırda doludur; sürüm öncesi
/// iptalin anı bilinmez (null). Alanları taşımayan eski sunucuda ikisi de null. <paramref name="EslesmeTuru"/>/<paramref name="EslesmeId"/>:
/// satırın bağlı olduğu mevcut kayıt ('Gider', 'KartHarcama', 'KartTaksidi', 'KartOdeme'); 'Eslestir' satırı yalnız bağdır, kaydı
/// alış ödemesine bağlanan Gider/KartHarcama satırında da dolar. <paramref name="EslesmeDurumu"/> 'Eslesti' ya da hedef silinmiş/iptal
/// edilmişse 'KayitYok'. Eski sunucuda null.</summary>
public record EkstreKayitDto(int Id, int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? KrediKartiId, int? IslemId, int? KartHarcamaId, int? KartOdemeId, bool Iptal, string? IptalAciklamasi = null, DateTimeOffset? IptalZamani = null,
    string? EslesmeTuru = null, int? EslesmeId = null, string? EslesmeDurumu = null);
/// <summary><paramref name="IslemTuru"/> 'Eslestir' ise <paramref name="EslesenKayitTuru"/>/<paramref name="EslesenKayitId"/> ile gösterilen
/// mevcut kayda bağlanır (DagilimTuru 'Eslesme', dağılım boş); yeni kayıt üretmez, kasa ve kart borcu değişmez.</summary>
public record EkstreSatirYaz(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KrediKartiId = null, int? KaynakHarcamaId = null,
    string? EslesenKayitTuru = null, int? EslesenKayitId = null);
public record EkstreKaydetYaz(Guid IstekId, int Surum, IReadOnlyList<EkstreSatirYaz> Satirlar, string? OnizlemeOzeti = null, bool TekrarOnay = false);
public record EkstreSatirOnizleme(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti, decimal KasaEtkisi, IReadOnlyList<EkstreSatirOnizleme> Satirlar, IReadOnlyList<string> Uyarilar, bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId, string Aciklama);
/// <summary>Eşleşme adayı sorgusu: satırın (düzenlenmiş) tarihi ve tutarı.</summary>
public record EkstreEslesmeAdayiSorgu(DateOnly Tarih, decimal Tutar);
/// <summary>Ekstre satırının eşleştirilebileceği mevcut kayıt; <see cref="Tur"/> ve <see cref="Id"/> EkstreSatirYaz.EslesenKayitTuru/Id
/// olarak gönderilir. Taksitte <see cref="Id"/> taksidin, <see cref="HarcamaId"/> harcamanın kimliğidir.</summary>
public record EkstreEslesmeAdayiDto(string Tur, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, string? KanalEtiketi = null, int? AlisId = null, int? EkstreKayitId = null,
    int? HarcamaId = null, int? TaksitNo = null, int? TaksitSayisi = null);

public interface IEkstreAktarmaApi
{
    Task<IReadOnlyList<EkstreBelgeOzetDto>> EkstreBelgelerAsync(int? beforeId = null);
    Task<EkstreBelgeDto> EkstreBelgeAsync(int id);
    Task<EkstreBelgeDto> EkstreKaynakBelgeAsync(int kayitId);
    Task<EkstreBelgeDto> EkstreYukleAsync(byte[] icerik, string dosyaAdi, string kaynak, string banka, string hesapAdi, int? kartId, CancellationToken cancellationToken = default);
    /// <summary>Kaynak PDF'i akışla <paramref name="hedef"/>'e yazar.</summary>
    Task<IndirmeBilgisi> EkstreDosyaAsync(int id, Stream hedef, CancellationToken cancellationToken = default);
    Task<EkstreOnizlemeDto> EkstreOnizlemeAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKaydetAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKayitIptalAsync(int id, int kayitId, EkstreIptalYaz g);
    /// <summary>Satırın eşleştirilebileceği mevcut kayıtlar (aynı tutar, ±3 gün; belge türüne uygun, başka satıra bağlanmamış), en çok 20.</summary>
    Task<IReadOnlyList<EkstreEslesmeAdayiDto>> EkstreEslesmeAdaylariAsync(int id, EkstreEslesmeAdayiSorgu g);
}
