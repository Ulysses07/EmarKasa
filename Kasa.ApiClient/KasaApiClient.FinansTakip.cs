namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IFinansTakipApi
{
    public Task<IReadOnlyList<KartTakipDto>> TakipKartlarAsync() => GetAsync<IReadOnlyList<KartTakipDto>>("api/takip/kartlar");
    public Task<KartTakipDto> TakipKartAsync(int id) => GetAsync<KartTakipDto>($"api/takip/kartlar/{id}");
    public Task<KartTakipDto> TakipKartKaydetAsync(int? id, KartTakipYaz g) => GonderJsonAsync<KartTakipDto>(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/takip/kartlar" : $"api/takip/kartlar/{id}", g);
    public Task<KartTakipDto> TakipKartDurumAsync(int id, TakipDurumYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/durum", g);
    public Task<KartTakipDto> TakipHarcamaKaydetAsync(int id, KartHarcamaYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/harcamalar", g);
    public Task<KartTakipDto> TakipHarcamaIptalAsync(int id, int harcamaId, TakipIptalYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/harcamalar/{harcamaId}/iptal", g);
    public Task<KartTakipDto> TakipEkstreKaydetAsync(int id, int ekstreId, KartEkstreYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Put, $"api/takip/kartlar/{id}/ekstreler/{ekstreId}", g);
    public Task<KartOdemeOnizlemeDto> TakipOdemeOnizlemeAsync(int id, KartTakipOdemeYaz g) => GonderJsonAsync<KartOdemeOnizlemeDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/odeme-onizleme", g);
    public Task<KartTakipDto> TakipOdemeKaydetAsync(int id, KartTakipOdemeYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/odemeler", g);
    public Task<KartTakipDto> TakipOdemeIptalAsync(int id, int odemeId, TakipIptalYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/odemeler/{odemeId}/iptal", g);
    public Task<TakipGecisDto> TakipKartGecisOnizlemeAsync(int id, KartGecisYaz g) => GonderJsonAsync<TakipGecisDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/gecis-onizleme", g);
    public Task<KartTakipDto> TakipKartGecisAsync(int id, KartGecisYaz g) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{id}/gecis", g);
    public Task<IReadOnlyList<KrediTakipDto>> TakipKredilerAsync() => GetAsync<IReadOnlyList<KrediTakipDto>>("api/takip/krediler");
    public Task<KrediTakipDto> TakipKrediAsync(int id) => GetAsync<KrediTakipDto>($"api/takip/krediler/{id}");
    public Task<KrediTakipDto> TakipKrediKaydetAsync(KrediTakipYaz g) => GonderJsonAsync<KrediTakipDto>(HttpMethod.Post, "api/takip/krediler", g);
    public Task<KrediTakipDto> TakipKrediDurumAsync(int id, TakipDurumYaz g) => GonderJsonAsync<KrediTakipDto>(HttpMethod.Post, $"api/takip/krediler/{id}/durum", g);
    public Task<KrediTakipDto> TakipTaksitKaydetAsync(int id, int taksitId, KrediTaksitYaz g) => GonderJsonAsync<KrediTakipDto>(HttpMethod.Put, $"api/takip/krediler/{id}/taksitler/{taksitId}", g);
    public Task<KrediTakipDto> TakipKrediKapatAsync(int id, KrediKapatYaz g) => GonderJsonAsync<KrediTakipDto>(HttpMethod.Post, $"api/takip/krediler/{id}/erken-kapat", g);
    public Task<TakipGecisDto> TakipKrediGecisOnizlemeAsync(int id, KrediGecisYaz g) => GonderJsonAsync<TakipGecisDto>(HttpMethod.Post, $"api/takip/krediler/{id}/gecis-onizleme", g);
    public Task<KrediTakipDto> TakipKrediGecisAsync(int id, KrediGecisYaz g) => GonderJsonAsync<KrediTakipDto>(HttpMethod.Post, $"api/takip/krediler/{id}/gecis", g);
    public Task<TakipOzetDto> TakipOzetAsync(int gun = 30) => GetAsync<TakipOzetDto>($"api/takip/ozet?gun={gun}");
}
