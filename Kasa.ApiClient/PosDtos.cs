namespace Kasa.ApiClient;

public enum PosSaglayici { BankaPosu, Iyzico, PayTr, Diger }

public record PosTanimDto(int Id, string Ad, PosSaglayici Saglayici, int? KanalId, string? KanalAd,
    decimal KomisyonOrani, int BlokajGunu, bool Aktif);
/// <param name="EskiSatislaraUygula">Kanal değişiyorsa POS'un kayıtlı satışları da yeni kanala geçsin mi (yanlış
/// girilmiş kanalı düzeltmek için). Hayır ise geçmiş satışlar ve geçmiş ayların kanal dökümü olduğu gibi kalır.</param>
public record PosTanimYaz(string Ad, PosSaglayici Saglayici, int? KanalId, decimal KomisyonOrani, int BlokajGunu, bool Aktif = true,
    bool EskiSatislaraUygula = false);

/// <summary>POS satışı + sunucunun hesapladığı komisyon, net, valör ve bugün bloke mi.</summary>
public record PosSatisDto(int Id, DateOnly Tarih, int PosId, string PosAd, string Kanal,
    decimal BrutTutar, decimal KomisyonOrani, decimal Komisyon, decimal Net, int BlokajGunu, DateOnly Valor, bool Bloke, string? Not);
/// <summary>Oran/blokaj null ise sunucu POS tanımındakini (düzenlemede kayıtlı olanı) kullanır.</summary>
public record PosSatisYaz(DateOnly Tarih, int PosId, decimal BrutTutar, decimal? KomisyonOrani, int? BlokajGunu, string? Not);

public record PosKanalOzetiDto(string Kanal, decimal Brut, decimal Komisyon, decimal Net, int Adet);
public record PosValorGunuDto(DateOnly Valor, decimal Net, int Adet);
/// <summary>Bir kanalın bugün bankada bloke duran neti (satış ayından bağımsız).</summary>
public record PosKanalBlokeDto(string Kanal, decimal Net, int Adet);
/// <summary>POS özeti: yalnız bilgi; kasa ve kârlılığa girmez. <see cref="BlokeKanallar"/>: bugün bloke duranın kanal dökümü.</summary>
public record PosOzetDto(int Yil, int Ay, DateOnly Bugun, decimal BlokeNet, int BlokeAdet,
    IReadOnlyList<PosValorGunuDto> Valorler, IReadOnlyList<PosKanalOzetiDto> Kanallar,
    decimal ToplamBrut, decimal ToplamKomisyon, decimal ToplamNet, IReadOnlyList<PosKanalBlokeDto>? BlokeKanallar = null);
