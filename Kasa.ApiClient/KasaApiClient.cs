using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public KasaApiClient(HttpClient http, ITokenStore store)
    {
        _http = http;
        _store = store;
    }

    public async Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre }, options: Json),
        };
        using var yanit = await GonderAsync(istek, tokenEkle: false);
        var login = (await yanit.Content.ReadFromJsonAsync<LoginYanit>(Json))!;
        await _store.YazAsync(login.Token);
        return login;
    }

    public async Task<string?> BenKimAsync()
    {
        var el = await GetAsync<RolYanit>("api/auth/me");
        return el.Rol;
    }

    public async Task CikisAsync()
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
        using var _ = await GonderAsync(istek);
        await _store.TemizleAsync();
    }

    // GEÇİCİ: Task 4'te reads bloğuna taşınacak. Task 3 testlerinin yeşil olması için burada.
    public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");

    private record RolYanit(string Rol);

    // ---- altyapı ----

    private async Task<HttpResponseMessage> GonderAsync(HttpRequestMessage istek, bool tokenEkle = true)
    {
        if (tokenEkle)
        {
            var token = await _store.OkuAsync();
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        var yanit = await _http.SendAsync(istek);
        if (!yanit.IsSuccessStatusCode)
        {
            var kod = yanit.StatusCode;
            yanit.Dispose();
            throw new KasaApiException(kod);
        }
        return yanit;
    }

    private async Task<T> GetAsync<T>(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> GonderJsonAsync<T>(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task GonderJsonAsync(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var _ = await GonderAsync(istek);
    }

    private async Task SilAsync(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Delete, yol);
        using var _ = await GonderAsync(istek);
    }
}
