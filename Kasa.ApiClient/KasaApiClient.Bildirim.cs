namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IBildirimApi
{
    public Task<IReadOnlyList<BildirimDto>> BildirimlerAsync() => GetAsync<IReadOnlyList<BildirimDto>>("api/bildirimler");
    public Task<BildirimAyarDto> BildirimAyarlariAsync() => GetAsync<BildirimAyarDto>("api/bildirimler/ayarlar");
    public Task<BildirimAyarDto> BildirimAyarKaydetAsync(BildirimAyarYaz g) => GonderJsonAsync<BildirimAyarDto>(HttpMethod.Put, "api/bildirimler/ayarlar", g);
    public async Task BildirimOkunduAsync(int id)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, $"api/bildirimler/{id}/okundu");
        using var yanit = await GonderAsync(istek);
    }
    public Task<PushAnahtarDto> BildirimAnahtariAsync() => GetAsync<PushAnahtarDto>("api/bildirimler/push/anahtar");
    public Task<IReadOnlyList<BildirimCihaziDto>> BildirimCihazlariAsync() => GetAsync<IReadOnlyList<BildirimCihaziDto>>("api/bildirimler/push/abonelikler");
    public Task BildirimCihaziKaldirAsync(int id) => SilAsync($"api/bildirimler/push/abonelikler/{id}");
}
