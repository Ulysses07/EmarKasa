using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api;

/// <summary>
/// Kasanın saati: şimdiki an ve İstanbul'a göre "bugün". Saat, uygulamanın DI'daki <see cref="TimeProvider"/>'ıdır;
/// Program.cs onu <see cref="AddKasaSaati"/> ile kaydeder (üretimde <see cref="TimeProvider.System"/>, testte fabrikanın saati).
/// Garantiler:
/// - <c>db.Saati()</c> / <c>db.Bugunu()</c> bağlamın ait olduğu uygulamanın DI saatini istek içinde de dışında da
///   (arka plan işi, doğrudan servis çağrısı) verir. Bağlamı olan servis kodu bunu, zaman damgası yazan uçlar ve
///   arka plan servisleri DI'dan aldıkları <see cref="TimeProvider"/>'ı kullanır.
/// - Parametresiz <see cref="Bugun"/> yalnız istek boyunca, o isteği işleyen uygulamanın DI saatini okur
///   (<see cref="AddKasaSaati"/> filtresi). İstek dışında sistem saatine düşer; bu yüzden istek dışında
///   çalışabilen kod onu kullanmaz, <c>db.Bugunu()</c> kullanır.
/// - İstek saati yalnız isteğin kendi async akışında (AsyncLocal) durur; küresel ve değiştirilebilir saat yoktur.
///   Aynı süreçte farklı saatle çalışan iki uygulama (paralel test fabrikaları) birbirini etkilemez.
/// - Her okuma saati yeniden okur; gece yarısına denk gelen iki okuma farklı gün verebilir. Tek bir hesap günü
///   bir kez okuyup taşır (ör. HesapServisi).
/// </summary>
public static class KasaSaati
{
    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly AsyncLocal<TimeProvider?> IstekSaati = new();

    /// <summary>Geçerli isteği işleyen uygulamanın saati; istek dışında sistem saati.</summary>
    public static TimeProvider Gecerli => IstekSaati.Value ?? TimeProvider.System;
    /// <summary>İstek boyunca uygulamanın, istek dışında sistemin İstanbul günü (bkz. sınıf açıklaması).</summary>
    public static DateOnly Bugun => Gecerli.IstanbulBugun();

    public static DateOnly IstanbulBugun(this TimeProvider saat) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(saat.GetUtcNow(), Istanbul).DateTime);

    /// <summary>Bağlamın ait olduğu uygulamanın saati. İstek dışında (arka plan işi, doğrudan servis
    /// çağrısı) da doğru saati verir; uygulama servisi olmayan bağlamda <see cref="Gecerli"/>'ye düşer.</summary>
    public static TimeProvider Saati(this KasaDbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?.GetService<TimeProvider>() ?? Gecerli;
    public static DateOnly Bugunu(this KasaDbContext db) => db.Saati().IstanbulBugun();

    /// <summary>DI'ya sistem saatini (başka saat kayıtlı değilse) ve parametresiz <see cref="Bugun"/>'ün istek boyunca
    /// DI saatini okumasını sağlayan filtreyi ekler. Program.cs'de kayıtlıdır; tekrar çağrı etkisizdir (TryAdd).</summary>
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
