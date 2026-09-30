using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Uç yetki taraması (tests-7): '/api' altındaki bütün uçlar EndpointDataSource'tan keşfedilir; yeni bir uç eklendiğinde
/// testler kendiliğinden kapsar. Yazma yetkisi (Finans/Alis gruplarında) tek tek eklenen RequireAuthorization("Editor")
/// çağrısına dayandığından, çağrıyı unutulan bir mutasyon burada kırmızıya düşer.
/// </summary>
public class UcYetkiTaramasiTests : IClassFixture<SabitSaatliKasaWebFactory>
{
    private static readonly string[] Roller = ["editor", "viewer", "alici"];
    private static readonly string[] YazmaYontemleri = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Kimliksiz açık uçlar: giriş, çıkış ve şifre kurtarma (kendi hız sınırlarıyla) ile sürüm sorgusu.</summary>
    private static readonly string[] AnonimUclar =
    [
        "POST /api/auth/login", "POST /api/auth/logout", "POST /api/auth/kurtar", "GET /api/surum",
    ];

    /// <summary>Editör olmadan yazılabilen mutasyonlar: alıcı kendi taslak alışını ve belgesini yönetir (Alis politikası;
    /// sahiplik ve durum kuralları uçta denetlenir). Listeye eklenen her satır bilinçli bir yetki kararıdır.</summary>
    private static readonly string[] AlisYazmaUclari =
    [
        "POST /api/alis", "PUT /api/alis/{id:int}", "POST /api/alis/{id:int}/gonder",
        "POST /api/alis/{id:int}/belgeler", "DELETE /api/belgeler/{id:int}",
    ];

    private readonly SabitSaatliKasaWebFactory _f;
    public UcYetkiTaramasiTests(SabitSaatliKasaWebFactory f) => _f = f;

    private sealed record Uc(string Yontem, string Desen, RouteEndpoint Kaynak)
    {
        public string Ad => $"{Yontem} {Desen}";
        public IReadOnlyList<IAuthorizeData> Yetkiler => Kaynak.Metadata.GetOrderedMetadata<IAuthorizeData>();
        public bool Anonim => Yetkiler.Count == 0 || Kaynak.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        public bool Yazma => YazmaYontemleri.Contains(Yontem);
    }

    /// <summary>'/api' altındaki her (yöntem, desen) çifti. Yöntem bilgisi taşımayan uç bütün yöntemleri kabul eder;
    /// o yüzden yazma sayılır.</summary>
    private IReadOnlyList<Uc> ApiUclari()
    {
        var uclar = _f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(y => new Uc(y == "*" ? "POST" : y, e.RoutePattern.RawText!.TrimEnd('/'), e)))
            .ToList();
        Assert.True(uclar.Count > 50, $"Uç keşfi beklenenden az uç buldu ({uclar.Count}).");
        return uclar;
    }

    [Fact]
    public void Api_mutasyonlari_editor_ya_da_bilinen_istisna_politikasi_tasir()
    {
        var uclar = ApiUclari();
        var ihlaller = new List<string>();
        foreach (var uc in uclar.Where(u => u.Yazma))
        {
            var politikalar = uc.Yetkiler.Select(a => a.Policy).ToHashSet();
            if (AnonimUclar.Contains(uc.Ad))
            {
                if (!uc.Anonim)
                    ihlaller.Add($"{uc.Ad}: anonim listede ama yetki istiyor ({string.Join(",", politikalar)})");
            }
            else if (AlisYazmaUclari.Contains(uc.Ad))
            {
                if (!politikalar.SetEquals(["Alis"]))
                    ihlaller.Add($"{uc.Ad}: yalnız Alis politikası beklenirdi ({string.Join(",", politikalar)})");
            }
            else if (uc.Anonim || !politikalar.Contains("Editor"))
                ihlaller.Add($"{uc.Ad}: Editor politikası yok ({(uc.Anonim ? "anonim" : string.Join(",", politikalar))})");
        }
        Assert.True(ihlaller.Count == 0, "Yazma yetkisi eksik uçlar:\n" + string.Join("\n", ihlaller));

        // İstisna tabloları bayatlamaz: her satır hâlâ gerçek bir uca karşılık gelir.
        var adlar = uclar.Select(u => u.Ad).ToHashSet();
        var bayat = AnonimUclar.Concat(AlisYazmaUclari).Where(a => !adlar.Contains(a)).ToList();
        Assert.True(bayat.Count == 0, "Artık bulunmayan istisna satırları: " + string.Join(", ", bayat));
    }

    [Fact]
    public void Api_okuma_uclari_kimliksiz_acik_degil()
    {
        var acik = ApiUclari().Where(u => !u.Yazma && u.Anonim && !AnonimUclar.Contains(u.Ad)).Select(u => u.Ad).ToList();
        Assert.True(acik.Count == 0, "Yetki meta verisi taşımayan okuma uçları:\n" + string.Join("\n", acik));
    }

    /// <summary>Meta verinin gerçekten uygulandığını çalışan sunucuda doğrular: her uç kimliksiz, izleyici ve alıcı
    /// oturumuyla çağrılır. Politikası rolü kabul etmiyorsa 403 (kimliksizde 401), kabul ediyorsa 401/403 dışında bir yanıt
    /// beklenir. Rol kümeleri sunucunun kendi yetki politikalarından okunur. İzin verilen çağrılar boş JSON gövdesiyle
    /// gider: gövde isteyen uç 400 ile bağlamada durur, iş kuralına ulaşmaz.</summary>
    [Fact]
    public async Task Yetki_matrisi_calisan_sunucuda_meta_veriyle_ayni()
    {
        using var editor = await _f.EditorClientAsync();
        using var izleyici = await IzleyiciAsync(editor);
        using var alici = await AliciAsync(editor);
        using var kimliksiz = _f.CreateClient();
        var saglayici = _f.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var ihlaller = new List<string>();
        foreach (var uc in ApiUclari().Where(u => !u.Anonim))
        {
            var izinli = await IzinliRollerAsync(saglayici, uc);
            Assert.True(izinli.Contains("editor"), $"{uc.Ad}: editör hiçbir rolde yetkili değil.");
            var yol = Yol(uc.Kaynak.RoutePattern);
            var durum = await CagirAsync(kimliksiz, uc.Yontem, yol);
            if (durum != HttpStatusCode.Unauthorized)
                ihlaller.Add($"{uc.Ad} kimliksiz: {(int)durum}");
            foreach (var (rol, istemci) in new[] { ("viewer", izleyici), ("alici", alici) })
            {
                durum = await CagirAsync(istemci, uc.Yontem, yol);
                var beklenen = izinli.Contains(rol);
                if (beklenen && durum is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    ihlaller.Add($"{uc.Ad} {rol}: izinli ama {(int)durum}");
                if (!beklenen && durum != HttpStatusCode.Forbidden)
                    ihlaller.Add($"{uc.Ad} {rol}: 403 beklenirdi, {(int)durum}");
            }
            // İzleyici hiçbir yazma ucunda yetkili olmamalı (Finans grubu okuma içindir).
            if (uc.Yazma && izinli.Contains("viewer"))
                ihlaller.Add($"{uc.Ad}: izleyici yazabiliyor");
        }
        Assert.True(ihlaller.Count == 0, "Yetki matrisi ihlalleri:\n" + string.Join("\n", ihlaller));
    }

    [Theory]
    [InlineData("POST", "/api/takip/kartlar")]
    [InlineData("PUT", "/api/takip/kartlar/1")]
    [InlineData("POST", "/api/takip/kartlar/1/durum")]
    [InlineData("POST", "/api/takip/kartlar/1/harcamalar")]
    [InlineData("POST", "/api/takip/kartlar/1/harcamalar/1/iptal")]
    [InlineData("POST", "/api/takip/kartlar/1/odemeler")]
    [InlineData("POST", "/api/takip/kartlar/1/odemeler/1/iptal")]
    [InlineData("PUT", "/api/takip/kartlar/1/ekstreler/1")]
    [InlineData("POST", "/api/takip/kartlar/1/gecis")]
    [InlineData("POST", "/api/takip/kartlar/1/masraflar")]
    [InlineData("POST", "/api/takip/krediler")]
    [InlineData("POST", "/api/takip/krediler/1/durum")]
    [InlineData("PUT", "/api/takip/krediler/1/taksitler/1")]
    [InlineData("POST", "/api/takip/krediler/1/erken-kapat")]
    [InlineData("POST", "/api/takip/krediler/1/gecis")]
    [InlineData("POST", "/api/aylik-giderler/sablonlar")]
    [InlineData("PUT", "/api/aylik-giderler/sablonlar/1")]
    [InlineData("POST", "/api/aylik-giderler/1/ode")]
    [InlineData("POST", "/api/aylik-giderler/odemeler/1/iptal")]
    [InlineData("POST", "/api/ay-kilidi/kapat")]
    [InlineData("POST", "/api/ay-kilidi/ac")]
    public async Task Izleyici_finans_mutasyonunda_403_alir(string yontem, string yol)
    {
        using var editor = await _f.EditorClientAsync();
        using var izleyici = await IzleyiciAsync(editor);
        using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        using var yanit = await izleyici.SendAsync(istek, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    private const string IzleyiciSifresi = "izleyici-tarama-sifresi";

    private async Task<HttpClient> IzleyiciAsync(HttpClient editor)
    {
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = IzleyiciSifresi })).EnsureSuccessStatusCode();
        var c = _f.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = IzleyiciSifresi })).EnsureSuccessStatusCode();
        return c;
    }

    private async Task<HttpClient> AliciAsync(HttpClient editor)
    {
        var kullanici = "tarama-" + Guid.NewGuid().ToString("N")[..8];
        (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(kullanici, "Tarama alıcısı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        var c = _f.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre = "alici-sifre-1" })).EnsureSuccessStatusCode();
        return c;
    }

    /// <summary>Uçtaki bütün yetki verilerini birlikte sağlayan roller (her politika ayrı ayrı sağlanmalıdır).</summary>
    private static async Task<HashSet<string>> IzinliRollerAsync(IAuthorizationPolicyProvider saglayici, Uc uc)
    {
        var izinli = Roller.ToHashSet();
        foreach (var veri in uc.Yetkiler)
        {
            var politika = veri.Policy is null ? await saglayici.GetDefaultPolicyAsync() : await saglayici.GetPolicyAsync(veri.Policy);
            Assert.True(politika is not null, $"{uc.Ad}: '{veri.Policy}' politikası tanımlı değil.");
            foreach (var gereksinim in politika!.Requirements)
            {
                switch (gereksinim)
                {
                    case RolesAuthorizationRequirement r:
                        izinli.IntersectWith(r.AllowedRoles);
                        break;
                    case DenyAnonymousAuthorizationRequirement:
                        break;
                    default:
                        Assert.Fail($"{uc.Ad}: taramanın tanımadığı yetki gereksinimi {gereksinim.GetType().Name}; tabloyu güncelleyin.");
                        break;
                }
            }
            if (!string.IsNullOrEmpty(veri.Roles))
                izinli.IntersectWith(veri.Roles.Split(',', StringSplitOptions.TrimEntries));
        }
        return izinli;
    }

    /// <summary>Desendeki her parametre 1 olur: rota eşleşir, kayıt yoksa uç 404 ile durur.</summary>
    private static string Yol(RoutePattern desen) => "/" + string.Join("/", desen.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
    {
        RoutePatternLiteralPart l => l.Content,
        RoutePatternParameterPart => "1",
        RoutePatternSeparatorPart a => a.Content,
        _ => throw new InvalidOperationException($"Beklenmeyen rota parçası: {p}"),
    }))));

    private static async Task<HttpStatusCode> CagirAsync(HttpClient c, string yontem, string yol)
    {
        using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol);
        if (yontem != "GET")
            istek.Content = new StringContent("", Encoding.UTF8, "application/json");
        using var yanit = await c.SendAsync(istek);
        return yanit.StatusCode;
    }
}
