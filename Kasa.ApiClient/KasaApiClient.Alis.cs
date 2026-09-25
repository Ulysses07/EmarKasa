namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IAlisApi
{
    public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => GetAsync<IReadOnlyList<AlisKanalDto>>("api/alis/kanallar");
    public Task<IReadOnlyList<AlisDto>> AlislarAsync() => GetAsync<IReadOnlyList<AlisDto>>("api/alis");
    public Task<AlisDto> AlisOlusturAsync(AlisYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, "api/alis", g);
    public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Put, $"api/alis/{id}", g);
    public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/gonder", g);
    public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/onayla", g);
    public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/iade", g);
    public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g) => GonderJsonAsync<AlisDto>(HttpMethod.Post, $"api/alis/{id}/odemeler", g);
    public Task<IReadOnlyList<AliciDto>> AlicilarAsync() => GetAsync<IReadOnlyList<AliciDto>>("api/alicilar");
    public Task<AliciDto> AliciOlusturAsync(AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Post, "api/alicilar", g);
    public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Put, $"api/alicilar/{id}", g);
}
