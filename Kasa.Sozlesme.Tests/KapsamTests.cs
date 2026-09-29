using System.Reflection;
using System.Text.RegularExpressions;
using Kasa.ApiClient;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Sozlesme.Tests;

/// <summary>Kapsam bekçisi: KasaApiClient'ın uyguladığı arayüzlerdeki her metot en az bir sözleşme testinde gerçek
/// sunucuya karşı çağrılır. Arayüze yeni metot eklenip sözleşme testi yazılmazsa bu test metodun adıyla kırılır.</summary>
public partial class KapsamTests
{
    private static IReadOnlyList<(string Arayuz, string Metot)> IstemciMetotlari() => typeof(KasaApiClient).GetInterfaces()
        .SelectMany(a => a.GetMethods().Where(m => !m.IsSpecialName).Select(m => (a.Name, m.Name))).Distinct().ToList();

    /// <summary>Yalnız xUnit test metotlarındaki ([Fact]/[Theory]) işaretler kapsam sayılır: test olmayan bir metottaki
    /// işaret hiç koşmaz, çağrının yapıldığı da denetlenmez.</summary>
    private static IReadOnlyList<(string Test, string Metot)> Isaretler() => typeof(KapsamTests).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods()).Where(m => m.IsDefined(typeof(FactAttribute), inherit: true))
        .SelectMany(m => m.GetCustomAttributes<SozlesmeKapsamiAttribute>()
            .SelectMany(a => a.Metotlar.Select(x => ($"{m.DeclaringType!.Name}.{m.Name}", x)))).ToList();

    private static List<Type> SozlesmeSiniflari() => typeof(KapsamTests).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(SozlesmeTemeli)) && !t.IsAbstract).ToList();

    /// <summary>Kapsam denetimi (işaretli metot o testte gerçekten çağrıldı mı) test metoduna bırakılmaz: her sözleşme
    /// sınıfı denetimi temel sınıftan devralır ve xUnit onu her testin sonunda çalıştırır. Denetimin kaçırmaması için
    /// bu sınıflardaki her test işaretlidir.</summary>
    [Fact]
    public void Sozlesme_test_siniflari_kapsam_denetimini_devralir_ve_her_testi_isaretlidir()
    {
        var siniflar = SozlesmeSiniflari();
        Assert.True(siniflar.Count >= 7, $"Sözleşme test sınıfları okunamadı ({siniflar.Count}).");
        Assert.All(siniflar, t => Assert.NotNull(t.GetCustomAttribute<SozlesmeKapsamiDenetimiAttribute>(inherit: true)));
        var testler = siniflar.SelectMany(t => t.GetMethods().Where(m => m.IsDefined(typeof(FactAttribute), inherit: true))).ToList();
        Assert.True(testler.Count >= 16, $"Sözleşme testleri okunamadı ({testler.Count}).");
        var isaretsiz = testler.Where(m => !m.IsDefined(typeof(SozlesmeKapsamiAttribute))).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();
        Assert.True(isaretsiz.Count == 0, "[SozlesmeKapsami] işareti olmayan sözleşme testleri:\n" + string.Join("\n", isaretsiz));
    }

    [Fact]
    public void Kapsam_denetimi_cagrilmayan_isaretli_metodu_ve_isaretsiz_testi_bildirir()
    {
        var isaretli = typeof(OrnekTestler).GetMethod(nameof(OrnekTestler.Isaretli))!;
        Assert.Empty(SozlesmeTemeli.KapsamHatalari(isaretli, new HashSet<string> { nameof(IKasaApi.LoginAsync), nameof(IKasaApi.BenKimAsync), nameof(IKasaApi.PanelAsync) }));
        var hata = Assert.Single(SozlesmeTemeli.KapsamHatalari(isaretli, new HashSet<string> { nameof(IKasaApi.LoginAsync) }));
        Assert.Contains($"çağrılmayan metotlar: {nameof(IKasaApi.BenKimAsync)} ", hata);
        Assert.Contains("işareti yok", Assert.Single(SozlesmeTemeli.KapsamHatalari(typeof(OrnekTestler).GetMethod(nameof(OrnekTestler.Isaretsiz))!, new HashSet<string>())));
    }

    /// <summary>Kapsam denetiminin birim testi için örnek metotlar; test değildir, işaretleri kapsam sayılmaz.</summary>
    private static class OrnekTestler
    {
        [SozlesmeKapsami(nameof(IKasaApi.LoginAsync), nameof(IKasaApi.BenKimAsync))]
        public static void Isaretli() { }
        public static void Isaretsiz() { }
    }

    [Fact]
    public void Istemci_arayuzlerindeki_her_metot_bir_sozlesme_testinde_cagrilir()
    {
        var metotlar = IstemciMetotlari();
        Assert.True(metotlar.Count > 90, $"İstemci metotları okunamadı ({metotlar.Count}).");
        var isaretli = Isaretler().Select(i => i.Metot).ToHashSet();
        var eksik = metotlar.Where(m => !isaretli.Contains(m.Metot)).Select(m => $"{m.Arayuz}.{m.Metot}").ToList();
        Assert.True(eksik.Count == 0, "Sözleşme testi olmayan istemci metotları (gerçek sunucuya karşı bir test yazıp [SozlesmeKapsami] ile işaretleyin):\n" + string.Join("\n", eksik));
    }

    [Fact]
    public void Kapsam_isaretleri_var_olan_metotlari_gosterir()
    {
        var adlar = IstemciMetotlari().Select(m => m.Metot).ToHashSet();
        var bilinmeyen = Isaretler().Where(i => !adlar.Contains(i.Metot)).Select(i => $"{i.Test}: {i.Metot}").ToList();
        Assert.True(bilinmeyen.Count == 0, "İstemcide bulunmayan metot işaretleri: " + string.Join(", ", bilinmeyen));
    }

    [Fact]
    public void Izin_listesi_satirlari_gerekceli_ve_tekil()
    {
        Assert.All(SozlesmeIzinleri.Liste, i => Assert.False(string.IsNullOrWhiteSpace(i.Gerekce)));
        Assert.Equal(SozlesmeIzinleri.Liste.Count, SozlesmeIzinleri.Liste.Select(i => (i.Yon, i.Tur, i.Alan.ToLowerInvariant(), i.Uc)).Distinct().Count());
    }

    /// <summary>Her izin satırı hâlâ gerçek bir farkı anlatır: DTO eşitlenince ya da uç kalkınca satır silinmelidir
    /// (aksi halde aynı alandaki gelecekteki gerçek sapmayı örter).</summary>
    [Fact]
    public void Izin_listesi_satirlari_bayat_degil()
    {
        var uclar = SunucuUclari();
        Assert.Contains("GET api/rapor/haftalik", uclar); Assert.Contains("PUT api/islemler/{id}", uclar);
        var bayat = SozlesmeIzinleri.Liste.SelectMany(i => SozlesmeIzinleri.BayatlikHatalari(i, uclar)).ToList();
        Assert.True(bayat.Count == 0, "Bayat izin satırları (silin ya da gerekçesini güncelleyin):\n" + string.Join("\n", bayat));
    }

    [Fact]
    public void Bayat_izin_satiri_bildirilir()
    {
        var uclar = new HashSet<string> { "PUT api/islemler/{id}" };
        Hata(new(SozlesmeIzinleri.Yon.SunucuFazlasi, typeof(IslemDto), "tutarTl", null, "istemci alanı tanıyor"), uclar, "artık tanıyor");
        Hata(new(SozlesmeIzinleri.Yon.SunucuFazlasi, typeof(HaftalikOzetDto), "yokAlan", null, "sunucu yazmıyor"), uclar, "artık yazmıyor");
        Hata(new(SozlesmeIzinleri.Yon.IstemciFazlasi, typeof(IslemDto), "yokAlan", null, "istemcide yok"), uclar, "böyle bir alan yok");
        Hata(new(SozlesmeIzinleri.Yon.IstemciFazlasi, typeof(KanalHaftalikDto), "gelen", null, "sunucu yazıyor"), uclar, "artık yazıyor");
        Hata(new(SozlesmeIzinleri.Yon.IstekFazlasi, typeof(Kasa.Api.KrediYazDto), "ad", "PUT api/islemler/{id}", "sunucu bağlıyor"), uclar, "artık bağlıyor");
        Hata(new(SozlesmeIzinleri.Yon.IstemciFazlasi, typeof(IslemDto), "alisId", "POST api/yok", "uç yok"), uclar, "böyle bir uç yok");
        Assert.Empty(SozlesmeIzinleri.BayatlikHatalari(new(SozlesmeIzinleri.Yon.SunucuFazlasi, typeof(KanalHaftalikDto), "krediGirisi", null, "geçerli"), uclar));

        static void Hata(SozlesmeIzinleri.Izin satir, IReadOnlySet<string> uclar, string beklenen)
            => Assert.Contains(SozlesmeIzinleri.BayatlikHatalari(satir, uclar), h => h.Contains(beklenen));
    }

    /// <summary>Uç meta verisinde 200 yanıt türü olan (tipli dönüşlü) her ucun yazdığı tür bir istemci türünün sunucu
    /// karşılığıdır: aksi halde o türün alanları statik karşılaştırmaya hiç girmez (sapma yalnız çalışan sunucu testi o
    /// dalı doldurursa görünür).</summary>
    [Fact]
    public void Tipli_uclarin_yanit_turleri_bir_istemci_turuyle_eslenir()
    {
        var eslenen = SunucuKarsiliklari.Ciftler().Select(c => c.Sunucu).ToHashSet();
        var tipli = SunucuUclariVeYanitlari().Where(u => u.Yanit is not null).ToList();
        Assert.True(tipli.Count >= 10, $"Tipli dönüşlü uç okunamadı ({tipli.Count}).");
        var eksik = tipli.Where(u => !eslenen.Contains(Eleman(u.Yanit!))).Select(u => $"{u.Uc} → {Eleman(u.Yanit!).FullName}").ToList();
        Assert.True(eksik.Count == 0, "İstemci karşılığı bilinmeyen yanıt türleri (SunucuKarsiliklari'na ekleyin):\n" + string.Join("\n", eksik));

        static Type Eleman(Type t) => t != typeof(string) && t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t) ? t.GetGenericArguments()[0] : t;
    }

    /// <summary>Sunucunun gerçek uçları, istemci uç adlarıyla aynı biçimde ("YÖNTEM yol", yol parametreleri {id}).</summary>
    private static HashSet<string> SunucuUclari() => SunucuUclariVeYanitlari().Select(u => u.Uc).ToHashSet();

    private static List<(string Uc, Type? Yanit)> SunucuUclariVeYanitlari()
    {
        using var f = new SozlesmeFabrikasi();
        return f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(y => (
                Uc: $"{y} {Parametre().Replace(e.RoutePattern.RawText!.TrimStart('/'), "{id}")}",
                Yanit: e.Metadata.OfType<IProducesResponseTypeMetadata>().FirstOrDefault(p => p.StatusCode == 200 && p.Type is not null && p.Type != typeof(void))?.Type)))
            .ToList();
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parametre();
}
