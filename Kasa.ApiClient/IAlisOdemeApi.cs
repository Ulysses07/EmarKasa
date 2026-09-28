namespace Kasa.ApiClient;

public record AlisOdemeDuzeltYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, int? HesapId, string Aciklama, int? HedefAlisId = null, int? HedefSurum = null);
/// <param name="KanalDagilimlari">Yalnız kart takibindeki ödemede: verilirse ödeme alıştan ayrılır, kart harcaması ve gideri bu gerçek kanal
/// paylarıyla (toplamı ödeme tutarı) alıştan bağımsız kalır. Verilmezse ödenmemiş kart harcaması gideriyle birlikte kalkar; ödenmişse 409.</param>
public record AlisOdemeIptalYaz(int Surum, Guid IstekId, string Aciklama, IReadOnlyList<AlisDagilimYaz>? KanalDagilimlari = null);

public interface IAlisOdemeApi
{
    Task<AlisDto> AlisOdemeDuzeltAsync(int alisId, int odemeId, AlisOdemeDuzeltYaz g);
    Task<AlisDto> AlisOdemeIptalAsync(int alisId, int odemeId, AlisOdemeIptalYaz g);
}
