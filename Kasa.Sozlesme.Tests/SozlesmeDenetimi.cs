using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Kasa.Sozlesme.Tests;

/// <summary>İstemcinin aldığı bir yanıt: uç ("GET api/rapor/panel", sayılar {id} olur), durum ve JSON gövdesi.</summary>
public sealed record IstemciYaniti(string Uc, HttpStatusCode Durum, string? Json);

/// <summary>İstemci hattında yanıtları kaydeder; JSON gövde belleğe alınır, istemci onu yine okuyabilir.</summary>
public sealed class YanitKaydedici : DelegatingHandler
{
    private readonly ConcurrentQueue<IstemciYaniti> _kayitlar = new();
    public IReadOnlyList<IstemciYaniti> Kayitlar => _kayitlar.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
    {
        var yanit = await base.SendAsync(istek, ct);
        string? json = null;
        if (yanit.Content.Headers.ContentType?.MediaType is { } tur && tur.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            await yanit.Content.LoadIntoBufferAsync(ct);
            json = await yanit.Content.ReadAsStringAsync(ct);
        }
        _kayitlar.Enqueue(new(SozlesmeDenetimi.UcAdi(istek.Method.Method, istek.RequestUri!.AbsolutePath), yanit.StatusCode, json));
        return yanit;
    }
}

/// <summary>
/// Sunucu JSON'u ile istemci DTO'sunun alan alan karşılaştırılması. Varsayılan Web seçenekleri sunucuda olup istemcide
/// olmayan alanı sessizce atar, istemcide olup sunucuda olmayan alanı null/0 bırakır: iki yön de hata sayılır
/// (<see cref="SozlesmeIzinleri"/> gerekçeli istisnaları ve sunucunun yalnız doluyken yazdığı alanların yokluğu dışında).
/// Tarih "yyyy-MM-dd", an ISO 8601 ve dilim, para JSON sayısı (üslü gösterim yok) olmalı; enum metin olarak ve istemcinin
/// tanıdığı bir değer olmalı. Son olarak gövde, katı seçeneklerle (zorunlu kurucu parametreleri, null atanamaz alanlar)
/// istemci türüne çözülür.
/// </summary>
public static partial class SozlesmeDenetimi
{
    /// <summary>İstemcinin kendi seçenekleri (KasaApiClient.Json) ve katı doğrulamalar.</summary>
    public static readonly JsonSerializerOptions Kati = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
    /// <summary>Sunucunun bağlama seçenekleri (ConfigureHttpJsonOptions: Web + metin enum).</summary>
    private static readonly JsonSerializerOptions Sunucu = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string UcAdi(string yontem, string yol) => $"{yontem} {Sayi().Replace(yol.TrimStart('/'), "{id}")}";

    /// <summary>Yanıt gövdesi <paramref name="tur"/>'e birebir uymalı; hatalar okunur yollarıyla döner.</summary>
    /// <param name="karsilik">İstemci türünün sunucu karşılığı (varsayılan <see cref="SunucuKarsiliklari.Bul"/>): sunucunun
    /// yalnız doluyken yazdığı alanın yanıtta olmaması hata değildir.</param>
    public static List<string> YanitHatalari(Type tur, IstemciYaniti yanit, Func<Type, Type?>? karsilik = null)
    {
        var hatalar = new List<string>();
        if (yanit.Json is null) { hatalar.Add($"{yanit.Uc}: JSON gövde yok ({(int)yanit.Durum})."); return hatalar; }
        using var belge = JsonDocument.Parse(yanit.Json);
        Yuru(belge.RootElement, tur, "$", yanit.Uc, SozlesmeIzinleri.Yon.SunucuFazlasi, hatalar, karsilik: karsilik ?? SunucuKarsiliklari.Bul);
        try { JsonSerializer.Deserialize(yanit.Json, tur, Kati); }
        catch (JsonException e) { hatalar.Add($"{yanit.Uc}: katı çözme {tur.Name}: {e.Message}"); }
        return hatalar;
    }

    /// <summary>İstemcinin gönderdiği gövde, ucun bağladığı sunucu türünde karşılığı olmayan alan taşımamalı.</summary>
    public static List<string> IstekHatalari(SunucuIstegi istek)
    {
        var hatalar = new List<string>();
        if (istek.GovdeTuru is null || string.IsNullOrWhiteSpace(istek.Govde)) return hatalar;
        using var belge = JsonDocument.Parse(istek.Govde);
        Yuru(belge.RootElement, istek.GovdeTuru, "$", istek.Uc, SozlesmeIzinleri.Yon.IstekFazlasi, hatalar, sunucuTuru: true);
        return hatalar;
    }

    private static void Yuru(JsonElement el, Type tur, string yol, string uc, SozlesmeIzinleri.Yon fazlaYonu, List<string> hatalar, bool sunucuTuru = false,
        Func<Type, Type?>? karsilik = null)
    {
        tur = Nullable.GetUnderlyingType(tur) ?? tur;
        if (el.ValueKind == JsonValueKind.Null) return; // null atanabilirlik katı çözmede denetlenir
        if (tur == typeof(JsonElement) || tur == typeof(object)) return;
        if (tur == typeof(string)) { Bekle(el, JsonValueKind.String, tur, yol, uc, hatalar); return; }
        if (tur == typeof(bool)) { if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) hatalar.Add($"{uc} {yol}: bool beklenirdi, {el.ValueKind}."); return; }
        if (tur == typeof(decimal) || tur == typeof(double))
        {
            if (Bekle(el, JsonValueKind.Number, tur, yol, uc, hatalar) && !Ondalik().IsMatch(el.GetRawText()))
                hatalar.Add($"{uc} {yol}: para/ondalık düz sayı olmalı, '{el.GetRawText()}'.");
            return;
        }
        if (tur == typeof(int) || tur == typeof(long)) { if (Bekle(el, JsonValueKind.Number, tur, yol, uc, hatalar) && !el.TryGetInt64(out _)) hatalar.Add($"{uc} {yol}: tam sayı beklenirdi, '{el.GetRawText()}'."); return; }
        if (tur == typeof(DateOnly)) { if (Bekle(el, JsonValueKind.String, tur, yol, uc, hatalar) && !Gun().IsMatch(el.GetString()!)) hatalar.Add($"{uc} {yol}: tarih yyyy-MM-dd olmalı, '{el.GetString()}'."); return; }
        if (tur == typeof(DateTimeOffset)) { if (Bekle(el, JsonValueKind.String, tur, yol, uc, hatalar) && !An().IsMatch(el.GetString()!)) hatalar.Add($"{uc} {yol}: an ISO 8601 ve saat dilimli olmalı, '{el.GetString()}'."); return; }
        if (tur == typeof(Guid)) { if (Bekle(el, JsonValueKind.String, tur, yol, uc, hatalar) && !Guid.TryParse(el.GetString(), out _)) hatalar.Add($"{uc} {yol}: Guid beklenirdi."); return; }
        if (tur.IsEnum)
        {
            if (Bekle(el, JsonValueKind.String, tur, yol, uc, hatalar) && !Enum.GetNames(tur).Contains(el.GetString(), StringComparer.OrdinalIgnoreCase))
                hatalar.Add($"{uc} {yol}: '{el.GetString()}' {tur.Name} değerlerinden biri değil ({string.Join(",", Enum.GetNames(tur))}).");
            return;
        }
        if (Eleman(tur) is { } eleman)
        {
            if (!Bekle(el, JsonValueKind.Array, tur, yol, uc, hatalar)) return;
            var i = 0;
            foreach (var oge in el.EnumerateArray()) Yuru(oge, eleman, $"{yol}[{i++}]", uc, fazlaYonu, hatalar, sunucuTuru, karsilik);
            return;
        }
        if (!Bekle(el, JsonValueKind.Object, tur, yol, uc, hatalar)) return;
        var bilgi = (sunucuTuru ? Sunucu : Kati).GetTypeInfo(tur);
        if (bilgi.Kind != JsonTypeInfoKind.Object) return;
        // Yalnız JSON'dan doldurulabilen alanlar (kurucu parametresi ya da ayarlanabilir özellik): hesaplanan salt okunur
        // özellik gövdeden bağlanmaz, adı eşleşse de değeri kaybolur.
        var alanlar = bilgi.Properties.Where(p => p.Set is not null || p.AssociatedParameter is not null).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var gelen = el.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
        {
            if (alanlar.TryGetValue(p.Name, out var alan)) Yuru(p.Value, alan.PropertyType, $"{yol}.{p.Name}", uc, fazlaYonu, hatalar, sunucuTuru, karsilik);
            else if (!SozlesmeIzinleri.Izinli(fazlaYonu, tur, p.Name, uc))
                hatalar.Add(fazlaYonu == SozlesmeIzinleri.Yon.IstekFazlasi
                    ? $"{uc} {yol}.{p.Name}: istemci gönderiyor, sunucu türü {tur.Name} tanımıyor (sessizce kaybolur)."
                    : $"{uc} {yol}.{p.Name}: sunucu gönderiyor, istemci DTO'su {tur.Name} tanımıyor (sessizce atılır).");
        }
        if (fazlaYonu == SozlesmeIzinleri.Yon.IstekFazlasi) return; // istemcinin göndermediği isteğe bağlı alan sunucuda varsayılan kalır
        // Sunucunun yalnız doluyken yazdığı alan (JsonIgnore WhenWritingNull/WhenWritingDefault) yoksa değeri boştur:
        // istemcide varsayılanda kalması doğrudur. Alanın iki tarafta da tanımlı olduğunu statik karşılaştırma denetler.
        var sunucu = karsilik?.Invoke(tur);
        foreach (var alan in alanlar.Values.Where(a => !gelen.Contains(a.Name)))
            if (!SozlesmeIzinleri.Izinli(SozlesmeIzinleri.Yon.IstemciFazlasi, tur, alan.Name, uc) && !(sunucu is not null && SunucuKarsiliklari.KosulluMu(sunucu, alan.Name)))
                hatalar.Add($"{uc} {yol}.{alan.Name}: istemci DTO'su {tur.Name} bekliyor, sunucu göndermiyor (varsayılan değerde kalır).");
    }

    private static bool Bekle(JsonElement el, JsonValueKind tur, Type beklenen, string yol, string uc, List<string> hatalar)
    {
        if (el.ValueKind == tur) return true;
        hatalar.Add($"{uc} {yol}: {beklenen.Name} için {tur} beklenirdi, {el.ValueKind} geldi.");
        return false;
    }

    private static Type? Eleman(Type tur)
    {
        if (tur == typeof(string)) return null;
        if (tur.IsArray) return tur.GetElementType();
        if (!typeof(IEnumerable).IsAssignableFrom(tur) && !(tur.IsInterface && tur.IsGenericType)) return null;
        return (tur.IsInterface && tur.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? tur : tur.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            ?.GetGenericArguments()[0];
    }

    [GeneratedRegex(@"(?<=/)\d+(?=/|$)")]
    private static partial Regex Sayi();
    [GeneratedRegex(@"^-?\d+(\.\d+)?$")]
    private static partial Regex Ondalik();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex Gun();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex An();
}
