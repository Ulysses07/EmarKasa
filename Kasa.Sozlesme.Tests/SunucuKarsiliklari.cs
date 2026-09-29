using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// İstemci DTO'sunun sunucudaki karşılığı: sunucunun doğrudan yazdığı Kasa.Core türü (<see cref="Cekirdek"/>), liste
/// uçlarının yazdığı farklı adlı tür (<see cref="FarkliAdli"/>) ya da Kasa.Api'de aynı adla tanımlı tür. Statik
/// karşılaştırma (<see cref="DtoEslesmeTests"/>), izin listesinin bayatlık denetimi ve koşullu alan kuralı
/// (<see cref="KosulluMu"/>) aynı eşlemeyi kullanır.
/// </summary>
public static class SunucuKarsiliklari
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>Rapor uçları Kasa.Core kayıtlarını olduğu gibi yazar; istemci onları "Dto" ekli adla yeniden tanımlar.
    /// Aynı ad kuralına uymadığı için ayrıca eşlenir. İstemcide adı Kasa.Core'daki bir türün "Dto" ekli hali olan her tür
    /// bu tabloda olmalıdır (<see cref="DtoEslesmeTests"/> denetler).</summary>
    public static readonly IReadOnlyDictionary<Type, Type> Cekirdek = new Dictionary<Type, Type>
    {
        [typeof(HaftalikOzetDto)] = typeof(Kasa.Core.HaftalikOzet),
        [typeof(KanalHaftalikDto)] = typeof(Kasa.Core.KanalHaftalik),
        [typeof(AylikRaporDto)] = typeof(Kasa.Core.AylikRapor),
        [typeof(KanalAylikDto)] = typeof(Kasa.Core.KanalAylik),
        [typeof(DonemDto)] = typeof(Kasa.Core.Donem),
    };

    /// <summary>Tipli dönüşlü uçların (uç meta verisindeki 200 yanıt türü) yazdığı, istemcidekinden farklı adlı sunucu
    /// türleri: veritabanı varlığı ya da liste projeksiyonu. Tipli dönüşlü her ucun yanıt türü bir istemci türüyle eşlenir
    /// (<see cref="KapsamTests"/> denetler).</summary>
    public static readonly IReadOnlyDictionary<Type, Type> FarkliAdli = new Dictionary<Type, Type>
    {
        [typeof(KanalDto)] = typeof(Kasa.Api.Data.KanalEntity),                 // GET api/kanallar
        [typeof(IslemDto)] = typeof(Kasa.Api.Servisler.IslemOkuDto),            // GET api/islemler
        [typeof(GelenDto)] = typeof(Kasa.Api.Data.GelenEntity),                 // GET api/gelenler
        [typeof(KrediDto)] = typeof(Kasa.Api.Data.KrediEntity),                 // GET api/krediler (eski uç; istemci metodu yok)
        [typeof(KrediKartiDto)] = typeof(Kasa.Api.KrediKartiTuretilmisDto),     // GET api/kredikartlari (eski uç)
        [typeof(KartOdemeDto)] = typeof(Kasa.Api.Data.KartOdemeEntity),         // GET api/kartodemeler (eski uç; istemci metodu yok)
    };

    private static readonly Lazy<Dictionary<string, Type[]>> ApiTurleri = new(() => typeof(Kasa.Api.KartTakipDto).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsGenericType).GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToArray()));

    /// <summary>İstemci kayıt türleri (record) ve sunucu karşılıkları; karşılığı bilinmeyenler dışarıda kalır.</summary>
    public static IReadOnlyList<(Type Istemci, Type Sunucu)> Ciftler() => IstemciKayitlari()
        .Select(t => (Istemci: t, Sunucu: Bul(t))).Where(c => c.Sunucu is not null).Select(c => (c.Istemci, c.Sunucu!)).ToList();

    public static IEnumerable<Type> IstemciKayitlari() => typeof(KasaApiClient).Assembly.GetTypes()
        .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && t.GetMethod("<Clone>$") is not null);

    public static Type? Bul(Type istemci)
    {
        if (Cekirdek.TryGetValue(istemci, out var cekirdek))
            return cekirdek;
        if (FarkliAdli.TryGetValue(istemci, out var farkli))
            return farkli;
        if (!ApiTurleri.Value.TryGetValue(istemci.Name, out var api))
            return null;
        return api.Length == 1 ? api[0] : throw new InvalidOperationException($"{istemci.Name} Kasa.Api'de birden çok ad alanında tanımlı; karşılık belirsiz.");
    }

    /// <summary>İstemcinin JSON'dan doldurabildiği alanlar (kurucu parametresi ya da ayarlanabilir özellik).</summary>
    public static IReadOnlyDictionary<string, JsonPropertyInfo> Okunan(Type tur) => Bilgi(tur)
        .Where(p => p.Set is not null || p.AssociatedParameter is not null).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sunucunun yanıta yazdığı alanlar: okunabilir her özellik (hesaplanan salt okunur özellik de yazılır,
    /// ör. Donem.Yil) ve bağlanabilen alanlar (istek türü olarak kullanıldığında).</summary>
    public static IReadOnlyDictionary<string, JsonPropertyInfo> Yazilan(Type tur) => Bilgi(tur)
        .Where(p => p.Get is not null || p.Set is not null || p.AssociatedParameter is not null).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sunucu türünde alan boşken yazılmıyor mu (JsonIgnore WhenWritingNull/WhenWritingDefault)? Böyle bir alanın
    /// yanıtta olmaması değerinin boş olduğu anlamına gelir; istemcideki karşılığı varsayılanında kalır ve bu doğrudur.</summary>
    public static bool KosulluMu(Type sunucu, string alan) => KosulluAlanlar(sunucu).Contains(alan, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sunucu türünün koşullu yazılan alanları (JSON adlarıyla).</summary>
    public static IEnumerable<string> KosulluAlanlar(Type sunucu) => Bilgi(sunucu).Where(p => p.AttributeProvider is System.Reflection.MemberInfo uye
            && uye.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).OfType<JsonIgnoreAttribute>()
                .Any(a => a.Condition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault))
        .Select(p => p.Name);

    private static IList<JsonPropertyInfo> Bilgi(Type tur)
    {
        var bilgi = Web.GetTypeInfo(tur);
        return bilgi.Kind == JsonTypeInfoKind.Object ? bilgi.Properties : [];
    }
}
