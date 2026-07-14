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

    private record RolYanit(string Rol);

    // ---- okuma metotları ----

    public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => GetAsync<IReadOnlyList<HaftalikOzetDto>>("api/rapor/haftalik");
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) => GetAsync<AylikRaporDto>($"api/rapor/aylik?yil={yil}&ay={ay}");
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => GetAsync<IReadOnlyList<DonemDto>>("api/donemler");
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => GetAsync<IReadOnlyList<KanalDto>>("api/kanallar");
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => GetAsync<IReadOnlyList<KrediKartiDto>>("api/kredikartlari");
    public Task<AyarlarDto> AyarlarAsync() => GetAsync<AyarlarDto>("api/ayarlar");

    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null)
        => GetAsync<IReadOnlyList<CariDto>>(ara is null ? "api/cariler" : $"api/cariler?ara={Uri.EscapeDataString(ara)}");

    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null)
        => GetAsync<IReadOnlyList<GelenDto>>(donemStart is { } d ? $"api/gelenler?donemStart={d:yyyy-MM-dd}" : "api/gelenler");

    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(
        DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        var q = new List<string>();
        if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(kanal)) q.Add($"kanal={Uri.EscapeDataString(kanal)}");
        if (!string.IsNullOrWhiteSpace(cari)) q.Add($"cari={Uri.EscapeDataString(cari)}");
        var yol = q.Count > 0 ? $"api/islemler?{string.Join("&", q)}" : "api/islemler";
        return GetAsync<IReadOnlyList<IslemDto>>(yol);
    }

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
