using System.Globalization;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : ICekApi
{
    public Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null)
    {
        var sorgu = new List<string>();
        if (yon is not null)
            sorgu.Add("yon=" + Uri.EscapeDataString(yon));
        if (durum is not null)
            sorgu.Add("durum=" + Uri.EscapeDataString(durum));
        if (!string.IsNullOrWhiteSpace(ara))
            sorgu.Add("ara=" + Uri.EscapeDataString(ara.Trim()));
        if (vadeBas is { } bas)
            sorgu.Add("vadeBas=" + bas.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (vadeSon is { } son)
            sorgu.Add("vadeSon=" + son.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return GetAsync<IReadOnlyList<CekDto>>("api/takip/cekler" + (sorgu.Count == 0 ? "" : "?" + string.Join("&", sorgu)));
    }

    public Task<CekDto> CekAsync(int id) => GetAsync<CekDto>($"api/takip/cekler/{id}");
    public Task<CekOzetDto> CekOzetAsync() => GetAsync<CekOzetDto>("api/takip/cekler/ozet");
    public Task<CekDto> CekKaydetAsync(int? id, CekYaz g) =>
        GonderJsonAsync<CekDto>(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/takip/cekler" : $"api/takip/cekler/{id}", g);
    public Task CekSilAsync(int id, CekSilYaz g) => GonderJsonAsync(HttpMethod.Delete, $"api/takip/cekler/{id}", g);
    public Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Post, $"api/takip/cekler/{id}/hareketler", g);
    public Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Delete, $"api/takip/cekler/{id}/hareketler/son", g);
}
