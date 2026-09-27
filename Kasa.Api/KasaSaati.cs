using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api;

/// <summary>
/// Kasanın tek saat kaynağı: şimdiki an ve İstanbul'a göre "bugün". Saat, uygulamanın DI'daki
/// <see cref="TimeProvider"/>'ıdır; kayıt yoksa <see cref="TimeProvider.System"/>. Üretimde ikisi aynıdır.
/// Parametresiz <see cref="Bugun"/> istek boyunca o isteği işleyen uygulamanın saatini okur: değer
/// yalnız isteğin kendi async akışında (AsyncLocal) durur, küresel ve değiştirilebilir bir saat yoktur.
/// Böylece aynı süreçte farklı saatle çalışan iki uygulama (paralel test fabrikaları) birbirini etkilemez.
/// </summary>
public static class KasaSaati
{
    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly AsyncLocal<TimeProvider?> IstekSaati = new();

    /// <summary>Geçerli isteği işleyen uygulamanın saati; istek dışında sistem saati.</summary>
    public static TimeProvider Gecerli => IstekSaati.Value ?? TimeProvider.System;
    public static DateTimeOffset Simdi => Gecerli.GetUtcNow();
    public static DateOnly Bugun => Gecerli.IstanbulBugun();

    public static DateOnly IstanbulBugun(this TimeProvider saat) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(saat.GetUtcNow(), Istanbul).DateTime);

    /// <summary>Bağlamın ait olduğu uygulamanın saati. İstek dışında (arka plan işi, doğrudan servis
    /// çağrısı) da doğru saati verir; uygulama servisi olmayan bağlamda <see cref="Gecerli"/>'ye düşer.</summary>
    public static TimeProvider Saati(this KasaDbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?.GetService<TimeProvider>() ?? Gecerli;
    public static DateOnly Bugunu(this KasaDbContext db) => db.Saati().IstanbulBugun();

    /// <summary>Parametresiz <see cref="Bugun"/>'ün istek boyunca DI saatini okumasını sağlar. Kayıt
    /// olmadan da <see cref="Bugun"/> sistem saatini verir; üretimde sonuç aynıdır.</summary>
    public static IServiceCollection AddKasaSaati(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, IstekSaatiFiltresi>());
        return services;
    }

    private sealed class IstekSaatiFiltresi : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var saat = app.ApplicationServices.GetService<TimeProvider>();
            // async yöntem içinde atanan AsyncLocal yalnız bu isteğin akışına iner; çağırana sızmaz.
            app.Use(async (http, devam) => { IstekSaati.Value = saat; await devam(http); });
            next(app);
        };
    }
}
