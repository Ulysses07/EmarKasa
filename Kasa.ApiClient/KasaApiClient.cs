using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient : IKasaApi, IOturumBildirimleri
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly SemaphoreSlim _oturumKilidi = new(1, 1);
    public event EventHandler? OturumSonlandi;

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
        await _oturumKilidi.WaitAsync();
        try { await _store.YazAsync(login.Token); }
        finally { _oturumKilidi.Release(); }
        return login;
    }

    public async Task<string?> BenKimAsync()
    {
        var el = await GetAsync<RolYanit>("api/auth/me");
        return el.Rol;
    }

    public async Task CikisAsync()
    {
        try
        {
            using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            using var _ = await GonderAsync(istek);
        }
        finally { await _store.TemizleAsync(); }
    }

    private record RolYanit(string Rol);

    // ---- okuma metotları ----

    public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => GetAsync<IReadOnlyList<HaftalikOzetDto>>("api/rapor/haftalik");
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) => GetAsync<AylikRaporDto>($"api/rapor/aylik?yil={yil}&ay={ay}");
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => GetAsync<IReadOnlyList<DonemDto>>("api/donemler");
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => GetAsync<IReadOnlyList<KanalDto>>("api/kanallar");
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => GetAsync<IReadOnlyList<KrediKartiDto>>("api/kredikartlari");
    public Task<IReadOnlyList<KrediDto>> KredilerAsync() => GetAsync<IReadOnlyList<KrediDto>>("api/krediler");
    public Task<AyarlarDto> AyarlarAsync() => GetAsync<AyarlarDto>("api/ayarlar");


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

    // ---- mutasyon metotları ----

    // Kanal
    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Post, "api/kanallar", g);
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Put, $"api/kanallar/{id}", g);
    public Task KanalSilAsync(int id) => SilAsync($"api/kanallar/{id}");

    // Cari

    // İşlem
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Post, "api/islemler", g);
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Put, $"api/islemler/{id}", g);
    public Task IslemSilAsync(int id) => SilAsync($"api/islemler/{id}");

    // Kredi kartı
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Post, "api/kredikartlari", g);
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Put, $"api/kredikartlari/{id}", g);
    public Task KrediKartiSilAsync(int id) => SilAsync($"api/kredikartlari/{id}");

    // Kredi
    public Task KrediEkleAsync(KrediDto kredi) => GonderJsonAsync(HttpMethod.Post, "api/krediler", kredi);
    public Task KrediGuncelleAsync(int id, KrediDto kredi) => GonderJsonAsync(HttpMethod.Put, $"api/krediler/{id}", kredi);
    public Task KrediSilAsync(int id) => SilAsync($"api/krediler/{id}");

    // Kart ödeme
    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId) => GetAsync<IReadOnlyList<KartOdemeDto>>($"api/kartodemeler?krediKartiId={krediKartiId}");
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) => GonderJsonAsync<KartOdemeDto>(HttpMethod.Post, "api/kartodemeler", g);
    public Task KartOdemeSilAsync(int id) => SilAsync($"api/kartodemeler/{id}");

    // Gelen upsert
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) => GonderJsonAsync<GelenDto>(HttpMethod.Put, "api/gelenler", g);

    // Ayarlar
    public Task AyarGuncelleAsync(AyarYaz g) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar", g);
    public Task IzleyiciSifreAsync(string yeniSifre) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar/izleyici-sifre", new { yeniSifre });

    // ---- altyapı ----

    private async Task<HttpResponseMessage> GonderAsync(HttpRequestMessage istek, bool tokenEkle = true, TimeSpan? zamanAsimi = null, CancellationToken cancellationToken = default)
    {
        string? token = null;
        if (tokenEkle)
        {
            token = await _store.OkuAsync();
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        using var sure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sure.CancelAfter(zamanAsimi ?? TimeSpan.FromSeconds(15));
        var yanit = await _http.SendAsync(istek, sure.Token);
        if (!yanit.IsSuccessStatusCode)
        {
            using (yanit)
            {
                if (yanit.StatusCode == HttpStatusCode.Unauthorized && tokenEkle)
                    await OturumuGecersizKilAsync(token);
                var mesaj = await HataMesajiAsync(yanit);
                throw new KasaApiException(yanit.StatusCode, mesaj);
            }
        }
        return yanit;
    }

    private async Task OturumuGecersizKilAsync(string? istekTokeni)
    {
        var temizlendi = false;
        await _oturumKilidi.WaitAsync();
        try
        {
            // Eski bir isteğin 401 yanıtı, bu sırada açılmış yeni oturumu kapatmasın.
            if (istekTokeni is not null && await _store.OkuAsync() == istekTokeni)
            {
                await _store.TemizleAsync();
                temizlendi = true;
            }
        }
        finally { _oturumKilidi.Release(); }
        if (temizlendi) OturumSonlandi?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<string?> HataMesajiAsync(HttpResponseMessage yanit)
    {
        if (yanit.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.ServiceUnavailable)) return null;
        try
        {
            using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());
            var kok = belge.RootElement;
            if (kok.ValueKind == JsonValueKind.String) return kok.GetString();
            if (kok.ValueKind != JsonValueKind.Object) return null;
            if (kok.TryGetProperty("errors", out var hatalar) && hatalar.ValueKind == JsonValueKind.Object)
            {
                var mesajlar = hatalar.EnumerateObject().SelectMany(h => h.Value.ValueKind == JsonValueKind.Array
                    ? h.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())
                    : Array.Empty<string?>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
                var mesaj = string.Join("\n", mesajlar);
                if (mesaj.Length > 0) return mesaj;
            }
            foreach (var alan in new[] { "detail", "hata", "message", "title" })
                if (kok.TryGetProperty(alan, out var deger) && deger.ValueKind == JsonValueKind.String)
                    return deger.GetString();
        }
        catch (JsonException) { /* JSON dışındaki hata gövdesini kullanıcıya taşıma. */ }
        return null;
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
