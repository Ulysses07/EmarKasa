using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kasa.ApiClient;

// Paket F: belge, işlem ekleri, fatura takibi, muhasebeci listesi ve POS.
public sealed partial class KasaApiClient
{
    public Task<IReadOnlyList<EkDto>> EklerAsync(int islemId) => GetAsync<IReadOnlyList<EkDto>>($"api/islemler/{islemId}/ekler");

    public async Task<EkDto> EkYukleAsync(int islemId, string dosyaAdi, byte[] icerik)
    {
        var dosya = new ByteArrayContent(icerik);
        dosya.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { dosya, "dosya", dosyaAdi } };
        using var istek = new HttpRequestMessage(HttpMethod.Post, $"api/islemler/{islemId}/ekler") { Content = form };
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<EkDto>(Json))!;
    }

    public Task<IndirilenDosya> EkIndirAsync(int ekId) => IndirAsync($"api/ekler/{ekId}", $"ek-{ekId}");
    public Task EkSilAsync(int ekId) => SilAsync($"api/ekler/{ekId}");

    public Task<IslemDto> BelgeGuncelleAsync(int islemId, BelgeBilgisi belge)
        => GonderJsonAsync<IslemDto>(HttpMethod.Put, $"api/islemler/{islemId}/belge",
            new { belgeTuru = belge.Tur, belgeNo = belge.No, faturaBekleniyor = belge.FaturaBekleniyor });

    public Task<FaturaTakibiDto> FaturaTakibiAsync(int yil, int ay)
        => GetAsync<FaturaTakibiDto>(FormattableString.Invariant($"api/faturatakibi?yil={yil}&ay={ay}"));

    public Task<IndirilenDosya> MuhasebeciCsvAsync(int yil, int ay)
        => IndirAsync(FormattableString.Invariant($"api/disaaktar/muhasebeci.csv?yil={yil}&ay={ay}"),
            FormattableString.Invariant($"kasa-muhasebeci-{yil:D4}-{ay:D2}.csv"));

    public Task<IReadOnlyList<PosTanimDto>> PosTanimlariAsync() => GetAsync<IReadOnlyList<PosTanimDto>>("api/pos/tanimlar");
    public Task<PosTanimDto> PosTanimOlusturAsync(PosTanimYaz g) => GonderJsonAsync<PosTanimDto>(HttpMethod.Post, "api/pos/tanimlar", g);
    public Task<PosTanimDto> PosTanimGuncelleAsync(int id, PosTanimYaz g) => GonderJsonAsync<PosTanimDto>(HttpMethod.Put, $"api/pos/tanimlar/{id}", g);
    public Task PosTanimSilAsync(int id) => SilAsync($"api/pos/tanimlar/{id}");

    public Task<IReadOnlyList<PosSatisDto>> PosSatislariAsync(DateOnly? baslangic = null, DateOnly? bitis = null, int? posId = null)
    {
        var q = new List<string>();
        if (baslangic is { } b) q.Add(FormattableString.Invariant($"baslangic={b:yyyy-MM-dd}"));
        if (bitis is { } s) q.Add(FormattableString.Invariant($"bitis={s:yyyy-MM-dd}"));
        if (posId is { } p) q.Add(FormattableString.Invariant($"posId={p}"));
        return GetAsync<IReadOnlyList<PosSatisDto>>(q.Count > 0 ? "api/pos/satislar?" + string.Join("&", q) : "api/pos/satislar");
    }
    public Task<PosSatisDto> PosSatisOlusturAsync(PosSatisYaz g) => GonderJsonAsync<PosSatisDto>(HttpMethod.Post, "api/pos/satislar", g);
    public Task<PosSatisDto> PosSatisGuncelleAsync(int id, PosSatisYaz g) => GonderJsonAsync<PosSatisDto>(HttpMethod.Put, $"api/pos/satislar/{id}", g);
    public Task PosSatisSilAsync(int id) => SilAsync($"api/pos/satislar/{id}");
    public Task<PosOzetDto> PosOzetAsync(int yil, int ay)
        => GetAsync<PosOzetDto>(FormattableString.Invariant($"api/pos/ozet?yil={yil}&ay={ay}"));
}
