using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class IstemciSurumKapisiTests
{
    private const string Baslik = "X-Kasa-Istemci-Surumu";

    [Fact]
    public async Task Eski_masaustu_okuyabilir_ama_kayit_degistiremez_guncel_surumu_yazar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(new DateOnly(2026, 9, 25));
        using var girisIstemcisi = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var ayarlar = f.Services.GetRequiredService<IConfiguration>();
        using var giris = await girisIstemcisi.PostAsJsonAsync("/api/auth/login",
            new { kullanici = ayarlar["Kasa:EditorKullanici"], sifre = ayarlar["Kasa:EditorSifre"] }, ct);
        giris.EnsureSuccessStatusCode();
        var jwt = (await giris.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("token").GetString();
        using var masaustu = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        masaustu.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Eski uygulama listeyi ve güncelleme bilgisini okuyabilsin.
        Assert.Equal(HttpStatusCode.OK, (await masaustu.GetAsync("/api/ayarlar", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await masaustu.GetAsync("/api/surum", ct)).StatusCode);
        var yazim = new { takipBaslangic = new DateOnly(2026, 9, 1), kasaAcilisDevri = 123m };

        async Task Reddedilir()
        {
            using var yanit = await masaustu.PutAsJsonAsync("/api/ayarlar", yazim, ct);
            Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
            var hata = await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            Assert.Contains("uygulamayı güncelleyin", hata.GetProperty("hata").GetString());
        }

        await Reddedilir(); // 2.3 gibi sürüm başlığı göndermeyen istemci
        using (var olusturma = await masaustu.PostAsJsonAsync("/api/islemler",
            new { tarih = new DateOnly(2026, 9, 10), cari = "Kira", tutarTl = 10m, kanal = "MEZAT", tip = "Cari" }, ct))
            Assert.Equal(HttpStatusCode.Conflict, olusturma.StatusCode);
        Assert.Equal(0, (await masaustu.GetFromJsonAsync<JsonElement>("/api/islemler", ct)).GetArrayLength());
        masaustu.DefaultRequestHeaders.Add(Baslik, "2.3.0");
        await Reddedilir();
        masaustu.DefaultRequestHeaders.Remove(Baslik);
        masaustu.DefaultRequestHeaders.Add(Baslik, "2.4.0");
        await Reddedilir(); // yayınlanmış 2.4.0 ZIP'i güncelleme ister
        masaustu.DefaultRequestHeaders.Remove(Baslik);
        masaustu.DefaultRequestHeaders.Add(Baslik, "gecersiz");
        await Reddedilir();
        Assert.Equal(0m, (await masaustu.GetFromJsonAsync<JsonElement>("/api/ayarlar", ct)).GetProperty("kasaAcilisDevri").GetDecimal());

        masaustu.DefaultRequestHeaders.Remove(Baslik);
        masaustu.DefaultRequestHeaders.Add(Baslik, YonetimEndpoints.MinimumIstemci);
        using var guncel = await masaustu.PutAsJsonAsync("/api/ayarlar", yazim, ct);
        Assert.Equal(HttpStatusCode.OK, guncel.StatusCode);
        Assert.Equal(123m, (await masaustu.GetFromJsonAsync<JsonElement>("/api/ayarlar", ct)).GetProperty("kasaAcilisDevri").GetDecimal());
    }

    [Fact]
    public async Task Tarayici_cerezi_surumsuz_yazabilir()
    {
        await using var f = KasaWebFactory.Sabit(new DateOnly(2026, 9, 25));
        using var tarayici = await f.EditorClientAsync();
        using var yanit = await tarayici.PutAsJsonAsync("/api/ayarlar",
            new { takipBaslangic = new DateOnly(2026, 9, 1), kasaAcilisDevri = 45m }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
