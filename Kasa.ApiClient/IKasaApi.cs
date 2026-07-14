namespace Kasa.ApiClient;

/// <summary>KasaApiClient'in test edilebilir yüzeyi (VM'ler buna bağlanır).</summary>
public interface IKasaApi
{
    Task<LoginYanit> LoginAsync(string? kullanici, string sifre);
    Task<string?> BenKimAsync();
    Task CikisAsync();

    Task<PanelDto> PanelAsync();
    Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync();
    Task<AylikRaporDto> AylikAsync(int yil, int ay);
    Task<IReadOnlyList<DonemDto>> DonemlerAsync();
    Task<IReadOnlyList<KanalDto>> KanallarAsync();
    Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null);
    Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null);
    Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync();
    Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null);
    Task<AyarlarDto> AyarlarAsync();
}
