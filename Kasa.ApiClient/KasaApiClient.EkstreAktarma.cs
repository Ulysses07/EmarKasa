using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IEkstreAktarmaApi
{
    public Task<IReadOnlyList<EkstreBelgeOzetDto>> EkstreBelgelerAsync(int? beforeId = null) => GetAsync<IReadOnlyList<EkstreBelgeOzetDto>>("api/ekstre-aktar" + (beforeId is { } id ? $"?beforeId={id}" : ""));
    public Task<EkstreBelgeDto> EkstreBelgeAsync(int id) => GetAsync<EkstreBelgeDto>($"api/ekstre-aktar/{id}");
    public Task<EkstreBelgeDto> EkstreKaynakBelgeAsync(int kayitId) => GetAsync<EkstreBelgeDto>($"api/ekstre-aktar/kayitlar/{kayitId}");
    public Task<IndirilenDosya> EkstreDosyaAsync(int id) => DosyaIndirAsync(HttpMethod.Get, $"api/ekstre-aktar/{id}/dosya", "ekstre.pdf");
    public Task<EkstreOnizlemeDto> EkstreOnizlemeAsync(int id, EkstreKaydetYaz g) => GonderJsonAsync<EkstreOnizlemeDto>(HttpMethod.Post, $"api/ekstre-aktar/{id}/onizleme", g);
    public Task<EkstreBelgeDto> EkstreKaydetAsync(int id, EkstreKaydetYaz g) => GonderJsonAsync<EkstreBelgeDto>(HttpMethod.Post, $"api/ekstre-aktar/{id}/kaydet", g);
    public Task<EkstreBelgeDto> EkstreKayitIptalAsync(int id, int kayitId, EkstreIptalYaz g) => GonderJsonAsync<EkstreBelgeDto>(HttpMethod.Post, $"api/ekstre-aktar/{id}/kayitlar/{kayitId}/iptal", g);

    public async Task<EkstreBelgeDto> EkstreYukleAsync(byte[] icerik, string dosyaAdi, string kaynak, string banka, string hesapAdi, int? kartId, CancellationToken cancellationToken = default)
    {
        if (icerik.Length is 0 or > 10 * 1024 * 1024) throw new ArgumentException("PDF dosyası en fazla 10 MB olabilir.", nameof(icerik));
        using var form = new MultipartFormDataContent();
        var dosya = new ByteArrayContent(icerik);
        dosya.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(dosya, "dosya", GuvenliDosyaAdi(dosyaAdi, "ekstre.pdf"));
        form.Add(new StringContent(kaynak), "kaynak");
        form.Add(new StringContent(banka), "banka");
        form.Add(new StringContent(hesapAdi), "hesapAdi");
        if (kartId is { } id) form.Add(new StringContent(id.ToString(CultureInfo.InvariantCulture)), "kartId");
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/ekstre-aktar/yukle") { Content = form };
        using var yanit = await GonderAsync(istek, zamanAsimi: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken);
        return (await yanit.Content.ReadFromJsonAsync<EkstreBelgeDto>(Json, cancellationToken))!;
    }
}
