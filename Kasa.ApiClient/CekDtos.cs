namespace Kasa.ApiClient;

/// <summary>Yeni ya da düzeltilen çek / senet. Kanal: verilende kasa adı ya da "Ortak", alınanda boş. Alanların kod değerleri
/// Kasa.Core.Kodlar sabitleridir (CekTurleri, CekYonleri, CekKonumlari).</summary>
public record CekYaz(Guid IstekId, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    string? Kanal, bool Teminat, string? Konum, string? Not);
/// <summary>Çek hareketi (CekHareketTurleri). Kanal yalnız alınan çekin tahsilat, ciro ve kırdırmasında; NetTutar yalnız kırdırmada.</summary>
public record CekHareketYaz(Guid IstekId, int Surum, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, string? Kanal, string? Karsi);
/// <summary>Çeki silme ya da son hareketi geri alma gövdesi.</summary>
public record CekSilYaz(Guid IstekId, int Surum);
public record CekHareketDto(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, int? KanalId, string? Kanal, string? Karsi);
/// <summary>Çek ve sunucunun hesapladığı durum (CekDurumlari), kalan, girilebilecek hareketler ve aynı çek uyarısı.</summary>
public record CekDto(int Id, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    int? KanalId, string? Kanal, bool Teminat, string? Konum, string? Not, string Durum, decimal Kalan, IReadOnlyList<string> IzinliHareketler,
    IReadOnlyList<CekHareketDto> Hareketler, string? Uyari);
public record CekOzetKalemi(int Adet, decimal Toplam);
/// <summary>Çek panel özeti: teminatsız ve açık çeklerin kalan tutarları. VadesiGecmis alınan, VerilenVadesiGecmis verilen (ödenmemiş)
/// çeklerdir.</summary>
public record CekOzetDto(DateOnly Tarih, CekOzetKalemi PortfoydekiAlinan, CekOzetKalemi Alinan30, CekOzetKalemi Verilen30, CekOzetKalemi VadesiGecmis,
    CekOzetKalemi VerilenVadesiGecmis);

/// <summary>Çek ve senet uçları (/api/takip/cekler).</summary>
public interface ICekApi
{
    /// <summary>Liste; süzgeçler isteğe bağlıdır (yon: CekYonleri, durum: CekSuzgecleri, vade aralığı iki ucu dahil).</summary>
    Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null);
    Task<CekDto> CekAsync(int id);
    Task<CekOzetDto> CekOzetAsync();
    /// <summary>id null ise ekler, değilse düzeltir.</summary>
    Task<CekDto> CekKaydetAsync(int? id, CekYaz g);
    Task CekSilAsync(int id, CekSilYaz g);
    Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g);
    /// <summary>Son hareketi geri alır.</summary>
    Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g);
}
