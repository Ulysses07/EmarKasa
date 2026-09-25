namespace Kasa.ApiClient;

public record AlisOdemeDuzeltYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, int? HesapId, string Aciklama, int? HedefAlisId = null, int? HedefSurum = null);
public record AlisOdemeIptalYaz(int Surum, Guid IstekId, string Aciklama);

public interface IAlisOdemeApi
{
    Task<AlisDto> AlisOdemeDuzeltAsync(int alisId, int odemeId, AlisOdemeDuzeltYaz g);
    Task<AlisDto> AlisOdemeIptalAsync(int alisId, int odemeId, AlisOdemeIptalYaz g);
}
