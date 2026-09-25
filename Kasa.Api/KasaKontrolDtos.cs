namespace Kasa.Api;

public record KartMasrafYaz(Guid IstekId, int Surum, int EkstreId, DateOnly Tarih, decimal Tutar, string Aciklama, string? DagilimOzeti = null);
public record KartMasrafOnizlemeDto(int KartId, int EkstreId, DateOnly Tarih, decimal Tutar, decimal DevredenBorc, IReadOnlyList<TakipKanalPayi> Dagilimlar, string DagilimOzeti);
public record KasaEsikYaz(int Surum, decimal Tutar, bool Etkin);
public record KasaEsikDto(int KanalId, string Kanal, int Surum, decimal Tutar, bool Etkin, decimal Bakiye, bool EsikAltinda);
public record KasaKontrolOnizle(decimal GercekBakiye, string? Not = null);
public record KasaKontrolYaz(Guid IstekId, decimal GercekBakiye, string KontrolOzeti, string? Not = null);
public record KasaKontrolOnizlemeDto(decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string KontrolOzeti);
public record KasaKontrolDto(int Id, DateTimeOffset Kaydedildi, decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string? Not);
