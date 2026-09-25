namespace Kasa.ApiClient;

public record BenzerlikYaz(string Tur, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, string? Kanal = null, int? AlisId = null);
public record BenzerKayitDto(string Kaynak, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, int? AlisId);
public interface IBenzerKayitApi
{
    Task<IReadOnlyList<BenzerKayitDto>> BenzerKayitlarAsync(BenzerlikYaz g);
}
