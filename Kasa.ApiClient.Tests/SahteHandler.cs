using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>Bütün istekleri sırasıyla kaydeden ve kuyruktaki canned yanıtları sırayla döndüren test handler'ı (tests-8).
/// Kuyruk boşken gelen istek testin kurmadığı bir istektir: 200 uydurulmaz, <see cref="InvalidOperationException"/> ile
/// test kırılır. Beklenmeyen ek istek ya da eksik yanıt böylece görünür; istek sayısı <see cref="Istekler"/> ile doğrulanır.</summary>
public sealed class SahteHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _yanitlar = new();
    private readonly List<(HttpRequestMessage Istek, string? Govde)> _istekler = new();
    /// <summary>Gelen bütün istekler ve gövdeleri, geliş sırasıyla.</summary>
    public IReadOnlyList<(HttpRequestMessage Istek, string? Govde)> Istekler => _istekler;
    public HttpRequestMessage? SonIstek => _istekler.Count > 0 ? _istekler[^1].Istek : null;
    public string? SonGovde => _istekler.Count > 0 ? _istekler[^1].Govde : null;
    /// <summary>Henüz kullanılmamış kurgulanmış yanıt sayısı.</summary>
    public int KalanYanit => _yanitlar.Count;

    public SahteHandler Kuyrukla(HttpStatusCode kod, string? json = null)
    {
        var resp = new HttpResponseMessage(kod);
        if (json is not null) resp.Content = new StringContent(json, Encoding.UTF8, "application/json");
        _yanitlar.Enqueue(resp);
        return this;
    }

    /// <summary>Başlık ya da içerik türü gibi ayrıntıları test kuran hazır yanıt.</summary>
    public SahteHandler Kuyrukla(HttpResponseMessage yanit)
    {
        _yanitlar.Enqueue(yanit);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _istekler.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        return _yanitlar.Count > 0 ? _yanitlar.Dequeue() : throw new InvalidOperationException($"Beklenmeyen istek: {request.Method} {request.RequestUri}");
    }
}
