using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Kasa.Api.YapayZeka;

/// <summary>Modele giden istek: sabit yönerge, kanal ve örnekler (önbelleğe alınır), soru ve yanıtın JSON şeması.</summary>
public sealed record YapayZekaIstegi(string Yonerge, string Baglam, string Soru, string Sema)
{
    public int Karakter => Yonerge.Length + Baglam.Length + Soru.Length + Sema.Length;
}

/// <summary>Modelin yanıtı. <paramref name="Metin"/> null: ret ya da yarım kaldı (<paramref name="Neden"/>); jetonlar yine faturalanır.</summary>
public sealed record YapayZekaYaniti(string? Metin, string? Neden, long GirdiJetonu, long CiktiJetonu, long OnbellekYazma, long OnbellekOkuma);

/// <summary>Çağrı yapılamadı. <paramref name="sonucBilinmiyor"/>: istek gitmiş olabilir, ayrılan maliyet harcanmış sayılır.</summary>
public sealed class YapayZekaHatasi(string ileti, bool sonucBilinmiyor, Exception? neden = null) : Exception(ileti, neden)
{
    public bool SonucBilinmiyor { get; } = sonucBilinmiyor;
}

public interface IYapayZekaIstemcisi
{
    Task<YapayZekaYaniti> SorAsync(YapayZekaIstegi istek, CancellationToken ct);
}

/// <summary>Resmî Anthropic SDK'sı ile çağrı (Messages API, yapılandırılmış çıktı). Anahtar ve metin günlüğe yazılmaz.</summary>
public sealed class AnthropicIstemcisi(YapayZekaAyarlari ayarlar, IHttpClientFactory http) : IYapayZekaIstemcisi
{
    /// <summary>Ters vekilin 60 saniyelik sınırının altında kalır; tek deneme yapılır (tekrar ücret doğurabilir).</summary>
    public static readonly TimeSpan Sure = TimeSpan.FromSeconds(45);
    public const string HttpAdi = "yapay-zeka", Adres = "https://api.anthropic.com";

    public async Task<YapayZekaYaniti> SorAsync(YapayZekaIstegi istek, CancellationToken ct)
    {
        var istemci = new AnthropicClient { ApiKey = ayarlar.Anahtar, BaseUrl = Adres, HttpClient = http.CreateClient(HttpAdi), MaxRetries = 0, Timeout = Sure };
        try
        {
            var m = await istemci.Messages.Create(new MessageCreateParams
            {
                Model = YapayZekaAyarlari.Model,
                MaxTokens = YapayZekaAyarlari.EnCokCiktiJetonu,
                System = new List<TextBlockParam> { new() { Text = istek.Yonerge + "\n\n" + istek.Baglam, CacheControl = new CacheControlEphemeral() } },
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Low,
                    Format = new JsonOutputFormat { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(istek.Sema)! },
                },
                Messages = [new UserMessageParam { Content = istek.Soru }],
            }, ct);
            var metin = string.Concat(m.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            var neden = m.StopReason == StopReason.EndTurn ? null
                : m.StopReason == StopReason.Refusal ? "Model bu satırlara yanıt vermedi."
                : m.StopReason == StopReason.MaxTokens ? "Yanıt yarım kaldı." : "Yanıt beklenmeyen biçimde bitti.";
            return new(neden is null ? metin : null, neden, m.Usage.InputTokens, m.Usage.OutputTokens, m.Usage.CacheCreationInputTokens ?? 0, m.Usage.CacheReadInputTokens ?? 0);
        }
        catch (AnthropicUnauthorizedException e) { throw new YapayZekaHatasi("Yapay zekâ anahtarı geçersiz; sunucudaki anahtarı yenileyin.", false, e); }
        catch (AnthropicForbiddenException e) { throw new YapayZekaHatasi("Yapay zekâ anahtarının bu modele izni yok.", false, e); }
        catch (AnthropicNotFoundException e) { throw new YapayZekaHatasi($"Model bulunamadı ({YapayZekaAyarlari.Model}); sunucu güncellenmeli.", false, e); }
        catch (AnthropicRateLimitException e) { throw new YapayZekaHatasi("Yapay zekâ hizmetinin hız ya da harcama sınırına ulaşıldı; daha sonra yeniden deneyin.", false, e); }
        catch (Anthropic5xxException e) { throw new YapayZekaHatasi("Yapay zekâ hizmeti şu an yanıt vermiyor; daha sonra yeniden deneyin.", true, e); }
        catch (Anthropic4xxException e) { throw new YapayZekaHatasi("Yapay zekâ isteği kabul edilmedi; hesabın kredisi bitmiş olabilir.", false, e); }
        catch (Exception e) when (e is AnthropicException or OperationCanceledException or HttpRequestException)
        { throw new YapayZekaHatasi("Yapay zekâ hizmetine ulaşılamadı ya da yanıt zamanında gelmedi.", true, e); }
    }
}
