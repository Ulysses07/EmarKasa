using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>Son isteği kaydeden ve sıradaki canned yanıtı döndüren test handler'ı.</summary>
public sealed class SahteHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _yanitlar = new();
    public HttpRequestMessage? SonIstek { get; private set; }
    public string? SonGovde { get; private set; }

    public SahteHandler Kuyrukla(HttpStatusCode kod, string? json = null)
    {
        var resp = new HttpResponseMessage(kod);
        if (json is not null) resp.Content = new StringContent(json, Encoding.UTF8, "application/json");
        _yanitlar.Enqueue(resp);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        SonIstek = request;
        SonGovde = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return _yanitlar.Count > 0 ? _yanitlar.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
    }
}
