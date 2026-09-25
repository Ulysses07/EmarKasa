namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IAlisOdemeApi
{
    public Task<AlisDto> AlisOdemeDuzeltAsync(int alisId, int odemeId, AlisOdemeDuzeltYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Put, $"api/alis/{alisId}/odemeler/{odemeId}", g);
    public Task<AlisDto> AlisOdemeIptalAsync(int alisId, int odemeId, AlisOdemeIptalYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{alisId}/odemeler/{odemeId}/iptal", g);
}
