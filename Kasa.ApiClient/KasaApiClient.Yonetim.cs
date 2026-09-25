using System.Net.Http.Headers;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IYonetimApi
{
    public async Task SifreDegistirAsync(SifreDegistirYaz g)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/sifre") { Content = System.Net.Http.Json.JsonContent.Create(g, options: Json) };
        using var _ = await GonderAsync(istek);
        await OturumuGecersizKilAsync(istek.Headers.Authorization?.Parameter);
    }
    public Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string mevcutSifre)
        => GonderJsonAsync<KurtarmaKoduDto>(HttpMethod.Post, "api/auth/kurtarma-kodu", new { mevcutSifre });
    public async Task SifreKurtarAsync(SifreKurtarYaz g)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/kurtar") { Content = System.Net.Http.Json.JsonContent.Create(g, options: Json) };
        using var _ = await GonderAsync(istek, tokenEkle: false);
    }
    public Task<SurumDto> SurumAsync() => GetAsync<SurumDto>("api/surum");
    public Task<YedekDurumuDto> YedekDurumuAsync() => GetAsync<YedekDurumuDto>("api/yedek/durum");
    public Task<IndirilenDosya> YedekIndirAsync() => DosyaIndirAsync(HttpMethod.Post, "api/yedek", "kasa-yedek.zip");
    public Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int alisId) => GetAsync<IReadOnlyList<BelgeDto>>($"api/alis/{alisId}/belgeler");
    public async Task<BelgeDto> BelgeYukleAsync(int alisId, string dosyaAdi, string icerikTuru, byte[] icerik, int? odemeId = null)
    {
        using var govde = new MultipartFormDataContent();
        var dosya = new ByteArrayContent(icerik);
        dosya.Headers.ContentType = new MediaTypeHeaderValue(icerikTuru);
        govde.Add(dosya, "dosya", GuvenliDosyaAdi(dosyaAdi, "belge"));
        if (odemeId is { } id) govde.Add(new StringContent(id.ToString(System.Globalization.CultureInfo.InvariantCulture)), "odemeId");
        using var istek = new HttpRequestMessage(HttpMethod.Post, $"api/alis/{alisId}/belgeler") { Content = govde };
        using var yanit = await GonderAsync(istek);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<BelgeDto>(yanit.Content, Json))!;
    }
    public Task<IndirilenDosya> BelgeIndirAsync(int belgeId) => DosyaIndirAsync(HttpMethod.Get, $"api/belgeler/{belgeId}", $"belge-{belgeId}");
    public Task BelgeSilAsync(int belgeId) => SilAsync($"api/belgeler/{belgeId}");
    public Task<IndirilenDosya> DisariAktarAsync(DateOnly baslangic, DateOnly bitis, string? kanal, string bicim)
        => DosyaIndirAsync(HttpMethod.Get, $"api/disari-aktar?baslangic={baslangic:yyyy-MM-dd}&bitis={bitis:yyyy-MM-dd}&bicim={Uri.EscapeDataString(bicim)}"
            + (string.IsNullOrWhiteSpace(kanal) ? "" : "&kanal=" + Uri.EscapeDataString(kanal)), $"kasa-rapor.{bicim}");

    private async Task<IndirilenDosya> DosyaIndirAsync(HttpMethod metot, string yol, string varsayilan)
    {
        using var istek = new HttpRequestMessage(metot, yol);
        using var yanit = await GonderAsync(istek);
        var ad = yanit.Content.Headers.ContentDisposition?.FileNameStar ?? yanit.Content.Headers.ContentDisposition?.FileName;
        return new(await yanit.Content.ReadAsByteArrayAsync(), GuvenliDosyaAdi(ad, varsayilan), yanit.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }
    private static string GuvenliDosyaAdi(string? ad, string varsayilan)
    {
        var temiz = (ad ?? "").Trim('"').Replace('\\', '/').Split('/').Last();
        temiz = string.Concat(temiz.Where(c => !char.IsControl(c) && !"<>:\"/\\|?*".Contains(c))).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(temiz) ? varsayilan : temiz;
    }
}
