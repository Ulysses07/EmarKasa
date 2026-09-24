using Kasa.Api.Data;

namespace Kasa.Api;

public record PosTanimDto(int Id, string Ad, PosSaglayici Saglayici, int? KanalId, string? KanalAd,
    decimal KomisyonOrani, int BlokajGunu, bool Aktif);

/// <param name="EskiSatislaraUygula">Kanal değişiyorsa bu POS'un kayıtlı satışları da yeni kanala geçsin mi
/// (yanlış girilmiş kanalı düzeltmek için). Varsayılan: hayır — geçmiş ayların kanal dökümü değişmez.</param>
public record PosTanimYazDto(string? Ad, PosSaglayici Saglayici, int? KanalId, decimal KomisyonOrani, int BlokajGunu, bool Aktif = true,
    bool EskiSatislaraUygula = false);

/// <summary>
/// POS satışı ve hesaplanan alanları: Komisyon = brüt × oran / 100 (kuruşa yuvarlı), Net = brüt − komisyon,
/// Valör = tarih + blokaj günü, Bloke = bugün itibarıyla bankada bekliyor.
/// </summary>
public record PosSatisDto(int Id, DateOnly Tarih, int PosId, string PosAd, string Kanal,
    decimal BrutTutar, decimal KomisyonOrani, decimal Komisyon, decimal Net, int BlokajGunu, DateOnly Valor, bool Bloke, string? Not);

/// <summary>Oran/blokaj boşsa POS tanımındaki değer kullanılır.</summary>
public record PosSatisYazDto(DateOnly Tarih, int PosId, decimal BrutTutar, decimal? KomisyonOrani, int? BlokajGunu, string? Not);

public record PosKanalOzetiDto(string Kanal, decimal Brut, decimal Komisyon, decimal Net, int Adet);
public record PosValorGunuDto(DateOnly Valor, decimal Net, int Adet);
/// <summary>Bir kanalın bugün bankada bloke duran neti (satış ayından bağımsız).</summary>
public record PosKanalBlokeDto(string Kanal, decimal Net, int Adet);

/// <summary>
/// POS özeti (yalnız bilgi; kasa ve kârlılığa girmez): bugün bankada bloke duran net (toplam ve kanal
/// kanal), valör dökümü ve seçilen ayın kanal başına komisyonu.
/// </summary>
public record PosOzetDto(int Yil, int Ay, DateOnly Bugun, decimal BlokeNet, int BlokeAdet,
    IReadOnlyList<PosValorGunuDto> Valorler, IReadOnlyList<PosKanalOzetiDto> Kanallar,
    decimal ToplamBrut, decimal ToplamKomisyon, decimal ToplamNet, IReadOnlyList<PosKanalBlokeDto> BlokeKanallar);
