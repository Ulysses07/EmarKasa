using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient
{
    /// <summary>Cihaz adı başlığı: sunucu giriş günlüğüne ve oturum listesine yazar.</summary>
    public const string CihazBasligi = "X-Kasa-Cihaz";

    /// <summary>
    /// Bu cihazın adını (ör. bilgisayar adı "EMAR-LAPTOP") her isteğe ekler. Ad URL kodlanır
    /// (Türkçe harfler başlıkta bozulmasın); boşsa eklenmez.
    /// </summary>
    public static void CihazAdiEkle(HttpClient http, string? cihazAdi)
    {
        http.DefaultRequestHeaders.Remove(CihazBasligi);
        var ad = cihazAdi?.Trim();
        if (string.IsNullOrEmpty(ad)) return;
        if (ad.Length > 64) ad = ad[..64];
        http.DefaultRequestHeaders.TryAddWithoutValidation(CihazBasligi, Uri.EscapeDataString(ad));
    }

    private sealed record GirisYaniti(string Rol, string Token, string? Ad, int? KullaniciId);
    private sealed record TokenYaniti(string Token);
    private sealed record KodlarYaniti(IReadOnlyList<string> Kodlar, string? Token);

    public async Task<GirisSonucu> GirisAsync(string? kullanici, string sifre, string? kod)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre, kod = string.IsNullOrWhiteSpace(kod) ? null : kod.Trim() }, options: Json),
        };
        using var yanit = await _http.SendAsync(istek);
        if (yanit.StatusCode == HttpStatusCode.Unauthorized)
        {
            var (hata, kodGerekli) = await GirisHatasiAsync(yanit);
            if (kodGerekli) return GirisSonucu.KodIsteniyor(hata);
            throw new KasaApiException(HttpStatusCode.Unauthorized, hata);
        }
        if (!yanit.IsSuccessStatusCode)
            throw new KasaApiException(yanit.StatusCode, await SunucuHatasiAsync(yanit));
        var g = (await yanit.Content.ReadFromJsonAsync<GirisYaniti>(Json))!;
        await _store.YazAsync(g.Token);
        return new GirisSonucu(g.Rol, g.Ad, g.KullaniciId);
    }

    private static async Task<(string? Hata, bool KodGerekli)> GirisHatasiAsync(HttpResponseMessage yanit)
    {
        try
        {
            var el = await yanit.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (el.ValueKind != JsonValueKind.Object) return (null, false);
            var hata = el.TryGetProperty("hata", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            var kod = el.TryGetProperty("kodGerekli", out var k) && k.ValueKind == JsonValueKind.True;
            return (hata, kod);
        }
        catch (Exception) { return (null, false); }
    }

    public Task<BenDto> BenAsync() => GetAsync<BenDto>("api/auth/me");

    // Hesabım
    public Task<HesapDto> HesabimAsync() => GetAsync<HesapDto>("api/hesap");

    public async Task SifremiDegistirAsync(string mevcutSifre, string yeniSifre)
    {
        var y = await GonderJsonAsync<TokenYaniti>(HttpMethod.Post, "api/hesap/sifre", new { mevcutSifre, yeniSifre });
        await _store.YazAsync(y.Token);
    }

    public Task<IkiAdimKurulumDto> IkiAdimBaslatAsync() => GonderJsonAsync<IkiAdimKurulumDto>(HttpMethod.Post, "api/hesap/iki-adim/baslat", new { });

    public async Task<IReadOnlyList<string>> IkiAdimOnaylaAsync(string sifre, string kod)
    {
        var y = await GonderJsonAsync<KodlarYaniti>(HttpMethod.Post, "api/hesap/iki-adim/onayla", new { sifre, kod });
        if (!string.IsNullOrEmpty(y.Token)) await _store.YazAsync(y.Token);
        return y.Kodlar;
    }

    public Task IkiAdimKapatAsync(string sifre, string kod) => GonderJsonAsync(HttpMethod.Post, "api/hesap/iki-adim/kapat", new { sifre, kod });

    public async Task<IReadOnlyList<string>> KurtarmaKodlariYenileAsync(string sifre, string kod)
        => (await GonderJsonAsync<KodlarYaniti>(HttpMethod.Post, "api/hesap/kurtarma-kodlari", new { sifre, kod })).Kodlar;

    // Kullanıcılar
    public Task<IReadOnlyList<KullaniciDto>> KullanicilarAsync() => GetAsync<IReadOnlyList<KullaniciDto>>("api/kullanicilar");
    public Task<KullaniciDto> KullaniciEkleAsync(KullaniciEkle g) => GonderJsonAsync<KullaniciDto>(HttpMethod.Post, "api/kullanicilar", g);
    public Task KullaniciGuncelleAsync(int id, KullaniciGuncelle g) => GonderJsonAsync(HttpMethod.Put, $"api/kullanicilar/{id}", g);
    public Task KullaniciSifreAsync(int id, string yeniSifre) => GonderJsonAsync(HttpMethod.Post, $"api/kullanicilar/{id}/sifre", new { yeniSifre });
    public Task KullaniciOturumlariniKapatAsync(int id) => GonderJsonAsync(HttpMethod.Post, $"api/kullanicilar/{id}/oturumlari-kapat", new { });
    public Task KullaniciIkiAdimKapatAsync(int id) => GonderJsonAsync(HttpMethod.Post, $"api/kullanicilar/{id}/iki-adim-kapat", new { });
    public Task KullaniciSilAsync(int id) => SilAsync($"api/kullanicilar/{id}");
    public Task IzleyiciSifresiniKaldirAsync() => SilAsync("api/ayarlar/izleyici-sifre");

    // Oturumlar ve giriş günlüğü
    public Task<IReadOnlyList<OturumDto>> OturumlarAsync() => GetAsync<IReadOnlyList<OturumDto>>("api/oturumlar");
    public Task OturumKapatAsync(string oturumId)
        => GonderJsonAsync(HttpMethod.Post, $"api/oturumlar/{Uri.EscapeDataString(oturumId)}/kapat", new { });

    public async Task<GirisKaydiSayfasi> GirisKayitlariAsync(bool yalnizBasarisiz, int limit, int offset)
    {
        var yol = FormattableString.Invariant($"api/guvenlik/girisler?limit={limit}&offset={offset}");
        if (yalnizBasarisiz) yol += "&basarisiz=true";
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek);
        var kayitlar = (await yanit.Content.ReadFromJsonAsync<IReadOnlyList<GirisKaydiDto>>(Json))!;
        return new GirisKaydiSayfasi(kayitlar, ToplamKayit(yanit, offset + kayitlar.Count));
    }

    public Task<GuvenlikAyariDto> GuvenlikAyariAsync() => GetAsync<GuvenlikAyariDto>("api/guvenlik/ayar");
    public Task GuvenlikAyariKaydetAsync(int editorOturumGun) => GonderJsonAsync(HttpMethod.Put, "api/guvenlik/ayar", new { editorOturumGun });

    // Sorular
    public Task<IReadOnlyList<SoruDto>> SorularAsync(SoruDurumu? durum = null, SoruHedefTuru? hedefTur = null, int? hedefId = null, DateOnly? hafta = null)
    {
        var q = new List<string>();
        if (durum is { } d) q.Add($"durum={d}");
        if (hedefTur is { } t) q.Add($"hedefTur={t}");
        if (hedefId is { } i) q.Add(FormattableString.Invariant($"hedefId={i}"));
        if (hafta is { } h) q.Add($"hafta={h.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}");
        return GetAsync<IReadOnlyList<SoruDto>>(q.Count > 0 ? $"api/sorular?{string.Join("&", q)}" : "api/sorular");
    }
    public Task<SoruOzetDto> SoruOzetAsync() => GetAsync<SoruOzetDto>("api/sorular/ozet");
    public Task<SoruDto> SoruSorAsync(SoruYaz g) => GonderJsonAsync<SoruDto>(HttpMethod.Post, "api/sorular", g);
    public Task<SoruDto> SoruCevaplaAsync(int id, string cevap, bool kapat) => GonderJsonAsync<SoruDto>(HttpMethod.Post, $"api/sorular/{id}/cevap", new { cevap, kapat });
    public Task<SoruDto> SoruKapatAsync(int id) => GonderJsonAsync<SoruDto>(HttpMethod.Post, $"api/sorular/{id}/kapat", new { });
    public Task<SoruDto> SoruAcAsync(int id) => GonderJsonAsync<SoruDto>(HttpMethod.Post, $"api/sorular/{id}/ac", new { });
    public Task SoruSilAsync(int id) => SilAsync($"api/sorular/{id}");

    // Sistem
    public Task<SistemRiskDto> SistemRiskiAsync() => GetAsync<SistemRiskDto>("api/sistem/risk");
}
