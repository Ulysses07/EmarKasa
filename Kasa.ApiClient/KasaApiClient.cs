using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient : IKasaApi, IOturumBildirimleri
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly KasaZamanAsimlari _zaman;
    private readonly SemaphoreSlim _oturumKilidi = new(1, 1);
    public event EventHandler? OturumSonlandi;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <param name="zamanAsimlari">İstek başına süre sınırları; verilmezse <see cref="KasaZamanAsimlari.Varsayilanlar"/>.</param>
    public KasaApiClient(HttpClient http, ITokenStore store, KasaZamanAsimlari? zamanAsimlari = null)
    {
        _http = http;
        _store = store;
        _zaman = zamanAsimlari ?? KasaZamanAsimlari.Varsayilanlar;
    }

    /// <summary>Tanıdık cihaz belirtecinin gönderildiği başlık (sunucuda TanidikCihaz.BaslikAdi).</summary>
    public const string TanidikCihazBasligi = "X-Kasa-Cihaz";

    /// <summary>Tanıdık cihaz belirteçlerinin rol başına saklandığı roller (sunucunun giriş ve /me yanıtındaki 'rol').</summary>
    public static readonly IReadOnlyList<string> CihazRolleri = ["editor", "viewer", "alici"];

    public async Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre }, options: Json),
        };
        // Tanıdık cihaz belirteçleri yalnız girişte gider: dağıtık saldırı hedefi kilitlese de bu cihazdan girilir.
        // İstemci adın hangi role düştüğünü bilmez; saklıların hepsi gider, sunucu denenen hedefe ait olanı kabul eder.
        if (await SakliCihazlarAsync() is { Count: > 0 } cihazlar)
            istek.Headers.TryAddWithoutValidation(TanidikCihazBasligi, string.Join(",", cihazlar));
        using var yanit = await GonderAsync(istek, tokenEkle: false);
        var login = (await yanit.Content.ReadFromJsonAsync<LoginYanit>(Json))!;
        await _oturumKilidi.WaitAsync();
        try
        { await _store.YazAsync(login.Token); }
        finally { _oturumKilidi.Release(); }
        // Her başarılı giriş kendi rolünün belirtecini yeniler; belirteçsiz yanıt (eski sunucu) saklananı silmez.
        await CihazSaklaAsync(login.Rol, login.Cihaz);
        return login;
    }

    public async Task<string?> BenKimAsync()
    {
        var el = await GetAsync<RolYanit>("api/auth/me");
        // Oturum doğrulaması (açılış) belirteci yeniler: kullanılan cihaz, oturum dolduğunda da tanıdık kalır.
        await CihazSaklaAsync(el.Rol, el.Cihaz);
        return el.Rol;
    }

    /// <summary>Saklı tanıdık cihaz belirteçleri. Belirteç isteğe bağlıdır: güvenli depo bir kaydı okuyamazsa
    /// (ör. Windows profili ya da DPAPI anahtarı değişti) o kayıt atlanır, giriş başlıksız da sürer.</summary>
    private async Task<List<string>> SakliCihazlarAsync()
    {
        var cihazlar = new List<string>();
        foreach (var rol in CihazRolleri)
        {
            try
            {
                if (await _store.CihazOkuAsync(rol) is { Length: > 0 } belirtec && !belirtec.Contains(','))
                    cihazlar.Add(belirtec);
            }
            catch (Exception) { /* Okunamayan kayıt yok sayılır; sunucu başarılı girişte yenisini verir. */ }
        }
        return cihazlar;
    }

    /// <summary>Sunucunun verdiği belirteci rolüne yazar. Yazma hatası (güvenli depo) işlemi bozmaz: oturum zaten
    /// açıldı ya da değişti; cihaz yalnız bir sonraki belirtece kadar tanınmaz.</summary>
    private async Task CihazSaklaAsync(string? rol, string? belirtec)
    {
        if (string.IsNullOrEmpty(belirtec) || rol is null || !CihazRolleri.Contains(rol))
            return;
        try
        { await _store.CihazYazAsync(rol, belirtec); }
        catch (Exception) { /* Belirteç isteğe bağlıdır. */ }
    }

    public async Task CikisAsync()
    {
        try
        {
            using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            using var _ = await GonderAsync(istek);
        }
        finally { await _store.TemizleAsync(); }
    }

    private record RolYanit(string Rol, string? Cihaz = null);

    // ---- okuma metotları ----

    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<HaftalikOzetDto>>("api/rapor/haftalik", ct);

    /// <summary>Uç bir kez 404 verdiyse (eski sunucu) sonraki yüklemeler doğrudan panele gider; uygulama yeniden
    /// başlatılınca yeniden denenir.</summary>
    private volatile bool _anaSayfaUcuYok;

    public async Task<AnaSayfaDto> AnaSayfaAsync(int gun = 30, CancellationToken ct = default)
    {
        if (!_anaSayfaUcuYok)
        {
            try
            { return await GetAsync<AnaSayfaDto>($"api/rapor/ana-sayfa?gun={gun}", ct); }
            catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.NotFound) { _anaSayfaUcuYok = true; }
            // Birleşik ucun sunucu hatası (5xx; ör. takip özeti hesaplanamadı) kasa bakiyelerini gizlemez: panel ayrı uçtan
            // alınır, eşikler ve özet çağıranca kendi uçlarından (kendi hatalarıyla) yüklenir. Uç sonraki yüklemede yeniden denenir.
            catch (KasaApiException e) when ((int)e.DurumKodu >= 500) { }
        }
        // Eski sunucu ya da birleşik uç hatası: panel tek başına; eşikler ve takip özeti çağıranca eski uçlardan yüklenir.
        return new(await GetAsync<PanelDto>("api/rapor/panel", ct), null, null);
    }
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) => GetAsync<AylikRaporDto>($"api/rapor/aylik?yil={yil}&ay={ay}");
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => GetAsync<IReadOnlyList<DonemDto>>("api/donemler");
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => GetAsync<IReadOnlyList<KanalDto>>("api/kanallar");
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => GetAsync<IReadOnlyList<KrediKartiDto>>("api/kredikartlari");
    public Task<AyarlarDto> AyarlarAsync() => GetAsync<AyarlarDto>("api/ayarlar");


    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null)
        => GetAsync<IReadOnlyList<GelenDto>>(donemStart is { } d ? $"api/gelenler?donemStart={d:yyyy-MM-dd}" : "api/gelenler");

    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(
        DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        var q = new List<string>();
        if (baslangic is { } b)
            q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s)
            q.Add($"bitis={s:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(kanal))
            q.Add($"kanal={Uri.EscapeDataString(kanal)}");
        if (!string.IsNullOrWhiteSpace(cari))
            q.Add($"cari={Uri.EscapeDataString(cari)}");
        var yol = q.Count > 0 ? $"api/islemler?{string.Join("&", q)}" : "api/islemler";
        return GetAsync<IReadOnlyList<IslemDto>>(yol);
    }

    // ---- mutasyon metotları ----

    // Kanal
    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Post, "api/kanallar", g);
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Put, $"api/kanallar/{id}", g);
    public Task KanalSilAsync(int id) => SilAsync($"api/kanallar/{id}");

    // Cari

    // İşlem
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Post, "api/islemler", g);
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Put, $"api/islemler/{id}", g);
    public Task IslemSilAsync(int id) => SilAsync($"api/islemler/{id}");

    // Gelen upsert
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) => GonderJsonAsync<GelenDto>(HttpMethod.Put, "api/gelenler", g);

    // Ayarlar
    public Task AyarGuncelleAsync(AyarYaz g) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar", g);
    public Task IzleyiciSifreAsync(string yeniSifre) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar/izleyici-sifre", new { yeniSifre });

    // ---- altyapı ----

    /// <summary>İsteği gönderir; yanıt gövdesi süre sınırı içinde belleğe alınmış olarak döner (JSON ve kısa yanıtlar).
    /// Süre verilmezse normal çağrı sınırı (<see cref="KasaZamanAsimlari.Varsayilan"/>) uygulanır.</summary>
    private Task<HttpResponseMessage> GonderAsync(HttpRequestMessage istek, bool tokenEkle = true, TimeSpan? zamanAsimi = null, CancellationToken cancellationToken = default)
        => SureliAsync(zamanAsimi ?? _zaman.Varsayilan, cancellationToken, t => YanitAlAsync(istek, tokenEkle, HttpCompletionOption.ResponseContentRead, t));

    /// <summary>İşlemi istek başına süre sınırıyla çalıştırır; sınır, işlemin gövde okuması dahil tamamını kapsar.
    /// Süre (ya da HttpClient.Timeout) dolarsa <see cref="TimeoutException"/>; çağıranın iptali OperationCanceledException
    /// olarak kalır. Kullanıcıya "sunucu yanıt vermedi" ile "vazgeçildi" farklı anlatılır.</summary>
    private static async Task<T> SureliAsync<T>(TimeSpan sure, CancellationToken iptal, Func<CancellationToken, Task<T>> islem)
    {
        using var kaynak = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        kaynak.CancelAfter(sure);
        try
        { return await islem(kaynak.Token); }
        catch (OperationCanceledException e) when (!iptal.IsCancellationRequested && (kaynak.IsCancellationRequested || e.InnerException is TimeoutException))
        {
            throw new TimeoutException(KasaZamanAsimlari.Ileti, e);
        }
    }

    /// <summary>Bearer ekleyip gönderir; başarısız yanıtı KasaApiException'a çevirir (401 oturumu kapatır).</summary>
    private async Task<HttpResponseMessage> YanitAlAsync(HttpRequestMessage istek, bool tokenEkle, HttpCompletionOption tamamlama, CancellationToken ct)
    {
        string? token = null;
        if (tokenEkle)
        {
            token = await _store.OkuAsync();
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        var yanit = await _http.SendAsync(istek, tamamlama, ct);
        if (!yanit.IsSuccessStatusCode)
        {
            using (yanit)
            {
                if (yanit.StatusCode == HttpStatusCode.Unauthorized && tokenEkle)
                    await OturumuGecersizKilAsync(token);
                var (mesaj, iz, alanlar) = await HataAyrintisiAsync(yanit, ct);
                throw new KasaApiException(yanit.StatusCode, mesaj, iz, alanlar);
            }
        }
        return yanit;
    }

    private async Task OturumuGecersizKilAsync(string? istekTokeni, OturumSonuNedeni neden = OturumSonuNedeni.OturumGecersiz)
    {
        var temizlendi = false;
        await _oturumKilidi.WaitAsync();
        try
        {
            // Eski bir isteğin 401 yanıtı, bu sırada açılmış yeni oturumu kapatmasın.
            if (istekTokeni is not null && await _store.OkuAsync() == istekTokeni)
            {
                await _store.TemizleAsync();
                temizlendi = true;
            }
        }
        finally { _oturumKilidi.Release(); }
        if (temizlendi)
            OturumSonlandi?.Invoke(this, new OturumSonlandiEventArgs(neden));
    }

    /// <summary>Hata yanıtından kullanıcıya taşınan ileti (yalnız sunucunun anlamlı Türkçe ileti verdiği durumlarda), sunucu
    /// hatasının (5xx) ProblemDetails iz kimliği (traceId; kullanıcıya kısa "Hata kodu" olarak gösterilir) ve iletili yanıttaki
    /// alan hataları (<see cref="AlanHatalari"/>).</summary>
    private static async Task<(string? Mesaj, string? Iz, IReadOnlyDictionary<string, string>? Alanlar)> HataAyrintisiAsync(HttpResponseMessage yanit, CancellationToken ct)
    {
        var iletiVar = yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
        var sunucuHatasi = (int)yanit.StatusCode >= 500;
        if (!iletiVar && !sunucuHatasi)
            return (null, null, null);
        try
        {
            using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync(ct));
            var kok = belge.RootElement;
            var iz = sunucuHatasi && kok.ValueKind == JsonValueKind.Object && kok.TryGetProperty("traceId", out var izDegeri) && izDegeri.ValueKind == JsonValueKind.String
                ? izDegeri.GetString() : null;
            return iletiVar ? (Ileti(kok), iz, AlanHatalari(kok)) : (null, iz, null);
        }
        catch (JsonException) { /* JSON dışındaki hata gövdesini kullanıcıya taşıma. */ }
        return (null, null, null);
    }

    /// <summary>Doğrulama yanıtının "errors" sözlüğü: alan adı küçük harfe iner (sunucu camelCase yazar: "krediKartiId" →
    /// "kredikartiid"), her alandan ilk dolu ileti alınır. Sözlük yoksa ya da boşsa null.</summary>
    private static Dictionary<string, string>? AlanHatalari(JsonElement kok)
    {
        if (kok.ValueKind != JsonValueKind.Object || !kok.TryGetProperty("errors", out var hatalar) || hatalar.ValueKind != JsonValueKind.Object)
            return null;
        var alanlar = new Dictionary<string, string>();
        foreach (var alan in hatalar.EnumerateObject())
        {
            if (alan.Value.ValueKind != JsonValueKind.Array)
                continue;
            var ilk = alan.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            if (ilk is not null)
                alanlar.TryAdd(alan.Name.ToLowerInvariant(), ilk);
        }
        return alanlar.Count > 0 ? alanlar : null;
    }

    private static string? Ileti(JsonElement kok)
    {
        if (kok.ValueKind == JsonValueKind.String)
            return kok.GetString();
        if (kok.ValueKind != JsonValueKind.Object)
            return null;
        if (kok.TryGetProperty("errors", out var hatalar) && hatalar.ValueKind == JsonValueKind.Object)
        {
            var mesajlar = hatalar.EnumerateObject().SelectMany(h => h.Value.ValueKind == JsonValueKind.Array
                ? h.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())
                : Array.Empty<string?>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
            var mesaj = string.Join("\n", mesajlar);
            if (mesaj.Length > 0)
                return mesaj;
        }
        foreach (var alan in new[] { "detail", "hata", "message", "title" })
            if (kok.TryGetProperty(alan, out var deger) && deger.ValueKind == JsonValueKind.String)
                return deger.GetString();
        return null;
    }

    private async Task<T> GetAsync<T>(string yol, CancellationToken ct = default)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek, cancellationToken: ct);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json, ct))!;
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
