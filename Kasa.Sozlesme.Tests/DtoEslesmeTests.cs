using System.Collections;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Elle iki kez tanımlanmış DTO'lar (contract-8): Kasa.ApiClient'taki her kayıt türü, sunucu karşılığıyla
/// (<see cref="SunucuKarsiliklari"/>: Kasa.Api'de aynı ad ya da rapor uçlarının yazdığı Kasa.Core türü) aynı JSON
/// alanlarını (ad ve temel tür) taşımalı. Çalışan sunucuya karşı testlerin ulaşamadığı dallarda ve yalnız dolu olduğunda
/// yazılan alanlarda da sapma derlemede yakalanır. Bilinçli farklar <see cref="SozlesmeIzinleri"/>'ndeki bütün uçlar için
/// geçerli satırlardır. Enum'lar metin olarak taşınır: adlar aynı olmalı.
/// </summary>
public class DtoEslesmeTests
{
    [Fact]
    public void GiderTipi_adlari_sunucu_ile_ayni()
        => Assert.Equal(Enum.GetNames<Kasa.Core.GiderTipi>(), Enum.GetNames<GiderTipi>());

    [Fact]
    public void Istemci_turleri_sunucu_karsiliklariyla_ayni_json_alanlarini_tasir()
    {
        var ciftler = SunucuKarsiliklari.Ciftler();
        Assert.True(ciftler.Count > 65, $"Eşlenen DTO çifti beklenenden az ({ciftler.Count}).");
        Assert.All(SunucuKarsiliklari.Cekirdek, c => Assert.Contains((c.Key, c.Value), ciftler));
        var farklar = ciftler.SelectMany(c => Farklar(c.Istemci, c.Sunucu)).ToList();
        Assert.True(farklar.Count == 0, "İstemci ve sunucu DTO farkları:\n" + string.Join("\n", farklar));
    }

    /// <summary>Rapor uçlarının yazdığı Kasa.Core türleri aynı ad kuralına uymaz (istemcide "Dto" ekli): istemcideki
    /// "XDto" için Kasa.Api'de karşılık yoksa ve Kasa.Core'da X varsa eşleme tablosunda olmalı; yoksa statik
    /// karşılaştırma o türü hiç görmez (HaftalikOzet.VeriSagligiUyarisi sapması böyle gözden kaçmıştı).</summary>
    [Fact]
    public void Kasa_Core_karsiligi_olan_istemci_turleri_esleme_tablosunda()
    {
        var cekirdek = typeof(Kasa.Core.HaftalikOzet).Assembly.GetTypes().Where(t => t.IsPublic).Select(t => t.Name).ToHashSet();
        var eksik = SunucuKarsiliklari.IstemciKayitlari()
            .Where(t => t.Name.EndsWith("Dto") && cekirdek.Contains(t.Name[..^3]) && SunucuKarsiliklari.Bul(t) is null)
            .Select(t => $"{t.Name} ↔ Kasa.Core.{t.Name[..^3]}").ToList();
        Assert.True(eksik.Count == 0, "SunucuKarsiliklari.Cekirdek tablosuna eklenmesi gereken türler: " + string.Join(", ", eksik));
    }

    [Fact]
    public void Tam_sayi_ile_ondalik_ayri_sinif()
    {
        Assert.Equal(Tur(typeof(int)), Tur(typeof(long)));
        Assert.Equal(Tur(typeof(decimal)), Tur(typeof(double?)));
        Assert.NotEqual(Tur(typeof(int)), Tur(typeof(decimal)));
        Assert.NotEqual(Tur(typeof(IReadOnlyList<int>)), Tur(typeof(decimal[])));
    }

    /// <summary>Sunucunun yalnız doluyken yazdığı her alan (JsonIgnore WhenWritingNull/WhenWritingDefault) bir sözleşme
    /// testinde dolu hâliyle istemciden geçirilir: çalışan sunucu denetimi boş değerli yanıtta o alanı hiç görmez.</summary>
    [Fact]
    public void Kosullu_sunucu_alanlari_dolduran_bir_sozlesme_testinde_denetlenir()
    {
        var kosullu = SunucuKarsiliklari.Ciftler().Select(c => c.Sunucu).Distinct()
            .SelectMany(t => SunucuKarsiliklari.KosulluAlanlar(t).Select(a => $"{t.Name}.{a}")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(kosullu);
        var senaryolar = typeof(DtoEslesmeTests).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes(typeof(KosulluAlanSenaryosuAttribute), false).Cast<KosulluAlanSenaryosuAttribute>()
                .Select(a => (Test: $"{m.DeclaringType!.Name}.{m.Name}", Alan: $"{a.SunucuTuru.Name}.{a.Alan}"))).ToList();
        var eksik = kosullu.Where(k => !senaryolar.Any(s => string.Equals(s.Alan, k, StringComparison.OrdinalIgnoreCase))).Order().ToList();
        Assert.True(eksik.Count == 0, "Dolu hâliyle hiçbir sözleşme testinde görülmeyen koşullu alanlar ([KosulluAlanSenaryosu] ile işaretli bir test yazın): " + string.Join(", ", eksik));
        var bayat = senaryolar.Where(s => !kosullu.Contains(s.Alan)).Select(s => $"{s.Test}: {s.Alan}").ToList();
        Assert.True(bayat.Count == 0, "Koşullu olmayan ya da eşlenmemiş alan için senaryo işareti: " + string.Join(", ", bayat));
    }

    private static IEnumerable<string> Farklar(Type istemci, Type sunucu)
    {
        var a = SunucuKarsiliklari.Okunan(istemci); var b = SunucuKarsiliklari.Yazilan(sunucu);
        foreach (var ad in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order())
        {
            if (!b.TryGetValue(ad, out var sunucuAlani))
            {
                if (!SozlesmeIzinleri.HerUctaIzinli(SozlesmeIzinleri.Yon.IstemciFazlasi, istemci, ad)) yield return $"{istemci.Name}.{ad}: yalnız istemcide.";
            }
            else if (!a.TryGetValue(ad, out var istemciAlani))
            {
                if (!SozlesmeIzinleri.HerUctaIzinli(SozlesmeIzinleri.Yon.SunucuFazlasi, istemci, ad)) yield return $"{istemci.Name}.{ad}: yalnız sunucuda ({sunucu.FullName}).";
            }
            else if (Tur(istemciAlani.PropertyType) != Tur(sunucuAlani.PropertyType))
                yield return $"{istemci.Name}.{ad}: istemci {Tur(istemciAlani.PropertyType)}, sunucu {Tur(sunucuAlani.PropertyType)}.";
        }
    }

    /// <summary>JSON'daki biçimi: tam sayı, ondalık (para), metin, tarih, an, mantıksal, enum, dizi ya da iç içe türün
    /// adı. Tam sayı ile ondalık ayrıdır: bir tarafta int, öbür tarafta decimal olan para alanı kuruşu keser.</summary>
    internal static string Tur(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return "metin";
        if (t == typeof(bool)) return "mantıksal";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return "tam sayı";
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return "ondalık";
        if (t == typeof(DateOnly)) return "tarih";
        if (t == typeof(DateTimeOffset)) return "an";
        if (t == typeof(Guid)) return "guid";
        if (t.IsEnum) return "enum";
        if (typeof(IEnumerable).IsAssignableFrom(t))
            return "dizi<" + Tur(t.IsArray ? t.GetElementType()! : t.GetGenericArguments()[0]) + ">";
        return t.Name.EndsWith("Dto") ? t.Name[..^3] : t.Name;
    }
}

/// <summary>Testin, sunucunun yalnız doluyken yazdığı alanı (<see cref="Alan"/>, C# özellik adı) dolu hâliyle
/// istemciden geçirdiğini bildirir; test o alanın yanıtta dolu geldiğini ayrıca doğrular.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class KosulluAlanSenaryosuAttribute(Type sunucuTuru, string alan) : Attribute
{
    public Type SunucuTuru { get; } = sunucuTuru;
    public string Alan { get; } = alan;
}
