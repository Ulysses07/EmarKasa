using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient : IKasaApi
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>İstemci tarafı istek zaman aşımı (HttpClient varsayılanı 100 sn'dir).</summary>
    public static readonly TimeSpan ZamanAsimi = TimeSpan.FromSeconds(30);

    public event EventHandler<OturumBitisNedeni>? OturumSonaErdi;

    public KasaApiClient(HttpClient http, ITokenStore store)
    {
        _http = http;
        _store = store;
    }

    /// <summary>
    /// Uygulamanın kullanacağı HttpClient: çerez saklamaz (kimlik yalnız Bearer token'la gider;
    /// sunucunun <c>kasa_auth</c> çerezi Bearer'ın önüne geçmesin) ve 30 sn zaman aşımı vardır.
    /// </summary>
    public static HttpClient HttpOlustur(Uri adres, HttpMessageHandler? isleyici = null)
        => new(isleyici ?? IsleyiciOlustur()) { BaseAddress = adres, Timeout = ZamanAsimi };

    /// <summary>Çerez kapalı HTTP işleyicisi.</summary>
    public static HttpClientHandler IsleyiciOlustur() => new() { UseCookies = false };

    public async Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre }, options: Json),
        };
        using var yanit = await GonderAsync(istek, tokenEkle: false);
        var login = (await yanit.Content.ReadFromJsonAsync<LoginYanit>(Json))!;
        await _store.YazAsync(login.Token);
        return login;
    }

    public async Task<string?> BenKimAsync()
    {
        var el = await GetAsync<RolYanit>("api/auth/me");
        return el.Rol;
    }

    public async Task CikisAsync()
    {
        try
        {
            using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            using var _ = await GonderAsync(istek, oturumOlayi: false);
        }
        finally
        {
            // Çevrimdışıyken ya da sunucu 401 dönse bile yerel oturum kapanmalı.
            await _store.TemizleAsync();
        }
    }

    private record RolYanit(string Rol);

    // ---- okuma metotları ----

    public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => GetAsync<IReadOnlyList<HaftalikOzetDto>>("api/rapor/haftalik");
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) => GetAsync<AylikRaporDto>($"api/rapor/aylik?yil={yil}&ay={ay}");
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => GetAsync<IReadOnlyList<DonemDto>>("api/donemler");
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => GetAsync<IReadOnlyList<KanalDto>>("api/kanallar");
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => GetAsync<IReadOnlyList<KrediKartiDto>>("api/kredikartlari");
    public Task<AyarlarDto> AyarlarAsync() => GetAsync<AyarlarDto>("api/ayarlar");

    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null)
        => GetAsync<IReadOnlyList<CariDto>>(ara is null ? "api/cariler" : $"api/cariler?ara={Uri.EscapeDataString(ara)}");

    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null)
        => GetAsync<IReadOnlyList<GelenDto>>(donemStart is { } d ? $"api/gelenler?donemStart={d:yyyy-MM-dd}" : "api/gelenler");

    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(
        DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null,
        int? limit = null, int? offset = null)
        => GetAsync<IReadOnlyList<IslemDto>>(IslemYolu(baslangic, bitis, kanal, cari, limit, offset));

    public async Task<IslemSayfasi> IslemSayfasiAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, int limit, int offset)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, IslemYolu(baslangic, bitis, kanal, cari, limit, offset));
        using var yanit = await GonderAsync(istek);
        var kayitlar = (await yanit.Content.ReadFromJsonAsync<IReadOnlyList<IslemDto>>(Json))!;
        // Başlık yoksa (eski sunucu) toplam, gelen kayıtlardan çıkarılır.
        var toplam = yanit.Headers.TryGetValues("X-Toplam-Kayit", out var d)
            && int.TryParse(d.FirstOrDefault(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var t) && t >= 0
            ? t
            : offset + kayitlar.Count;
        return new IslemSayfasi(kayitlar, toplam);
    }

    private static string IslemYolu(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, int? limit, int? offset,
        string taban = "api/islemler")
    {
        var q = new List<string>();
        if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(kanal)) q.Add($"kanal={Uri.EscapeDataString(kanal)}");
        if (!string.IsNullOrWhiteSpace(cari)) q.Add($"cari={Uri.EscapeDataString(cari)}");
        if (limit is { } l) q.Add(FormattableString.Invariant($"limit={l}"));
        if (offset is { } o) q.Add(FormattableString.Invariant($"offset={o}"));
        return q.Count > 0 ? $"{taban}?{string.Join("&", q)}" : taban;
    }

    // ---- Excel'e aktar (CSV) ----

    public Task<IndirilenDosya> IslemlerCsvAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
        => IndirAsync(IslemYolu(baslangic, bitis, kanal, cari, null, null, "api/disaaktar/islemler.csv"), "kasa-islemler.csv");
    public Task<IndirilenDosya> HaftalikCsvAsync() => IndirAsync("api/disaaktar/haftalik.csv", "kasa-haftalik.csv");
    public Task<IndirilenDosya> AylikCsvAsync(int yil, int ay)
        => IndirAsync(FormattableString.Invariant($"api/disaaktar/aylik.csv?yil={yil}&ay={ay}"),
            FormattableString.Invariant($"kasa-aylik-{yil:D4}-{ay:D2}.csv"));

    /// <summary>
    /// Dosyayı indirir. Ad, sunucunun Content-Disposition başlığından (filename*, yoksa filename)
    /// alınır; başlık yoksa <paramref name="varsayilanAd"/>. Ad burada yalnız okunur; diske yazan
    /// taraf yine de güvenli hale getirmelidir.
    /// </summary>
    private async Task<IndirilenDosya> IndirAsync(string yol, string varsayilanAd)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek);
        var cd = yanit.Content.Headers.ContentDisposition;
        var ad = (cd?.FileNameStar ?? cd?.FileName)?.Trim().Trim('"');
        var icerik = await yanit.Content.ReadAsByteArrayAsync();
        return new IndirilenDosya(string.IsNullOrWhiteSpace(ad) ? varsayilanAd : ad, icerik);
    }

    // ---- mutasyon metotları ----

    // Kanal
    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Post, "api/kanallar", g);
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Put, $"api/kanallar/{id}", g);
    public Task KanalSilAsync(int id) => SilAsync($"api/kanallar/{id}");

    // Cari
    public Task<CariDto> CariOlusturAsync(CariYaz g) => GonderJsonAsync<CariDto>(HttpMethod.Post, "api/cariler", g);
    public Task<CariDto> CariGuncelleAsync(int id, CariYaz g) => GonderJsonAsync<CariDto>(HttpMethod.Put, $"api/cariler/{id}", g);
    public Task CariSilAsync(int id) => SilAsync($"api/cariler/{id}");

    // Sabit gider kalemi
    public Task<IReadOnlyList<GiderKalemiDto>> GiderKalemleriAsync() => GetAsync<IReadOnlyList<GiderKalemiDto>>("api/giderkalemleri");
    public Task<GiderKalemiDto> GiderKalemiOlusturAsync(GiderKalemiYaz g) => GonderJsonAsync<GiderKalemiDto>(HttpMethod.Post, "api/giderkalemleri", g);
    public Task<GiderKalemiDto> GiderKalemiGuncelleAsync(int id, GiderKalemiYaz g) => GonderJsonAsync<GiderKalemiDto>(HttpMethod.Put, $"api/giderkalemleri/{id}", g);
    public Task GiderKalemiSilAsync(int id) => SilAsync($"api/giderkalemleri/{id}");

    // İşlem
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Post, "api/islemler", g);
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Put, $"api/islemler/{id}", g);
    public Task IslemSilAsync(int id) => SilAsync($"api/islemler/{id}");

    // Kredi kartı
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Post, "api/kredikartlari", g);
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Put, $"api/kredikartlari/{id}", g);
    public Task KrediKartiSilAsync(int id) => SilAsync($"api/kredikartlari/{id}");

    // Kart ödeme
    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId) => GetAsync<IReadOnlyList<KartOdemeDto>>($"api/kartodemeler?krediKartiId={krediKartiId}");
    public Task<IReadOnlyList<KartOdemeDto>> TumKartOdemeleriAsync() => GetAsync<IReadOnlyList<KartOdemeDto>>("api/kartodemeler");
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) => GonderJsonAsync<KartOdemeDto>(HttpMethod.Post, "api/kartodemeler", g);
    public Task KartOdemeSilAsync(int id) => SilAsync($"api/kartodemeler/{id}");

    // Gelen upsert
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) => GonderJsonAsync<GelenDto>(HttpMethod.Put, "api/gelenler", g);

    // Çek
    public Task<IReadOnlyList<CekDto>> CeklerAsync(CekYonu? yon = null, CekDurumu? durum = null, DateOnly? baslangic = null, DateOnly? bitis = null)
    {
        var q = new List<string>();
        if (yon is { } y) q.Add($"yon={y}");
        if (durum is { } d) q.Add($"durum={d}");
        if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
        return GetAsync<IReadOnlyList<CekDto>>(q.Count > 0 ? $"api/cekler?{string.Join("&", q)}" : "api/cekler");
    }
    public Task<CekOzetDto> CekOzetAsync() => GetAsync<CekOzetDto>("api/cekler/ozet");
    public Task<CekDto> CekOlusturAsync(CekYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Post, "api/cekler", g);
    public Task<CekDto> CekGuncelleAsync(int id, CekYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Put, $"api/cekler/{id}", g);
    public Task CekSilAsync(int id) => SilAsync($"api/cekler/{id}");

    // Kasa sayımı
    public Task<IReadOnlyList<KasaSayimDto>> KasaSayimlariAsync() => GetAsync<IReadOnlyList<KasaSayimDto>>("api/kasasayimlari");
    public Task<KasaHesapDto> KasaHesaplaAsync(DateOnly tarih)
        => GetAsync<KasaHesapDto>($"api/kasasayimlari/hesapla?tarih={tarih.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}");
    public Task<KasaSayimDto> KasaSayimKaydetAsync(KasaSayimYaz g) => GonderJsonAsync<KasaSayimDto>(HttpMethod.Post, "api/kasasayimlari", g);
    public Task KasaSayimSilAsync(int id) => SilAsync($"api/kasasayimlari/{id}");

    // Ayarlar
    public Task AyarGuncelleAsync(AyarYaz g) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar", g);
    public Task IzleyiciSifreAsync(string yeniSifre) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar/izleyici-sifre", new { yeniSifre });
    public async Task OturumlariKapatAsync()
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/ayarlar/oturumlari-kapat");
        using var _ = await GonderAsync(istek);
        await _store.TemizleAsync();
        OturumSonaErdi?.Invoke(this, OturumBitisNedeni.OturumlarKapatildi);
    }

    // ---- altyapı ----

    /// <param name="oturumOlayi">false ise 401'de <see cref="OturumSonaErdi"/> tetiklenmez (örn. çıkış isteği).</param>
    private async Task<HttpResponseMessage> GonderAsync(HttpRequestMessage istek, bool tokenEkle = true, bool oturumOlayi = true)
    {
        string? gonderilenToken = null;
        if (tokenEkle)
        {
            gonderilenToken = await _store.OkuAsync();
            if (!string.IsNullOrEmpty(gonderilenToken))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gonderilenToken);
        }
        var yanit = await _http.SendAsync(istek);
        if (!yanit.IsSuccessStatusCode)
        {
            var kod = yanit.StatusCode;
            var mesaj = await SunucuHatasiAsync(yanit);
            yanit.Dispose();
            if (kod == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(gonderilenToken))
                await OturumuDusurAsync(gonderilenToken, oturumOlayi);
            throw new KasaApiException(kod, mesaj);
        }
        return yanit;
    }

    /// <summary>
    /// Reddedilen token'ı siler ve oturum olayını tetikler. Bu arada yeniden giriş yapılmışsa
    /// (depoda başka token varsa) eski isteğin 401'i yeni oturumu bozmaz.
    /// </summary>
    private async Task OturumuDusurAsync(string reddedilenToken, bool olayTetikle)
    {
        var simdiki = await _store.OkuAsync();
        if (simdiki != reddedilenToken) return;
        await _store.TemizleAsync();
        if (olayTetikle) OturumSonaErdi?.Invoke(this, OturumBitisNedeni.Yetkisiz);
    }

    /// <summary>Sunucunun doğrulama yanıtındaki <c>{ "hata": "..." }</c> metnini okur (yoksa null).</summary>
    private static async Task<string?> SunucuHatasiAsync(HttpResponseMessage yanit)
    {
        if (yanit.StatusCode == HttpStatusCode.TooManyRequests)
            return "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin.";
        try
        {
            var el = await yanit.Content.ReadFromJsonAsync<JsonElement>(Json);
            return el.ValueKind == JsonValueKind.Object && el.TryGetProperty("hata", out var h) ? h.GetString() : null;
        }
        catch (Exception) { return null; }
    }

    private async Task<T> GetAsync<T>(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> GonderJsonAsync<T>(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task GonderJsonAsync(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var _ = await GonderAsync(istek);
    }

    private async Task SilAsync(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Delete, yol);
        using var _ = await GonderAsync(istek);
    }
}
