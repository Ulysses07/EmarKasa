using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Kasa.Api.Tests;

internal static class IslemTestSilme
{
    /// <summary>Silme senaryolarında kaydın o anda görülen sürümünü istemci gibi gönderir.</summary>
    internal static async Task<HttpResponseMessage> SilIslemAsync(this HttpClient client, int id, CancellationToken ct)
    {
        var liste = await client.GetFromJsonAsync<JsonArray>("/api/islemler", cancellationToken: ct);
        var satir = liste?.SingleOrDefault(i => i?["id"]?.GetValue<int>() == id);
        var surum = satir?["surum"]?.GetValue<int>() ?? 0;
        return await client.DeleteAsync($"/api/islemler/{id}?surum={surum}", ct);
    }
}
