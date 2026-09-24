using System.Globalization;

namespace Kasa.ApiClient;

// Paket A — panel, nakit tahmini ve bildirim uçları.
public sealed partial class KasaApiClient
{
    public Task<NakitTahminDto> NakitTahminAsync(int gun, IReadOnlyCollection<int>? haricCekler = null)
    {
        var yol = FormattableString.Invariant($"api/rapor/tahmin?gun={gun}");
        if (haricCekler is { Count: > 0 })
            yol += "&haric=" + string.Join(",", haricCekler.Distinct().Order().Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return GetAsync<NakitTahminDto>(yol);
    }

    public Task<IReadOnlyList<EksikGelenDto>> EksikGelenlerAsync() => GetAsync<IReadOnlyList<EksikGelenDto>>("api/gelenler/eksik");

    public Task<GecmisOzetDto> GecmisOzetAsync(int? sonId = null)
        => GetAsync<GecmisOzetDto>(sonId is { } s ? FormattableString.Invariant($"api/gecmis/ozet?sonId={s}") : "api/gecmis/ozet");
}
