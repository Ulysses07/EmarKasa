using System.Net.Http.Headers;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : IYonetimApi
{
    public async Task SifreDegistirAsync(SifreDegistirYaz g)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/sifre") { Content = System.Net.Http.Json.JsonContent.Create(g, options: Json) };
        using var yanit = await GonderAsync(istek);
        // Eski belirteç yeni şifreyle düştü; sunucu bu cihaza yenisini gövdesiz yanıtın başlığında verir.
        await CihazSaklaAsync("editor", BaslikBelirteci(yanit));
        await OturumuGecersizKilAsync(istek.Headers.Authorization?.Parameter, OturumSonuNedeni.SifreDegisti);
    }
    public Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string mevcutSifre)
        => GonderJsonAsync<KurtarmaKoduDto>(HttpMethod.Post, "api/auth/kurtarma-kodu", new { mevcutSifre });
    public async Task SifreKurtarAsync(SifreKurtarYaz g)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/kurtar") { Content = System.Net.Http.Json.JsonContent.Create(g, options: Json) };
        using var yanit = await GonderAsync(istek, tokenEkle: false);
        await CihazSaklaAsync("editor", BaslikBelirteci(yanit));
    }
    /// <summary>Gövdesiz (204) yanıttaki tanıdık cihaz belirteci: şifre değişikliği ve kurtarma bu cihaza yenisini verir.</summary>
    private static string? BaslikBelirteci(HttpResponseMessage yanit)
        => yanit.Headers.TryGetValues(TanidikCihazBasligi, out var degerler) ? degerler.FirstOrDefault() : null;
    public Task<SurumDto> SurumAsync() => GetAsync<SurumDto>("api/surum");
    public Task<YedekDurumuDto> YedekDurumuAsync() => GetAsync<YedekDurumuDto>("api/yedek/durum");
    /// <summary>Sunucu yedeği isteğin içinde hazırlar (SQLite yedeği, doğrulama, yedek aynası) ve belgeleri de içeren kendi kendine
    /// yeterli ZIP'i akıtır; hazırlık ve indirme birlikte <see cref="KasaZamanAsimlari.Yedek"/> süresine tabidir. Yedek belleğe alınmadan
    /// <paramref name="hedef"/>'e yazılır. Sunucunun yedek diskinde yer yoksa 507 ve Türkçe hata (<see cref="KasaApiException"/>).</summary>
    public Task<IndirmeBilgisi> YedekIndirAsync(Stream hedef, CancellationToken cancellationToken = default)
        => DosyaIndirAsync(HttpMethod.Post, "api/yedek", "kasa-yedek.zip", hedef, _zaman.Yedek, cancellationToken);
    public Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int alisId, bool silinenler = false)
        => GetAsync<IReadOnlyList<BelgeDto>>($"api/alis/{alisId}/belgeler" + (silinenler ? "?silinenler=true" : ""));
    /// <summary>Sunucunun belge sınırı (10 MB) istemcide de denetlenir: sınır dışı dosya yavaş bağlantıda boşuna gönderilmez.</summary>
    public async Task<BelgeDto> BelgeYukleAsync(int alisId, string dosyaAdi, string icerikTuru, byte[] icerik, int? odemeId = null, CancellationToken cancellationToken = default)
    {
        if (icerik.Length == 0) throw new ArgumentException("Belge dosyası boş.", nameof(icerik));
        if (icerik.Length > EnBuyukBelge) throw new ArgumentException("Belge en fazla 10 MB olabilir.", nameof(icerik));
        using var govde = new MultipartFormDataContent();
        var dosya = new ByteArrayContent(icerik);
        dosya.Headers.ContentType = new MediaTypeHeaderValue(icerikTuru);
        govde.Add(dosya, "dosya", GuvenliDosyaAdi(dosyaAdi, "belge"));
        if (odemeId is { } id) govde.Add(new StringContent(id.ToString(System.Globalization.CultureInfo.InvariantCulture)), "odemeId");
        using var istek = new HttpRequestMessage(HttpMethod.Post, $"api/alis/{alisId}/belgeler") { Content = govde };
        using var yanit = await GonderAsync(istek, zamanAsimi: _zaman.Yukleme, cancellationToken: cancellationToken);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<BelgeDto>(yanit.Content, Json, cancellationToken))!;
    }
    /// <summary>Belgenin adı ve uzantısı sunucunun bildirdiği türden kurulur (<see cref="DosyaTurleri.GuvenliAd"/>).</summary>
    public Task<IndirmeBilgisi> BelgeIndirAsync(int belgeId, Stream hedef, CancellationToken cancellationToken = default)
        => DosyaIndirAsync(HttpMethod.Get, $"api/belgeler/{belgeId}", $"belge-{belgeId}", hedef, _zaman.Indirme, cancellationToken);
    /// <summary>Gerekçe varsa JSON gövdede ({"gerekce"}) gider; gövdesiz silme eski sunucuyla da çalışır.</summary>
    public Task BelgeSilAsync(int belgeId, string? gerekce = null)
        => string.IsNullOrWhiteSpace(gerekce) ? SilAsync($"api/belgeler/{belgeId}") : GonderJsonAsync(HttpMethod.Delete, $"api/belgeler/{belgeId}", new { gerekce = gerekce.Trim() });
    public Task<IndirmeBilgisi> DisariAktarAsync(DateOnly baslangic, DateOnly bitis, string? kanal, string bicim, Stream hedef, CancellationToken cancellationToken = default)
        => DosyaIndirAsync(HttpMethod.Get, $"api/disari-aktar?baslangic={baslangic:yyyy-MM-dd}&bitis={bitis:yyyy-MM-dd}&bicim={Uri.EscapeDataString(bicim)}"
            + (string.IsNullOrWhiteSpace(kanal) ? "" : "&kanal=" + Uri.EscapeDataString(kanal)), $"kasa-rapor.{bicim}", hedef, _zaman.Indirme, cancellationToken);

    /// <summary>Sunucunun belge sınırı (10 MB).</summary>
    private const int EnBuyukBelge = 10 * 1024 * 1024;

    /// <summary>Dosya yanıtını başlıklar gelir gelmez akışla <paramref name="hedef"/>'e yazar; gövde belleğe toplanmaz.
    /// Süre sınırı gövdenin sonuna kadar geçerlidir, iptal okuma ve yazmaya yayılır.</summary>
    private Task<IndirmeBilgisi> DosyaIndirAsync(HttpMethod metot, string yol, string varsayilan, Stream hedef, TimeSpan zamanAsimi, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hedef);
        return SureliAsync(zamanAsimi, cancellationToken, async ct =>
        {
            using var istek = new HttpRequestMessage(metot, yol);
            using var yanit = await YanitAlAsync(istek, tokenEkle: true, HttpCompletionOption.ResponseHeadersRead, ct);
            var basliklar = yanit.Content.Headers;
            var ad = basliklar.ContentDisposition?.FileNameStar ?? basliklar.ContentDisposition?.FileName;
            await using var kaynak = await yanit.Content.ReadAsStreamAsync(ct);
            var tampon = new byte[81920];
            long toplam = 0;
            int okunan;
            while ((okunan = await kaynak.ReadAsync(tampon, ct)) > 0)
            {
                await hedef.WriteAsync(tampon.AsMemory(0, okunan), ct);
                toplam += okunan;
            }
            await hedef.FlushAsync(ct);
            // Ad sunucudan gelse de uzantı yalnız bildirilen içerik türünden kurulur; yön işaretleri ve yol parçaları atılır.
            var tur = basliklar.ContentType?.MediaType;
            return new IndirmeBilgisi(DosyaTurleri.GuvenliAd(ad, tur, varsayilan), tur ?? "application/octet-stream", toplam);
        });
    }
    /// <summary>Yüklenen dosyanın gönderilen adı; türü ve saklanan adı sunucu sihirli baytlardan belirler.</summary>
    private static string GuvenliDosyaAdi(string? ad, string varsayilan) => DosyaTurleri.GonderilecekAd(ad, varsayilan);
}
