using System.Globalization;

namespace Kasa.ApiClient;

/// <summary>Merkezi değişiklik geçmişi (denetim izi) okuması; yalnız editör.</summary>
public interface IDenetimApi
{
    Task<IReadOnlyList<DenetimOlayDto>> DenetimOlaylariAsync(DenetimSorgusu? sorgu = null, CancellationToken ct = default);
}

public sealed partial class KasaApiClient : IDenetimApi
{
    public Task<IReadOnlyList<DenetimOlayDto>> DenetimOlaylariAsync(DenetimSorgusu? sorgu = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<DenetimOlayDto>>(DenetimYolu(sorgu ?? new DenetimSorgusu()), ct);

    /// <summary>Sorgu dizesi: yalnız dolu süzgeçler, metinler kaçışlı.</summary>
    internal static string DenetimYolu(DenetimSorgusu s)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Varlik)) q.Add("varlik=" + Uri.EscapeDataString(s.Varlik));
        if (!string.IsNullOrWhiteSpace(s.VarlikId)) q.Add("varlikId=" + Uri.EscapeDataString(s.VarlikId));
        if (!string.IsNullOrWhiteSpace(s.Tur)) q.Add("tur=" + Uri.EscapeDataString(s.Tur));
        if (s.KilitAcmaOlayiId is { } kilit) q.Add("kilitAcmaOlayiId=" + kilit.ToString(CultureInfo.InvariantCulture));
        if (s.OncekiId is { } once) q.Add("oncekiId=" + once.ToString(CultureInfo.InvariantCulture));
        if (s.Adet is { } adet) q.Add("adet=" + adet.ToString(CultureInfo.InvariantCulture));
        return q.Count == 0 ? "api/denetim" : "api/denetim?" + string.Join("&", q);
    }
}
