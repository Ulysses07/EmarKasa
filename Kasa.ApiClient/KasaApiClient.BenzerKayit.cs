namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IBenzerKayitApi
{
    public Task<IReadOnlyList<BenzerKayitDto>> BenzerKayitlarAsync(BenzerlikYaz g)
        => GonderJsonAsync<IReadOnlyList<BenzerKayitDto>>(HttpMethod.Post, "api/islemler/benzerlik", g);
}
