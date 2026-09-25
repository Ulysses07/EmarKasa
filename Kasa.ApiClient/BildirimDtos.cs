namespace Kasa.ApiClient;

public record BildirimDto(int Id, string Baslik, string Mesaj, DateOnly Tarih, bool Okundu, string Hedef, string Tur, int KaynakId);
public record BildirimAyarDto(bool Etkin, int Saat, int Dakika, string SaatDilimi, int Surum);
public record BildirimAyarYaz(bool Etkin, int Saat, int Dakika, int Surum);
public record PushAnahtarDto(bool Etkin, string? PublicKey);
public record BildirimCihaziDto(int Id, string CihazAdi, DateTimeOffset Olusturuldu, DateTimeOffset? SonBasarili, bool Etkin);
public interface IBildirimApi
{
    Task<IReadOnlyList<BildirimDto>> BildirimlerAsync();
    Task<BildirimAyarDto> BildirimAyarlariAsync();
    Task<BildirimAyarDto> BildirimAyarKaydetAsync(BildirimAyarYaz g);
    Task BildirimOkunduAsync(int id);
    Task<PushAnahtarDto> BildirimAnahtariAsync();
    Task<IReadOnlyList<BildirimCihaziDto>> BildirimCihazlariAsync();
    Task BildirimCihaziKaldirAsync(int id);
}
