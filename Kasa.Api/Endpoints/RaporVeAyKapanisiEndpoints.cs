using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket B (raporlar ve ay kapanışı) kurulumu: Program.cs'ye yalnız iki satır eklenir —
/// servisler için <see cref="AddRaporVeAyKapanisi"/>, uç noktalar için <see cref="MapRaporVeAyKapanisi"/>.
/// Tüm uç noktalar korumalı /api grubundadır (oturum zorunlu); yazmalar "Editor" politikasıyla.
/// </summary>
public static class RaporVeAyKapanisiKurulumu
{
    public static IServiceCollection AddRaporVeAyKapanisi(this IServiceCollection services)
    {
        services.AddScoped<RaporServisi>();
        services.AddHttpClient<TcmbKurServisi>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EmarKasa/1.0 (+https://kasa.emarglobal.com)");
        });
        return services;
    }

    public static RouteGroupBuilder MapRaporVeAyKapanisi(this RouteGroupBuilder api)
    {
        // Kilitli aya yazma (işlem, gelen, çek, …) hangi uç noktadan gelirse gelsin anlaşılır bir 409 olur.
        // "kilitli: true" bu 409'u öteki çakışmalardan ayırır: istemci (ör. korumalı gelen yazımı) bunu
        // "siz açtıktan sonra değişmiş" sorusuna çevirmez, mesajı olduğu gibi gösterir.
        api.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (AyKilitliHatasi ex) { return Results.Conflict(new { hata = ex.Message, kilitli = true }); }
        });
        api.MapKasaDokumu();
        api.MapAyKapanisi();
        api.MapAylikYazdir();
        api.MapKurlarVeGrafik();
        api.MapHedefButce();
        api.MapCariOzeti();
        api.MapDisaAktarmaEk();
        return api;
    }
}
