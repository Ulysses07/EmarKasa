namespace Kasa.Core;

/// <summary>Banka kredisi tanımı. Çekilen ana para genel kasaya gelir,
/// aylık taksitler seçilen kanala (veya <see cref="Kanallar.Ortak"/>) gider olarak yansır.</summary>
public record Kredi(
    string Ad,
    decimal CekilenTutar,
    DateOnly CekimTarihi,
    int TaksitSayisi,
    decimal AylikOdeme,
    int OdemeGunu,
    string Kanal);   // gerçek kanal adı VEYA Kanallar.Ortak
