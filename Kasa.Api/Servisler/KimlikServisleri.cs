using Kasa.Api.Auth;

namespace Kasa.Api.Servisler;

public static class KimlikServisleri
{
    /// <summary>Paket E servisleri: kullanıcı önbelleği, oturum izleyici, iki adımlı kurulum, bakım, soru yetkisi.</summary>
    public static IServiceCollection AddKimlikVeGuvenlik(this IServiceCollection services)
    {
        services.AddSingleton<KullaniciOnbellegi>();
        services.AddSingleton<OturumIzleyici>();
        services.AddSingleton<IkiAdimKurulumlari>();
        services.AddHostedService<GuvenlikBakimServisi>();
        // Soru yazmak: iki rol de (izleyicinin yazabildiği tek şey).
        services.AddAuthorizationBuilder()
            .AddPolicy(SoruEndpoints.SoruPolitikasi, p => p.RequireRole(Data.Roller.Editor, Data.Roller.Izleyici));
        return services;
    }
}
