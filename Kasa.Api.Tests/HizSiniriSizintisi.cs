using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kasa.Api.Tests;

/// <summary>
/// ASP.NET Core'un hız sınırı ara katmanı (RateLimitingMiddleware) uç politikaları için kurduğu PartitionedRateLimiter'ı hiç
/// Dispose etmez (dotnet/aspnetcore#66434, açık). Sınırlayıcının 100 ms'lik zamanlayıcı döngüsü süreç genelindeki TimerQueue'dan
/// köklenir; bölümleyici ara katmanı, ara katmanın _next zinciri de bütün uygulamayı (kök servis sağlayıcı, bellek içi
/// veritabanı bağlantısı, uç tablosu) tutar. Üretimde süreç başına tek uygulama olduğundan etkisizdir; testte ise kapatılan
/// her fabrika bellekte kalıyordu (fabrika başına ≈2–3 MB, tam koşuda ≈1.200 fabrika): yığın büyüdükçe çöp toplayıcı bütün
/// testleri giderek yavaşlatıyordu. Bu sınıf fabrika kapanırken ara katmanı istek hattında bulur ve sınırlayıcılarını kapatır.
/// </summary>
internal static class HizSiniriSizintisi
{
    private const string AraKatman = "Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware";
    private const BindingFlags Alanlar = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Sunucunun istek hattındaki hız sınırı ara katmanlarının sınırlayıcıları. Hat, sunucudan birkaç adım
    /// (TestServer → uygulama sarmalayıcısı → HostingApplication) sonra yalnız RequestDelegate zinciridir: ara katman ya da
    /// kapanış nesnesi → temsilci türündeki alan (_next, next) → sonraki temsilcinin hedefi. İlk adımlardan sonra yalnız bu
    /// zincir izlenir; servis sağlayıcıya inilmez.</summary>
    public static List<IDisposable> Sinirlayicilar(IServer sunucu)
    {
        var bulunan = new List<IDisposable>();
        var gorulen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var kuyruk = new Queue<(object Nesne, int Derinlik)>();
        kuyruk.Enqueue((sunucu, 0));
        while (kuyruk.TryDequeue(out var siradaki))
        {
            var (nesne, derinlik) = siradaki;
            if (derinlik > 200 || nesne is IServiceProvider || !gorulen.Add(nesne))
                continue;
            if (nesne is Delegate temsilci)
            {
                foreach (var tek in temsilci.GetInvocationList())
                    if (tek.Target is { } hedef)
                        kuyruk.Enqueue((hedef, derinlik + 1));
                continue;
            }
            var tur = nesne.GetType();
            if (tur.FullName == AraKatman)
            {
                foreach (var ad in (string[])["_endpointLimiter", "_globalLimiter"])
                    if (tur.GetField(ad, Alanlar)?.GetValue(nesne) is IDisposable sinirlayici)
                        bulunan.Add(sinirlayici);
                continue;
            }
            for (var t = tur; t is not null && t != typeof(object); t = t.BaseType)
                foreach (var alan in t.GetFields(Alanlar))
                    if ((derinlik < 3 ? !alan.FieldType.IsValueType && alan.FieldType != typeof(string) : typeof(Delegate).IsAssignableFrom(alan.FieldType))
                        && alan.GetValue(nesne) is { } deger)
                        kuyruk.Enqueue((deger, derinlik + 1));
        }
        return bulunan;
    }
}

/// <summary>Kapanınca hız sınırı ara katmanının sızdırdığı sınırlayıcıları da kapatan fabrika (<see cref="HizSiniriSizintisi"/>).
/// Sınırlayıcılar sunucu açılırken (istek hattı kurulmuş olarak) toplanır; WithWebHostBuilder'la türetilen fabrikaların
/// sunucuları da bu yoldan açılır ve fabrika kapanınca birlikte kapatılır.</summary>
public abstract class SizdirmayanFabrika<TGiris> : WebApplicationFactory<TGiris> where TGiris : class
{
    private readonly List<IDisposable> _sinirlayicilar = [];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        var bulunan = HizSiniriSizintisi.Sinirlayicilar(host.Services.GetRequiredService<IServer>());
        lock (_sinirlayicilar)
            _sinirlayicilar.AddRange(bulunan);
        return host;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        IDisposable[] kapatilacak;
        lock (_sinirlayicilar)
        {
            kapatilacak = [.. _sinirlayicilar];
            _sinirlayicilar.Clear();
        }
        foreach (var sinirlayici in kapatilacak)
            sinirlayici.Dispose();
    }
}
