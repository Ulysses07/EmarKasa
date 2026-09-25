namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IKasaKontrolApi, IAylikGiderApi
{
    public Task<KartMasrafOnizlemeDto> KartMasrafOnizleAsync(int kartId, KartMasrafYaz girdi) => GonderJsonAsync<KartMasrafOnizlemeDto>(HttpMethod.Post, $"api/takip/kartlar/{kartId}/masraf-onizleme", girdi);
    public Task<KartTakipDto> KartMasrafKaydetAsync(int kartId, KartMasrafYaz girdi) => GonderJsonAsync<KartTakipDto>(HttpMethod.Post, $"api/takip/kartlar/{kartId}/masraflar", girdi);
    public Task<IReadOnlyList<KasaEsikDto>> KasaEsikleriAsync() => GetAsync<IReadOnlyList<KasaEsikDto>>("api/kasa-esikleri");
    public Task<KasaEsikDto> KasaEsigiKaydetAsync(int kanalId, KasaEsikYaz girdi) => GonderJsonAsync<KasaEsikDto>(HttpMethod.Put, $"api/kasa-esikleri/{kanalId}", girdi);
    public Task<IReadOnlyList<KasaKontrolDto>> KasaKontrolleriAsync() => GetAsync<IReadOnlyList<KasaKontrolDto>>("api/kasa-kontrol");
    public Task<KasaKontrolOnizlemeDto> KasaKontrolOnizleAsync(KasaKontrolOnizle girdi) => GonderJsonAsync<KasaKontrolOnizlemeDto>(HttpMethod.Post, "api/kasa-kontrol/onizleme", girdi);
    public Task<KasaKontrolDto> KasaKontrolKaydetAsync(KasaKontrolYaz girdi) => GonderJsonAsync<KasaKontrolDto>(HttpMethod.Post, "api/kasa-kontrol", girdi);
    public Task<IReadOnlyList<AylikGiderSablonDto>> AylikGiderSablonlariAsync() => GetAsync<IReadOnlyList<AylikGiderSablonDto>>("api/aylik-giderler/sablonlar");
    public Task<AylikGiderSablonDto> AylikGiderSablonKaydetAsync(int? id, AylikGiderSablonYaz girdi) => GonderJsonAsync<AylikGiderSablonDto>(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/aylik-giderler/sablonlar" : $"api/aylik-giderler/sablonlar/{id}", girdi);
    public Task<AylikGiderAyDto> AylikGiderlerAsync(int yil, int ay) => GetAsync<AylikGiderAyDto>($"api/aylik-giderler?yil={yil}&ay={ay}");
    public Task<AylikGiderSatirDto> AylikGiderOdeAsync(int sablonId, AylikGiderOdemeYaz girdi) => GonderJsonAsync<AylikGiderSatirDto>(HttpMethod.Post, $"api/aylik-giderler/{sablonId}/ode", girdi);
    public Task<AylikGiderSatirDto> AylikGiderIptalAsync(int odemeId, AylikGiderIptalYaz girdi) => GonderJsonAsync<AylikGiderSatirDto>(HttpMethod.Post, $"api/aylik-giderler/odemeler/{odemeId}/iptal", girdi);
    public Task<AyKilidiDto> AyKilidiAsync() => GetAsync<AyKilidiDto>("api/ay-kilidi");
    public Task<AyKilidiDto> AyKilidiDegistirAsync(bool kapat, AyKilidiYaz girdi) => GonderJsonAsync<AyKilidiDto>(HttpMethod.Post, kapat ? "api/ay-kilidi/kapat" : "api/ay-kilidi/ac", girdi);
}
