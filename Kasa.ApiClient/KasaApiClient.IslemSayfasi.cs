using System.Net;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient
{
    public async Task<IslemSayfasiDto> IslemlerSayfasiAsync(DateOnly? baslangic = null, DateOnly? bitis = null,
        string? kanal = null, string? cari = null, string? imlec = null, int limit = 100,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var q = new List<string> { $"limit={limit}" };
        if (baslangic is { } bas)
            q.Add($"baslangic={bas:yyyy-MM-dd}");
        if (bitis is { } son)
            q.Add($"bitis={son:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(kanal))
            q.Add("kanal=" + Uri.EscapeDataString(kanal));
        if (!string.IsNullOrWhiteSpace(cari))
            q.Add("cari=" + Uri.EscapeDataString(cari));
        if (!string.IsNullOrEmpty(imlec))
            q.Add("imlec=" + Uri.EscapeDataString(imlec));

        try
        {
            return await GetAsync<IslemSayfasiDto>("api/islemler/sayfa?" + string.Join("&", q), ct);
        }
        catch (KasaApiException e) when (imlec is null && e.DurumKodu is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            // 2.4.1 sunucusunda bu yol yoktur. Her ilk okumada yeniden denemek, sunucu daha
            // sonra yükseltildiğinde istemciyi yeniden başlatmadan sayfalı yola geçirir.
            return new(await IslemlerAsync(baslangic, bitis, kanal, cari), null, false);
        }
    }
}
