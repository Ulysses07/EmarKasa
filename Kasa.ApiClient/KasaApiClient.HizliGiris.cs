using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.ApiClient;

// "Hızlı ve hatasız giriş" (paket C) uçları.
public sealed partial class KasaApiClient
{
    public async Task<IslemSayfasi> IslemAraAsync(IslemAramasi arama, int limit, int offset)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, AramaYolu(arama, limit, offset, "api/islemler"));
        using var yanit = await GonderAsync(istek);
        var kayitlar = (await yanit.Content.ReadFromJsonAsync<IReadOnlyList<IslemDto>>(Json))!;
        return new IslemSayfasi(kayitlar, ToplamKayit(yanit, offset + kayitlar.Count));
    }

    public Task<IndirilenDosya> IslemAramaCsvAsync(IslemAramasi arama)
        => IndirAsync(AramaYolu(arama, null, null, "api/disaaktar/islemler.csv"), "kasa-islemler.csv");

    /// <summary>Eski parametreler <c>IslemYolu</c> ile aynı; gelişmiş süzgeçler sonuna eklenir (tutarlar invariant).</summary>
    private static string AramaYolu(IslemAramasi a, int? limit, int? offset, string taban)
    {
        var yol = IslemYolu(a.Baslangic, a.Bitis, a.Kanal, a.Cari, limit, offset, taban);
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.NotAra)) q.Add($"notAra={Uri.EscapeDataString(a.NotAra.Trim())}");
        if (a.Tip is { } t) q.Add($"tip={t}");
        if (a.KartId is { } k) q.Add(FormattableString.Invariant($"kartId={k}"));
        if (a.MinTutar is { } en) q.Add($"minTutar={en.ToString(CultureInfo.InvariantCulture)}");
        if (a.MaxTutar is { } ec) q.Add($"maxTutar={ec.ToString(CultureInfo.InvariantCulture)}");
        if (q.Count == 0) return yol;
        return yol + (yol.Contains('?') ? "&" : "?") + string.Join("&", q);
    }

    public async Task<IReadOnlyList<IslemUyariDto>> IslemUyarilariAsync(IslemYaz g, int? haricId = null)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/islemler/uyarilar")
        {
            Content = JsonContent.Create(new
            {
                g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.KrediKartiId, HaricId = haricId,
            }, options: Json),
        };
        using var yanit = await HamGonderAsync(istek);
        if (yanit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return []; // eski sunucu
        await BasarisizsaFirlatAsync(yanit);
        return (await yanit.Content.ReadFromJsonAsync<IReadOnlyList<IslemUyariDto>>(Json)) ?? [];
    }

    public async Task<TopluIslemSonucu> TopluIslemKaydetAsync(IReadOnlyList<IslemYaz> satirlar, bool yeniCarileriEkle)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/islemler/toplu")
        {
            Content = JsonContent.Create(new { satirlar, yeniCarileriEkle }, options: Json),
        };
        using var yanit = await HamGonderAsync(istek);
        if (yanit.StatusCode == HttpStatusCode.BadRequest)
        {
            var govde = await GovdeOkuAsync(yanit);
            if (govde is { } el && el.TryGetProperty("satirlar", out var s) && s.ValueKind == JsonValueKind.Array)
            {
                var hatalar = s.Deserialize<IReadOnlyList<TopluSatirHatasi>>(Json) ?? [];
                var mesaj = el.TryGetProperty("hata", out var h) ? h.GetString() : null;
                return new TopluIslemSonucu(false, 0, 0m, [], [], mesaj, hatalar);
            }
            var genel = govde is { } g2 && g2.TryGetProperty("hata", out var h2) ? h2.GetString() : null;
            throw new KasaApiException(HttpStatusCode.BadRequest, genel);
        }
        await BasarisizsaFirlatAsync(yanit);
        var ok = (await yanit.Content.ReadFromJsonAsync<TopluYanit>(Json))!;
        return new TopluIslemSonucu(true, ok.Eklenen, ok.Toplam, ok.YeniCariler ?? [], ok.Islemler ?? [], null, []);
    }

    private sealed record TopluYanit(int Eklenen, decimal Toplam, IReadOnlyList<string>? YeniCariler, IReadOnlyList<IslemDto>? Islemler);

    public async Task<IslemOneriDto?> IslemOnerisiAsync(string cari)
    {
        if (string.IsNullOrWhiteSpace(cari)) return null;
        using var istek = new HttpRequestMessage(HttpMethod.Get, $"api/islemler/son?cari={Uri.EscapeDataString(cari.Trim())}");
        using var yanit = await HamGonderAsync(istek);
        if (yanit.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return null;
        await BasarisizsaFirlatAsync(yanit);
        return await yanit.Content.ReadFromJsonAsync<IslemOneriDto>(Json);
    }

    public async Task<GelenKayitSonucu> GelenKorumaliKaydetAsync(GelenYaz g, decimal beklenenTutar)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Put, "api/gelenler")
        {
            Content = JsonContent.Create(new { g.DonemStart, g.Kanal, g.TutarTl, BeklenenTutar = beklenenTutar }, options: Json),
        };
        using var yanit = await HamGonderAsync(istek);
        if (yanit.StatusCode == HttpStatusCode.Conflict)
        {
            var govde = await GovdeOkuAsync(yanit);
            var mesaj = govde is { } el && el.TryGetProperty("hata", out var h) ? h.GetString() : null;
            if (govde is { } el2 && el2.TryGetProperty("mevcutTutar", out var m) && m.TryGetDecimal(out var mevcut))
                return new GelenKayitSonucu(false, null, mevcut, mesaj);
            // Kısıt çakışması (aynı anda iki ekleme): güncel değeri yeniden oku.
            var guncel = (await GelenlerAsync(g.DonemStart)).FirstOrDefault(x => x.Kanal == g.Kanal);
            return new GelenKayitSonucu(false, guncel, guncel?.TutarTl ?? 0m, mesaj);
        }
        await BasarisizsaFirlatAsync(yanit);
        var kayit = (await yanit.Content.ReadFromJsonAsync<GelenDto>(Json))!;
        return new GelenKayitSonucu(true, kayit, kayit.TutarTl, null);
    }

    public Task<GelenTablosuDto> GelenTablosuAsync(DateOnly? donemStart = null)
        => GetAsync<GelenTablosuDto>(donemStart is { } d
            ? $"api/gelenler/tablo?donemStart={d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : "api/gelenler/tablo");

    public async Task<EksikGelenSayfasi> EksikGelenListesiAsync()
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, "api/gelenler/eksik-liste");
        using var yanit = await GonderAsync(istek);
        var kayitlar = (await yanit.Content.ReadFromJsonAsync<IReadOnlyList<EksikGelenSatiriDto>>(Json))!;
        return new EksikGelenSayfasi(kayitlar, ToplamKayit(yanit, kayitlar.Count));
    }

    public async Task<DegisiklikDto?> SonSilmeAsync(string tur, int kayitId)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get,
            FormattableString.Invariant($"api/gecmis/son-silme?tur={Uri.EscapeDataString(tur)}&kayitId={kayitId}"));
        using var yanit = await HamGonderAsync(istek);
        if (yanit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return null;
        await BasarisizsaFirlatAsync(yanit);
        return await yanit.Content.ReadFromJsonAsync<DegisiklikDto>(Json);
    }

    // ---- altyapı: durum koduna göre dallanan uçlar için ----

    /// <summary>
    /// <see cref="GonderAsync"/> gibi token ekler ve 401'de oturumu düşürür; diğer başarısız kodlarda
    /// istisna atmaz, yanıtı çağırana bırakır (409/400 gövdesi, 204/404 gibi beklenen durumlar için).
    /// </summary>
    private async Task<HttpResponseMessage> HamGonderAsync(HttpRequestMessage istek)
    {
        var token = await _store.OkuAsync();
        if (!string.IsNullOrEmpty(token))
            istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var yanit = await _http.SendAsync(istek);
        if (yanit.StatusCode == HttpStatusCode.Unauthorized)
        {
            var mesaj = await SunucuHatasiAsync(yanit);
            yanit.Dispose();
            if (!string.IsNullOrEmpty(token)) await OturumuDusurAsync(token, olayTetikle: true);
            throw new KasaApiException(HttpStatusCode.Unauthorized, mesaj);
        }
        return yanit;
    }

    private static async Task BasarisizsaFirlatAsync(HttpResponseMessage yanit)
    {
        if (yanit.IsSuccessStatusCode) return;
        throw new KasaApiException(yanit.StatusCode, await SunucuHatasiAsync(yanit));
    }

    private static async Task<JsonElement?> GovdeOkuAsync(HttpResponseMessage yanit)
    {
        try
        {
            var el = await yanit.Content.ReadFromJsonAsync<JsonElement>(Json);
            return el.ValueKind == JsonValueKind.Object ? el : null;
        }
        catch (Exception) { return null; }
    }
}
