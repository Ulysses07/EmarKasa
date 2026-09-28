using System.Net;
using System.Text.Json.Serialization;

namespace Kasa.Sozlesme.Tests;

/// <summary>Sözleşme denetiminin kendi kuralları, çalışan sunucu olmadan: fazla alan iki yönde hata, sunucunun yalnız
/// doluyken yazdığı alanın yokluğu hata değil, para düz JSON sayısı.</summary>
public class SozlesmeDenetimiTests
{
    public sealed record OrnekIstemci(string Ad, decimal Tutar, string? Uyari = null);
    public sealed record UyarisizIstemci(string Ad, decimal Tutar);
    public sealed record KosulluSunucu(string Ad, decimal Tutar, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Uyari = null);
    public sealed record DuzSunucu(string Ad, decimal Tutar, string? Uyari = null);

    private static List<string> Denetle(Type istemci, string json, Type? sunucu)
        => SozlesmeDenetimi.YanitHatalari(istemci, new("GET api/ornek", HttpStatusCode.OK, json), t => t == istemci ? sunucu : null);

    [Fact]
    public void Sunucunun_bosken_yazmadigi_alanin_yoklugu_hata_degil()
        => Assert.Empty(Denetle(typeof(OrnekIstemci), """{"ad":"a","tutar":1.5}""", typeof(KosulluSunucu)));

    [Fact]
    public void Kosulsuz_alan_yanitta_yoksa_istemci_fazlasi_sayilir()
    {
        Assert.Contains(Denetle(typeof(OrnekIstemci), """{"ad":"a","tutar":1.5}""", typeof(DuzSunucu)), h => h.Contains("$.uyari") && h.Contains("sunucu göndermiyor"));
        Assert.Contains(Denetle(typeof(OrnekIstemci), """{"ad":"a","tutar":1.5}""", null), h => h.Contains("$.uyari"));
    }

    [Fact]
    public void Dolu_kosullu_alani_tanimayan_istemci_sunucu_fazlasi_bildirir()
        => Assert.Contains(Denetle(typeof(UyarisizIstemci), """{"ad":"a","tutar":1.5,"uyari":"ufuk"}""", typeof(KosulluSunucu)),
            h => h.Contains("$.uyari") && h.Contains("sessizce atılır"));

    [Fact]
    public void Para_ussel_gosterimle_ya_da_metin_olarak_gelemez()
    {
        Assert.Contains(Denetle(typeof(UyarisizIstemci), """{"ad":"a","tutar":1E3}""", null), h => h.Contains("düz sayı"));
        Assert.NotEmpty(Denetle(typeof(UyarisizIstemci), """{"ad":"a","tutar":"12.5"}""", null));
    }
}
