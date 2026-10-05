using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;

namespace Kasa.Api.Tests;

public class IslemSayfaEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Sayfali_uc_yeniyi_once_dondurur_tam_ucun_sirasi_kalir_ve_gecersiz_istekleri_reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(new DateOnly(2026, 9, 25));
        using var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 9, 1), kasaAcilisDevri = 0m }, cancellationToken: ct)).EnsureSuccessStatusCode();
        foreach (var (tarih, cari) in new[]
        {
            (new DateOnly(2026, 9, 1), "İlk"),
            (new DateOnly(2026, 9, 2), "İkinci"),
            (new DateOnly(2026, 9, 2), "Üçüncü"),
        })
            (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, cari, 10m, "MEZAT", GiderTipi.Cari), cancellationToken: ct)).EnsureSuccessStatusCode();

        var ilk = (await c.GetFromJsonAsync<IslemSayfasi>("/api/islemler/sayfa?limit=2", Json, ct))!;
        Assert.Equal(new[] { "Üçüncü", "İkinci" }, ilk.Kayitlar.Select(i => i.Cari));
        Assert.True(ilk.DevamVar);
        Assert.NotNull(ilk.SonrakiImlec);
        var son = (await c.GetFromJsonAsync<IslemSayfasi>("/api/islemler/sayfa?limit=2&imlec=" + ilk.SonrakiImlec, Json, ct))!;
        Assert.Equal("İlk", Assert.Single(son.Kayitlar).Cari);
        Assert.False(son.DevamVar);
        Assert.Null(son.SonrakiImlec);

        var eski = (await c.GetFromJsonAsync<List<IslemOkuDto>>("/api/islemler", Json, ct))!;
        Assert.Equal(new[] { "İlk", "İkinci", "Üçüncü" }, eski.Select(i => i.Cari));
        var tek = (await c.GetFromJsonAsync<IslemOkuDto>($"/api/islemler/{eski[0].Id}", Json, ct))!;
        Assert.Equal(eski[0], tek);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/islemler/999999", ct)).StatusCode);

        using var anonim = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync($"/api/islemler/{tek.Id}", ct)).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izleyici-sifre-123" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        Assert.Equal(tek, await izleyici.GetFromJsonAsync<IslemOkuDto>($"/api/islemler/{tek.Id}", Json, ct));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler/sayfa?limit=0", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler/sayfa?imlec=bozuk", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/islemler/sayfa?baslangic=2026-09-03&bitis=2026-09-01", ct)).StatusCode);
    }
}
