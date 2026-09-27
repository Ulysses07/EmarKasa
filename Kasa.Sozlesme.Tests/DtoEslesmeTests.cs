using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Elle iki kez tanımlanmış DTO'lar (contract-8): Kasa.ApiClient'taki her kayıt türü, Kasa.Api'de aynı adla tanımlıysa
/// JSON alanları (ad ve temel tür) birebir aynı olmalı. Çalışan sunucuya karşı testlerin ulaşamadığı dallarda da
/// (ör. iç içe denetim izi türleri) sapma derlemede yakalanır. Enum'lar metin olarak taşınır: adlar aynı olmalı.
/// </summary>
public class DtoEslesmeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    [Fact]
    public void GiderTipi_adlari_sunucu_ile_ayni()
        => Assert.Equal(Enum.GetNames<Kasa.Core.GiderTipi>(), Enum.GetNames<GiderTipi>());

    [Fact]
    public void Ayni_adli_istemci_ve_sunucu_turleri_ayni_json_alanlarini_tasir()
    {
        var sunucu = typeof(Kasa.Api.KartTakipDto).Assembly.GetTypes().Where(t => t.IsPublic && !t.IsGenericType).GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
        var ciftler = typeof(KasaApiClient).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && t.GetMethod("<Clone>$") is not null && sunucu.ContainsKey(t.Name))
            .Select(t => (Istemci: t, Sunucu: Assert.Single(sunucu[t.Name]))).ToList();
        Assert.True(ciftler.Count > 60, $"Eşlenen DTO çifti beklenenden az ({ciftler.Count}).");
        var farklar = ciftler.SelectMany(c => Farklar(c.Istemci, c.Sunucu)).ToList();
        Assert.True(farklar.Count == 0, "İstemci ve sunucu DTO farkları:\n" + string.Join("\n", farklar));
    }

    private static IEnumerable<string> Farklar(Type istemci, Type sunucu)
    {
        var a = Alanlar(istemci); var b = Alanlar(sunucu);
        foreach (var ad in a.Keys.Union(b.Keys).Order())
        {
            if (!b.TryGetValue(ad, out var sunucuTuru)) yield return $"{istemci.Name}.{ad}: yalnız istemcide.";
            else if (!a.TryGetValue(ad, out var istemciTuru)) yield return $"{istemci.Name}.{ad}: yalnız sunucuda.";
            else if (istemciTuru != sunucuTuru) yield return $"{istemci.Name}.{ad}: istemci {istemciTuru}, sunucu {sunucuTuru}.";
        }
    }

    private static Dictionary<string, string> Alanlar(Type tur) => Web.GetTypeInfo(tur).Properties
        .Where(p => p.Set is not null || p.AssociatedParameter is not null).ToDictionary(p => p.Name, p => Tur(p.PropertyType));

    /// <summary>JSON'daki biçimi: sayı, metin, tarih, an, mantıksal, enum, dizi ya da iç içe türün adı.</summary>
    private static string Tur(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return "metin";
        if (t == typeof(bool)) return "mantıksal";
        if (t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double)) return "sayı";
        if (t == typeof(DateOnly)) return "tarih";
        if (t == typeof(DateTimeOffset)) return "an";
        if (t == typeof(Guid)) return "guid";
        if (t.IsEnum) return "enum";
        if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t))
            return "dizi<" + Tur(t.IsArray ? t.GetElementType()! : t.GetGenericArguments()[0]) + ">";
        return t.Name.EndsWith("Dto") ? t.Name[..^3] : t.Name;
    }
}
