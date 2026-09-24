using System.Globalization;

namespace Kasa.ApiClient;

// Paket D uçları.
public sealed partial class KasaApiClient
{
    private static string Gun(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<CekDto> CekDurumAsync(int id, CekDurumYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Post, $"api/cekler/{id}/durum", g);
    public Task<CekRiskDto> CekRiskAsync(CekTuru? tur = null)
        => GetAsync<CekRiskDto>(tur is { } t ? $"api/cekler/risk?tur={t}" : "api/cekler/risk");

    public Task<IReadOnlyList<TekrarlayanAtlananDto>> AtlananGiderlerAsync()
        => GetAsync<IReadOnlyList<TekrarlayanAtlananDto>>("api/tekrarlayangiderler/atlananlar");
    public Task TekrarlayanAtlamayiGeriAlAsync(int id, DateOnly ay)
        => GonderJsonAsync(HttpMethod.Post, $"api/tekrarlayangiderler/{id}/atlamayi-geri-al", new { ay });
    public Task<IReadOnlyList<TekrarlayanHazirDto>> TekrarlayanHazirlarAsync()
        => GetAsync<IReadOnlyList<TekrarlayanHazirDto>>("api/tekrarlayangiderler/hazir");
    public Task<IReadOnlyList<TekrarlayanGiderDto>> TekrarlayanHazirEkleAsync(string kod)
        => GonderJsonAsync<IReadOnlyList<TekrarlayanGiderDto>>(HttpMethod.Post, "api/tekrarlayangiderler/hazir", new { kod });

    public Task<IReadOnlyList<KartDonemDto>> KartDonemleriAsync(int krediKartiId, int? adet = null)
        => GetAsync<IReadOnlyList<KartDonemDto>>(adet is { } a
            ? FormattableString.Invariant($"api/kartmutabakat/donemler?krediKartiId={krediKartiId}&adet={a}")
            : FormattableString.Invariant($"api/kartmutabakat/donemler?krediKartiId={krediKartiId}"));
    public Task<KartMutabakatDetayDto> KartMutabakatAsync(int krediKartiId, DateOnly kesim)
        => GetAsync<KartMutabakatDetayDto>(FormattableString.Invariant($"api/kartmutabakat?krediKartiId={krediKartiId}&kesim=") + Gun(kesim));
    public Task<KartMutabakatDetayDto> KartMutabakatKaydetAsync(KartMutabakatYaz g)
        => GonderJsonAsync<KartMutabakatDetayDto>(HttpMethod.Put, "api/kartmutabakat", g);
    public Task KartMutabakatSilAsync(int id) => SilAsync($"api/kartmutabakat/{id}");

    public Task<KasaSayimDto> SayimFarkiAsync(int id, SayimFarkYaz g)
        => GonderJsonAsync<KasaSayimDto>(HttpMethod.Put, $"api/kasasayimlari/{id}/fark", g);
    public Task<NedenDegistiDto> SayimNedenDegistiAsync(int id) => GetAsync<NedenDegistiDto>($"api/kasasayimlari/{id}/nedendegisti");
    public Task<SonSayimDto> SonSayimAsync() => GetAsync<SonSayimDto>("api/kasasayimlari/son");
}
