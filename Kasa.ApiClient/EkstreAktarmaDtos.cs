namespace Kasa.ApiClient;

/// <summary>Ekstre yüklemede seçilebilen banka: <paramref name="Kod"/> yüklemede gönderilen ve belgede görünen değer,
/// <paramref name="Ad"/> görünen adı (GET api/ekstre-aktar/bankalar; liste yalnız sunucudadır).</summary>
public record EkstreBankaDto(string Kod, string Ad);
public record EkstreBelgeOzetDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, int SatirSayisi, int KayitSayisi, int YokSayilanSayisi = 0);
/// <summary><paramref name="YokSayilanlar"/>: kaydetmeden kapatılan satır numaraları; yok saymayı bilmeyen eski sunucuda null.
/// <paramref name="Atlananlar"/>: tutar içerdiği hâlde harekete alınmayan satırlar. <paramref name="YenidenOkunabilir"/>: hiç kaydı ve yok sayılan
/// satırı yok, saklanan PDF yeniden okunabilir.</summary>
public record EkstreBelgeDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, IReadOnlyList<string> Uyarilar, IReadOnlyList<EkstreOkunanSatir> Satirlar, IReadOnlyList<EkstreKayitDto> Kayitlar,
    IReadOnlyList<int>? YokSayilanlar = null, IReadOnlyList<EkstreAtlananSatir>? Atlananlar = null, bool YenidenOkunabilir = false);
/// <summary><paramref name="YonKaynagi"/>: yönün dayanağı (Kasa.Core.Kodlar.YonKaynaklari); bilinmiyorsa null. <paramref name="EkBilgi"/>: açıklamanın
/// altında gösterilen ek metin (ERP12: işlem türü, belge no).</summary>
public record EkstreOkunanSatir(int No, int Sayfa, string KaynakSatir, DateOnly? Tarih, string Aciklama, decimal? Tutar, string Yon, string OnerilenIslem, string Sinif, string ParaBirimi, IReadOnlyList<string> Uyarilar, string? YonKaynagi = null,
    string? EkBilgi = null);
/// <summary>PDF'de tutar içerdiği hâlde harekete alınmayan satır ve nedeni.</summary>
public record EkstreAtlananSatir(int Sayfa, string Metin, string Neden);
/// <summary>Satırları kaydetmeden kapatır (<paramref name="YokSay"/> true) ya da geri açar; kasa ve kart borcu değişmez.</summary>
public record EkstreYokSayYaz(Guid IstekId, int Surum, IReadOnlyList<int> SatirNolar, bool YokSay);
public record EkstreYenidenOkuYaz(Guid IstekId, int Surum);
/// <summary><paramref name="IptalAciklamasi"/> ve <paramref name="IptalZamani"/> yalnız iptal edilmiş satırda doludur; sürüm öncesi
/// iptalin anı bilinmez (null). Alanları taşımayan eski sunucuda ikisi de null. <paramref name="EslesmeTuru"/>/<paramref name="EslesmeId"/>:
/// satırın bağlı olduğu mevcut kayıt ('Gider', 'KartHarcama', 'KartTaksidi', 'KartOdeme'); 'Eslestir' satırı yalnız bağdır, kaydı
/// alış ödemesine bağlanan Gider/KartHarcama satırında da dolar. <paramref name="EslesmeDurumu"/> 'Eslesti' ya da hedef silinmiş/iptal
/// edilmişse 'KayitYok'. Eski sunucuda null. <paramref name="ErpCari"/>: kayda eşleşen ERP12 satırının carisi.</summary>
public record EkstreKayitDto(int Id, int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? KrediKartiId, int? IslemId, int? KartHarcamaId, int? KartOdemeId, bool Iptal, string? IptalAciklamasi = null, DateTimeOffset? IptalZamani = null,
    string? EslesmeTuru = null, int? EslesmeId = null, string? EslesmeDurumu = null, string? ErpCari = null);
/// <summary><paramref name="IslemTuru"/> 'Eslestir' ise <paramref name="EslesenKayitTuru"/>/<paramref name="EslesenKayitId"/> ile gösterilen
/// mevcut kayda bağlanır (DagilimTuru 'Eslesme', dağılım boş); yeni kayıt üretmez, kasa ve kart borcu değişmez.</summary>
public record EkstreSatirYaz(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KrediKartiId = null, int? KaynakHarcamaId = null,
    string? EslesenKayitTuru = null, int? EslesenKayitId = null);
public record EkstreKaydetYaz(Guid IstekId, int Surum, IReadOnlyList<EkstreSatirYaz> Satirlar, string? OnizlemeOzeti = null, bool TekrarOnay = false);
public record EkstreSatirOnizleme(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti, decimal KasaEtkisi, IReadOnlyList<EkstreSatirOnizleme> Satirlar, IReadOnlyList<string> Uyarilar, bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId, string Aciklama);
/// <summary>Eşleşme adayı sorgusu: satırın (düzenlenmiş) tarihi ve tutarı; ERP12 belgesinde satır numarası da gönderilir.</summary>
public record EkstreEslesmeAdayiSorgu(DateOnly Tarih, decimal Tutar, int? SatirNo = null);
/// <summary>Ekstre satırının eşleştirilebileceği mevcut kayıt; <see cref="Tur"/> ve <see cref="Id"/> EkstreSatirYaz.EslesenKayitTuru/Id
/// olarak gönderilir. Taksitte <see cref="Id"/> taksidin, <see cref="HarcamaId"/> harcamanın kimliğidir.</summary>
public record EkstreEslesmeAdayiDto(string Tur, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, string? KanalEtiketi = null, int? AlisId = null, int? EkstreKayitId = null,
    int? HarcamaId = null, int? TaksitNo = null, int? TaksitSayisi = null);

public interface IEkstreAktarmaApi
{
    Task<IReadOnlyList<EkstreKuralDto>> EkstreKurallarAsync() => Task.FromException<IReadOnlyList<EkstreKuralDto>>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    Task<EkstreKuralDto> EkstreKuralEkleAsync(EkstreKuralYaz g) => Task.FromException<EkstreKuralDto>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    Task<EkstreKuralDto> EkstreKuralDuzenleAsync(int id, EkstreKuralYaz g) => Task.FromException<EkstreKuralDto>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    Task EkstreKuralSilAsync(int id, int surum) => Task.FromException(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    Task<IReadOnlyList<EkstreKuralOnerisi>> EkstreOnerilerAsync(int id) => Task.FromException<IReadOnlyList<EkstreKuralOnerisi>>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>İşlem hafızası: anahtar ve yön başına son karar, en yeni önce.</summary>
    Task<IReadOnlyList<EkstreHafizaDto>> EkstreHafizaAsync() => Task.FromException<IReadOnlyList<EkstreHafizaDto>>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Anahtarın bugüne kadarki kararlarını hafızadan çıkarır; kayıtlar değişmez.</summary>
    Task EkstreHafizaUnutAsync(EkstreHafizaUnutYaz g) => Task.FromException(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Yükleme formunun banka seçenekleri (tek kaynak: sunucu).</summary>
    Task<IReadOnlyList<EkstreBankaDto>> EkstreBankalarAsync();
    Task<IReadOnlyList<EkstreBelgeOzetDto>> EkstreBelgelerAsync(int? beforeId = null);
    Task<EkstreBelgeDto> EkstreBelgeAsync(int id);
    Task<EkstreBelgeDto> EkstreKaynakBelgeAsync(int kayitId);
    /// <summary>PDF ya da Excel (.xlsx, .xls) yükler; hatalı Excel bütün hatalarıyla reddedilir (422, ileti satır satır).</summary>
    Task<EkstreBelgeDto> EkstreYukleAsync(byte[] icerik, string dosyaAdi, string kaynak, string banka, string hesapAdi, int? kartId, CancellationToken cancellationToken = default);
    /// <summary>Hiçbir satırı işlenmemiş ERP12 belgesini hareketleriyle siler.</summary>
    Task ErpBelgeSilAsync(int id) => Task.FromException(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Örnek Excel'i akışla <paramref name="hedef"/>'e yazar.</summary>
    Task<IndirmeBilgisi> EkstreSablonAsync(Stream hedef, CancellationToken cancellationToken = default) => Task.FromException<IndirmeBilgisi>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Kaynak dosyayı (PDF ya da Excel) akışla <paramref name="hedef"/>'e yazar.</summary>
    Task<IndirmeBilgisi> EkstreDosyaAsync(int id, Stream hedef, CancellationToken cancellationToken = default);
    Task<EkstreOnizlemeDto> EkstreOnizlemeAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKaydetAsync(int id, EkstreKaydetYaz g);
    Task<EkstreBelgeDto> EkstreKayitIptalAsync(int id, int kayitId, EkstreIptalYaz g);
    /// <summary>Satırları kaydetmeden kapatır ya da geri açar (mali etkisi yok).</summary>
    Task<EkstreBelgeDto> EkstreYokSayAsync(int id, EkstreYokSayYaz g) => Task.FromException<EkstreBelgeDto>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Hiç işlenmemiş belgenin saklanan PDF'ini yeniden okur.</summary>
    Task<EkstreBelgeDto> EkstreYenidenOkuAsync(int id, EkstreYenidenOkuYaz g, CancellationToken cancellationToken = default) => Task.FromException<EkstreBelgeDto>(new KasaApiException(System.Net.HttpStatusCode.NotFound));
    /// <summary>Satırın eşleştirilebileceği mevcut kayıtlar (aynı tutar, ±3 gün; belge türüne uygun, başka satıra bağlanmamış), en çok 20.</summary>
    Task<IReadOnlyList<EkstreEslesmeAdayiDto>> EkstreEslesmeAdaylariAsync(int id, EkstreEslesmeAdayiSorgu g);
}
