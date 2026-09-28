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
    public Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(arama)) q.Add("arama=" + Uri.EscapeDataString(arama.Trim()));
        if (tutar is { } t) q.Add("tutar=" + t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(imlec)) q.Add("imlec=" + Uri.EscapeDataString(imlec));
        if (limit is { } l) q.Add("limit=" + l.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<BaglanabilirGiderSayfasi>("api/alis/baglanabilir-giderler" + (q.Count > 0 ? "?" + string.Join("&", q) : ""));
    }
    public Task<IReadOnlyList<AliciDto>> AlicilarAsync() => GetAsync<IReadOnlyList<AliciDto>>("api/alicilar");
    public Task<AliciDto> AliciOlusturAsync(AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Post, "api/alicilar", g);
    public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) => GonderJsonAsync<AliciDto>(HttpMethod.Put, $"api/alicilar/{id}", g);
}
