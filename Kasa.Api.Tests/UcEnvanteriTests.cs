using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Uç envanteri: çalışan uygulamanın <see cref="EndpointDataSource"/> dökümü altın dosyayla (Altin/uc-envanteri.txt) birebir
/// karşılaştırılır. Döküm uçları veri kaynağındaki sırayla yazar; her uç için türü, rota deseni, Order'ı, görünen adı, HTTP
/// yöntemleri, adı, yetki verileri (politika, rol, şema; anonim izni), hız sınırı politikası ve meta veri öğelerini
/// (sırasıyla, türü ve içeriğiyle: kabul edilen gövde, yanıt türleri, parametre bağlama, gövde boyu sınırı...) yazar.
/// Yalnız kaynak dosyadaki yere ya da derleme yapılandırmasına bağlı derleyici ayrıntıları yazılmaz:
/// - Satır içi işleyicinin <see cref="MethodInfo"/>'sunun derleyicinin ürettiği adı ve bildiren türü (ör. Program+&lt;&gt;c);
///   dökümde imzası yer alır: dönüş türü, parametre türleri ve adları, her birinin etkin null durumu.
/// - İşleyici yöntemindeki NullableContextAttribute: derleyici null ek açıklamalarını sıkıştırırken bağlamı yönteme ya da
///   işleyicileri taşıyan, derleyicinin ürettiği sınıfa koyar; hangisine koyacağı aynı sınıftaki komşu işleyicilere bağlıdır.
///   Taşıdığı bilgi imzadaki etkin null durumudur (<see cref="NullabilityInfoContext"/>: ? null olabilir, ~ bilinmiyor).
/// - DebuggerStepThroughAttribute: derleyici async işleyicilere yalnız Debug derlemesinde ekler (CI Release derler).
/// Uç filtreleri (AddEndpointFilter) meta veri değildir; davranış testleri kapsar.
/// Uçları dosyalar arasında taşımak dökümü değiştirmez. Uç eklemek, kaldırmak ya da meta verisini değiştirmek bilinçli bir
/// sözleşme değişikliğidir: KASA_UC_ENVANTERI_YAZ=1 ile dosya yeniden yazılır ve fark incelenir.
/// </summary>
public class UcEnvanteriTests
{
    [Fact]
    public void Uc_envanteri_altin_dokumle_birebir_ayni()
    {
        using var f = new KasaWebFactory();
        var gercek = Dokum(f.Services.GetRequiredService<EndpointDataSource>());
        // Döküm belirlenimcidir: aynı uygulamanın ikinci dökümü aynıdır.
        Assert.Equal(gercek, Dokum(f.Services.GetRequiredService<EndpointDataSource>()));

        var yol = AltinDosyasi();
        if (Environment.GetEnvironmentVariable("KASA_UC_ENVANTERI_YAZ") == "1" || !File.Exists(yol))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            File.WriteAllText(yol, gercek);
            Assert.Fail($"Uç envanteri yazıldı: {yol}. Farkı inceleyip testi yeniden çalıştırın.");
        }
        var beklenen = File.ReadAllText(yol).ReplaceLineEndings("\n").Split('\n');
        var satirlar = gercek.Split('\n');
        for (var i = 0; i < Math.Max(beklenen.Length, satirlar.Length); i++)
        {
            var b = i < beklenen.Length ? beklenen[i] : "(dosya sonu)";
            var g = i < satirlar.Length ? satirlar[i] : "(döküm sonu)";
            Assert.True(b == g, $"Uç envanteri {i + 1}. satırda altın dosyadan farklı:\nBEKLENEN {b}\nGERÇEK   {g}");
        }
    }

    /// <summary>Uçların veri kaynağındaki sırayla, satır sonu LF olan metin dökümü.</summary>
    internal static string Dokum(EndpointDataSource kaynak)
    {
        var s = new StringBuilder();
        var uclar = kaynak.Endpoints;
        s.Append(CultureInfo.InvariantCulture, $"uç sayısı: {uclar.Count}\n");
        for (var i = 0; i < uclar.Count; i++)
        {
            var uc = uclar[i];
            var rota = uc as RouteEndpoint;
            s.Append(CultureInfo.InvariantCulture, $"\n#{i + 1} {uc.GetType().FullName} {rota?.RoutePattern.RawText ?? "-"} order={rota?.Order.ToString(CultureInfo.InvariantCulture) ?? "-"}\n");
            s.Append(CultureInfo.InvariantCulture, $"  görünen ad: {uc.DisplayName ?? "-"}\n");
            var m = uc.Metadata;
            s.Append(CultureInfo.InvariantCulture, $"  yöntem: {(m.GetMetadata<IHttpMethodMetadata>() is { } y ? string.Join(",", y.HttpMethods) : "-")}\n");
            s.Append(CultureInfo.InvariantCulture, $"  ad: {m.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "-"} / rota adı: {m.GetMetadata<IRouteNameMetadata>()?.RouteName ?? "-"}\n");
            var yetkiler = m.GetOrderedMetadata<IAuthorizeData>();
            s.Append(CultureInfo.InvariantCulture, $"  yetki: {(yetkiler.Count == 0 ? "-" : string.Join(" + ", yetkiler.Select(Yetki)))}{(m.GetMetadata<IAllowAnonymous>() is not null ? " (anonim izinli)" : "")}\n");
            s.Append(CultureInfo.InvariantCulture, $"  hız sınırı: {m.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "-"}{(m.GetMetadata<DisableRateLimitingAttribute>() is not null ? " (kapalı)" : "")}\n");
            var ogeler = m.Where(o => o.GetType().FullName is not ("System.Runtime.CompilerServices.NullableContextAttribute"
                // Derleyici async işleyicilere yalnız Debug derlemesinde ekler; CI Release derler.
                or "System.Diagnostics.DebuggerStepThroughAttribute")).ToList();
            s.Append(CultureInfo.InvariantCulture, $"  meta veri ({ogeler.Count}):\n");
            foreach (var oge in ogeler)
                s.Append(CultureInfo.InvariantCulture, $"    - {Tur(oge.GetType())}{(Tanim(oge) is { Length: > 0 } t ? ": " + t : "")}\n");
        }
        return s.ToString();
    }

    private static string Yetki(IAuthorizeData a) =>
        $"politika={a.Policy ?? "(varsayılan)"} roller={a.Roles ?? "-"} şemalar={a.AuthenticationSchemes ?? "-"}";

    /// <summary>Meta veri öğesinin içeriği. Bilinen sözleşme türleri alan alan; diğerleri kendi ToString'iyle (geçersiz
    /// kılmamışsa yalnız türü yazılır).</summary>
    private static string Tanim(object oge) => (oge switch
    {
        MethodInfo mi => Imza(mi),
        IHttpMethodMetadata h => $"yöntemler={string.Join(",", h.HttpMethods)} cors={h.AcceptCorsPreflight}",
        IAuthorizeData a => Yetki(a),
        EnableRateLimitingAttribute r => $"politika={r.PolicyName ?? "-"}",
        IAcceptsMetadata k => $"istek={(k.RequestType is null ? "-" : Tur(k.RequestType))} içerik={string.Join(",", k.ContentTypes)} isteğe bağlı={k.IsOptional}",
        IProducesResponseTypeMetadata p => $"durum={p.StatusCode} tür={(p.Type is null ? "-" : Tur(p.Type))} içerik={string.Join(",", p.ContentTypes)}",
        IParameterBindingMetadata b => $"ad={b.Name} tür={Tur(b.ParameterInfo.ParameterType)} TryParse={b.HasTryParse} BindAsync={b.HasBindAsync} isteğe bağlı={b.IsOptional}",
        IRequestSizeLimitMetadata l => $"azami gövde={l.MaxRequestBodySize?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
        IEndpointNameMetadata n => n.EndpointName,
        IRouteNameMetadata n => n.RouteName ?? "-",
        _ => oge.ToString() is { } metin && metin != oge.GetType().ToString() ? metin : "",
    }).ReplaceLineEndings(" ");

    /// <summary>İşleyicinin imzası: dönüş türü, parametre türleri ve adları, etkin null durumlarıyla.</summary>
    private static string Imza(MethodInfo mi)
    {
        var baglam = new NullabilityInfoContext();
        return $"imza {Tur(baglam.Create(mi.ReturnParameter))} ({string.Join(", ", mi.GetParameters().Select(p => $"{Tur(baglam.Create(p))} {p.Name}"))})";
    }

    /// <summary>Tür adı, başvuru türlerinde (genel tür bağımsız değişkenleri dahil) etkin null durumuyla: ? null olabilir,
    /// ~ bilinmiyor, işaretsiz null olamaz. Nullable&lt;T&gt; türün kendisinde görünür.</summary>
    private static string Tur(NullabilityInfo n)
    {
        var t = n.Type;
        if (Nullable.GetUnderlyingType(t) is not null)
            return Tur(t);
        var ad = t.IsArray && n.ElementType is { } eleman ? Tur(eleman) + "[]"
            : t.IsGenericType && n.GenericTypeArguments.Length == t.GetGenericArguments().Length
                ? $"{GenelAd(t)}<{string.Join(", ", n.GenericTypeArguments.Select(Tur))}>"
                : Tur(t);
        return t.IsValueType ? ad : ad + n.ReadState switch
        {
            NullabilityState.Nullable => "?",
            NullabilityState.NotNull => "",
            _ => "~",
        };
    }

    /// <summary>Tür adı: ad alanı + ad; genel türler bütünleşik adsız (sürüm bilgisi taşımaz), iç içe türler '+' ile.</summary>
    private static string Tur(Type t)
    {
        if (t.IsArray)
            return Tur(t.GetElementType()!) + "[]";
        if (!t.IsGenericType)
            return t.FullName ?? t.Name;
        return $"{GenelAd(t)}<{string.Join(", ", t.GetGenericArguments().Select(Tur))}>";
    }

    private static string GenelAd(Type t)
    {
        var ad = t.GetGenericTypeDefinition().FullName ?? t.Name;
        return ad[..ad.IndexOf('`')];
    }

    private static string AltinDosyasi([CallerFilePath] string kaynak = "") =>
        Path.Combine(Path.GetDirectoryName(kaynak)!, "Altin", "uc-envanteri.txt");
}
