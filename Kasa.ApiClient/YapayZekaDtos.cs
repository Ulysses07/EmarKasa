using System.Net.Http.Json;

namespace Kasa.ApiClient;

/// <summary>Yapay zekâ önerisinin güveni (YapayZekaGuvenleri), kategorisi ve düzenlenmiş açıklaması.</summary>
public record EkstreYapayZekaOnerisi(string Guven, string Kategori, string? Aciklama);
/// <summary>Yapay zekânın durumu: açık mı (değilse nedeni), ayın harcaması ve sınırı, önerisi sonuçlanan satırlardan türü aynı kaydedilenler, son ölçümler.</summary>
public record YapayZekaDurumDto(bool Acik, string? KapaliNedeni, string Model, decimal AyHarcamasiUsd, decimal AylikSinirUsd, int AyCagri, int Sonuclanan, int AyniTur,
    IReadOnlyList<YapayZekaOlcumDto> Olcumler);
/// <summary>Belgeden gönderilecek maskeli satırlar (gönderilmeden); <paramref name="KalanSatir"/> sonraki çağrıya kalan.</summary>
public record YapayZekaOnizlemeDto(int SatirSayisi, int KalanSatir, int OrnekSayisi, IReadOnlyList<string> Satirlar, decimal EnYuksekMaliyetUsd);
public record YapayZekaSorYaz(Guid IstekId);
public record YapayZekaSonucDto(int Sorulan, int Onerilen, int KalanSatir, decimal MaliyetUsd, string? Not);
public record YapayZekaOlcumYaz(Guid IstekId, string Kaynak);
public record YapayZekaOlcumDto(string Kaynak, int Satir, int TurDogru, int DagilimSatiri, int DagilimDogru, int YuksekGuven, int YuksekDogru, int Onerisiz, decimal MaliyetUsd, DateTimeOffset Zaman);

public sealed partial class KasaApiClient
{
    public Task<YapayZekaDurumDto> YapayZekaDurumuAsync() => GetAsync<YapayZekaDurumDto>("api/ekstre-aktar/yapay-zeka");
    public Task<YapayZekaOnizlemeDto> YapayZekaOnizlemeAsync(int id) => GetAsync<YapayZekaOnizlemeDto>($"api/ekstre-aktar/{id}/yapay-zeka");
    public Task<YapayZekaSonucDto> YapayZekaSorAsync(int id, YapayZekaSorYaz g) => UzunGonderAsync<YapayZekaSonucDto>($"api/ekstre-aktar/{id}/yapay-zeka", g);
    public Task<YapayZekaOlcumDto> YapayZekaOlcAsync(YapayZekaOlcumYaz g) => UzunGonderAsync<YapayZekaOlcumDto>("api/ekstre-aktar/yapay-zeka/olcum", g);

    /// <summary>Sunucunun dış hizmeti beklediği istek: yükleme ile aynı süre sınırı.</summary>
    private async Task<T> UzunGonderAsync<T>(string yol, object govde)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, yol) { Content = JsonContent.Create(govde, govde.GetType(), options: Json) };
        using var yanit = await GonderAsync(istek, zamanAsimi: _zaman.Yukleme);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
