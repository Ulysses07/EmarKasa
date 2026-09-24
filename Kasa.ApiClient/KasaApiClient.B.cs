using System.Globalization;

namespace Kasa.ApiClient;

/// <summary>Paket B uç noktaları (raporlar ve ay kapanışı).</summary>
public sealed partial class KasaApiClient
{
    private static string T(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string AySorgusu(int yil, int ay) => FormattableString.Invariant($"yil={yil}&ay={ay}");
    private static string AyEki(int yil, int ay) => FormattableString.Invariant($"{yil:D4}-{ay:D2}");

    public Task<KasaDokumuDto> KasaDokumuAsync(DateOnly baslangic, DateOnly bitis)
        => GetAsync<KasaDokumuDto>($"api/rapor/kasa-dokumu?baslangic={T(baslangic)}&bitis={T(bitis)}");
    public Task<IndirilenDosya> KasaDokumuCsvAsync(DateOnly baslangic, DateOnly bitis)
        => IndirAsync($"api/disaaktar/kasa-dokumu.csv?baslangic={T(baslangic)}&bitis={T(bitis)}", $"kasa-dokumu-{T(baslangic)}_{T(bitis)}.csv");

    public Task<AyKapanisDto> AyKapanisiAsync(int yil, int ay) => GetAsync<AyKapanisDto>($"api/ay-kapanisi?{AySorgusu(yil, ay)}");
    public Task<IReadOnlyList<AyKilidiDto>> AyKilitleriAsync() => GetAsync<IReadOnlyList<AyKilidiDto>>("api/ay-kapanisi/kilitler");
    public Task<AyKapanisDto> AyiKilitleAsync(int yil, int ay) => GonderJsonAsync<AyKapanisDto>(HttpMethod.Post, "api/ay-kapanisi/kilitle", new { yil, ay });
    public Task<AyKapanisDto> AyKilidiniAcAsync(int yil, int ay) => GonderJsonAsync<AyKapanisDto>(HttpMethod.Post, "api/ay-kapanisi/kilit-ac", new { yil, ay });
    public Task<AyKapanisDto> AyiYayinlaAsync(int yil, int ay) => GonderJsonAsync<AyKapanisDto>(HttpMethod.Post, "api/ay-kapanisi/yayinla", new { yil, ay });

    public Task<IndirilenDosya> AylikYazdirAsync(int yil, int ay)
        => IndirAsync($"api/rapor/aylik-yazdir?{AySorgusu(yil, ay)}", $"kasa-aylik-rapor-{AyEki(yil, ay)}.html");
    public Task<IndirilenDosya> AyPaketiAsync(int yil, int ay)
        => IndirAsync($"api/disaaktar/ay-paketi.zip?{AySorgusu(yil, ay)}", $"kasa-ay-paketi-{AyEki(yil, ay)}.zip");

    public Task<IndirilenDosya> CeklerCsvAsync(CekYonu? yon = null, CekDurumu? durum = null, DateOnly? baslangic = null, DateOnly? bitis = null)
    {
        var q = new List<string>();
        if (yon is { } y) q.Add($"yon={y}");
        if (durum is { } d) q.Add($"durum={d}");
        if (baslangic is { } b) q.Add($"baslangic={T(b)}");
        if (bitis is { } s) q.Add($"bitis={T(s)}");
        return IndirAsync(q.Count > 0 ? $"api/disaaktar/cekler.csv?{string.Join("&", q)}" : "api/disaaktar/cekler.csv", "kasa-cekler.csv");
    }
    public Task<IndirilenDosya> KasaSayimlariCsvAsync() => IndirAsync("api/disaaktar/kasasayimlari.csv", "kasa-sayimlari.csv");
    public Task<IndirilenDosya> GecmisCsvAsync(string? tur = null)
        => IndirAsync(string.IsNullOrWhiteSpace(tur) ? "api/disaaktar/gecmis.csv" : $"api/disaaktar/gecmis.csv?tur={Uri.EscapeDataString(tur)}",
            "kasa-gecmis.csv");

    public Task<IReadOnlyList<KurDto>> KurlarAsync() => GetAsync<IReadOnlyList<KurDto>>("api/kurlar");
    public Task<KurDto> KurKaydetAsync(KurDto g) => GonderJsonAsync<KurDto>(HttpMethod.Put, "api/kurlar", g);
    public Task<KurTcmbSonucDto> KurTcmbDoldurAsync(DateOnly ay) => GonderJsonAsync<KurTcmbSonucDto>(HttpMethod.Post, "api/kurlar/tcmb", new { ay });
    public Task<GrafikDto> GrafikAsync(int yil, int ay) => GetAsync<GrafikDto>($"api/rapor/grafik?{AySorgusu(yil, ay)}");

    public Task<HedefButceDto> HedefButceAsync(int yil, int ay) => GetAsync<HedefButceDto>($"api/hedef-butce?{AySorgusu(yil, ay)}");
    public Task<HedefButceDto> HedefButceKaydetAsync(HedefButceYaz g) => GonderJsonAsync<HedefButceDto>(HttpMethod.Put, "api/hedef-butce", g);
    public Task<KopyalaSonucDto> HedefButceKopyalaAsync(DateOnly ay) => GonderJsonAsync<KopyalaSonucDto>(HttpMethod.Post, "api/hedef-butce/kopyala", new { ay });

    public Task<CariOzetiDto> CariOzetiAsync(string ad, int yil, CariOzetiTuru tur)
        => GetAsync<CariOzetiDto>(FormattableString.Invariant(
            $"api/rapor/cari-ozeti?ad={Uri.EscapeDataString(ad)}&yil={yil}&tur={(tur == CariOzetiTuru.Kalem ? "kalem" : "cari")}"));
}
