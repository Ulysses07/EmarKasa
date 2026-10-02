namespace Kasa.Api;

/// <summary>Yeni ya da düzeltilen çek / senet (POST, PUT /api/takip/cekler). <paramref name="Kanal"/>: verilen çekte ödeneceği kasanın
/// adı ya da "Ortak" (zorunlu); alınan çekte boş (kasa harekette seçilir). <paramref name="Konum"/>: yalnız alınan çekte; boşsa Elde.
/// Yeni kayıtta <paramref name="Surum"/> 0'dır.</summary>
public record CekYaz(Guid IstekId, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    string? Kanal, bool Teminat, string? Konum, string? Not);

/// <summary>Çek hareketi (POST /api/takip/cekler/{id}/hareketler). <paramref name="Kanal"/>: alınan çekin tahsilat, ciro ve kırdırmasında
/// kasanın adı; dönüşte ve verilen çekte boş (sunucu ters çevrilen hareketin ya da çekin kasasını kullanır). <paramref name="NetTutar"/>
/// yalnız kırdırmada; <paramref name="Karsi"/> ciroda ve kırdırmada zorunlu.</summary>
public record CekHareketYaz(Guid IstekId, int Surum, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, string? Kanal, string? Karsi);

/// <summary>Çeki silme ya da son hareketi geri alma (DELETE gövdesi): tekrar koruması ve sürüm.</summary>
public record CekSilYaz(Guid IstekId, int Surum);

public record CekHareketDto(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, int? KanalId, string? Kanal, string? Karsi);

/// <summary>Çek ve hesaplanan durumu. <paramref name="Kanal"/>: verilen çekin kasası (kanal adı ya da "Ortak"); alınanda null.
/// <paramref name="Durum"/> (CekDurumlari) ve <paramref name="Kalan"/> hareketlerden hesaplanır; <paramref name="IzinliHareketler"/>
/// şu an girilebilecek hareket türleridir. <paramref name="Uyari"/>: aynı yön, banka ve numarayla başka kayıt varsa uyarı; yoksa
/// null (kaydı engellemez).</summary>
public record CekDto(int Id, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    int? KanalId, string? Kanal, bool Teminat, string? Konum, string? Not, string Durum, decimal Kalan, IReadOnlyList<string> IzinliHareketler,
    IReadOnlyList<CekHareketDto> Hareketler, string? Uyari);

public record CekOzetKalemi(int Adet, decimal Toplam);

/// <summary>Çek panel özeti (GET /api/takip/cekler/ozet). Teminat çekleri ve kapanmış çekler dışarıdadır; tutarlar kalandır. Otuz gün
/// bugünden bugün+30'a kadardır (ikisi dahil). Vadesi geçmiş: vadesi bugünden önce, portföyde ya da kısmen tahsil edilmiş alınan çek.</summary>
public record CekOzetDto(DateOnly Tarih, CekOzetKalemi PortfoydekiAlinan, CekOzetKalemi Alinan30, CekOzetKalemi Verilen30, CekOzetKalemi VadesiGecmis);
