using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// contract-6: çekirdek kasa kayıtlarının (gider, gelir, kanal, ayarlar) sürümü. Okuma ve yazma yanıtları kaydın sürümünü taşır;
/// yeni istemci düzenlemede okuduğu sürümü gönderir, kayıt arada değiştiyse gerçek sunucu 409 ve istemcinin okuduğu iletiyle
/// reddeder. Sürüm göndermeyen eski istemcinin (canlıdaki 2.3.0 masaüstü, önbellekteki eski web) gövdesi kabul edilir: denetlenmez,
/// son yazan kazanır, sürüm yine artar (Kasa:MinimumIstemci değişmez).
/// </summary>
public class CekirdekSurumSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.AyarlarAsync), nameof(IKasaApi.AyarGuncelleAsync), nameof(IKasaApi.KanalOlusturAsync), nameof(IKasaApi.KanalGuncelleAsync),
        nameof(IKasaApi.KanallarAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemGuncelleAsync), nameof(IKasaApi.IslemlerAsync),
        nameof(IKasaApi.GelenKaydetAsync), nameof(IKasaApi.GelenlerAsync))]
    public async Task Surum_okunur_ve_gonderilir_yanlis_surum_409_ve_okunur_iletiyle_reddedilir()
    {
        var o = await Editor();
        async Task Cakisma(Func<Task> yazma, string ileti)
        {
            var hata = await Assert.ThrowsAsync<KasaApiException>(yazma);
            Assert.Equal((HttpStatusCode.Conflict, ileti), (hata.DurumKodu, hata.Message));
        }

        var ayar = await o.Kasa.AyarlarAsync();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m, ayar.Surum));
        var guncelAyar = await o.Kasa.AyarlarAsync();
        Assert.Equal((1000m, ayar.Surum + 1), (guncelAyar.KasaAcilisDevri, guncelAyar.Surum));
        await Cakisma(() => o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 0m, ayar.Surum)), "Ayarlar başka bir oturumda değişti. Güncel değerleri yükleyip tekrar deneyin.");

        var kanal = await o.Kasa.KanalOlusturAsync(new KanalYaz("SÜRÜM", true, 3, 0m));
        Assert.Equal(0, kanal.Surum);
        kanal = await o.Kasa.KanalGuncelleAsync(kanal.Id, new KanalYaz("SÜRÜM", true, 4, 0m, kanal.Surum));
        Assert.Equal((4, 1), (kanal.Sira, kanal.Surum));
        Assert.Equal(1, (await o.Kasa.KanallarAsync()).Single(k => k.Id == kanal.Id).Surum);
        await Cakisma(() => o.Kasa.KanalGuncelleAsync(kanal.Id, new KanalYaz("SÜRÜM", false, 4, 0m, 0)), "Kanal başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.");

        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Sürüm", 1000m, "MEZAT", GiderTipi.Cari, null));
        Assert.Equal(0, gider.Surum);
        gider = await o.Kasa.IslemGuncelleAsync(gider.Id, new IslemYaz(Bugun, "Sürüm", 1200m, "MEZAT", GiderTipi.Cari, null, Surum: gider.Surum));
        Assert.Equal((1200m, 1), (gider.TutarTl, gider.Surum));
        Assert.Equal(1, (await o.Kasa.IslemlerAsync()).Single(i => i.Id == gider.Id).Surum);
        await Cakisma(() => o.Kasa.IslemGuncelleAsync(gider.Id, new IslemYaz(Bugun, "Sürüm", 1000m, "MEZAT", GiderTipi.Cari, "not", Surum: 0)),
            "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.");
        Assert.Equal(1200m, (await o.Kasa.IslemlerAsync()).Single(i => i.Id == gider.Id).TutarTl);

        // Gelir: satır yokken 0 gönderilir, yeni satır 1 ile eklenir; satırı görmeden (0) giren ikinci oturum reddedilir.
        var gelen = await o.Kasa.GelenKaydetAsync(new GelenYaz(Baslangic, "MEZAT", 500m));
        Assert.Equal(1, gelen.Surum);
        Assert.Equal(1, Assert.Single(await o.Kasa.GelenlerAsync(Baslangic)).Surum);
        await Cakisma(() => o.Kasa.GelenKaydetAsync(new GelenYaz(Baslangic, "MEZAT", 700m)),
            "Bu dönem ve kanalın geliri başka bir oturumda değişti. Güncel toplamı yükleyip tekrar deneyin.");
        gelen = await o.Kasa.GelenKaydetAsync(new GelenYaz(Baslangic, "MEZAT", 600m, gelen.Surum));
        Assert.Equal((600m, 2), (gelen.TutarTl, gelen.Surum));
    }

    /// <summary>Eski istemcinin gövdesi (2.3.0 masaüstü ve eski web: sürüm alanı yok) kabul edilir; kayıt yine sürüm artırır ve yeni
    /// istemci güncel sürümü okur.</summary>
    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.AyarlarAsync), nameof(IKasaApi.KanallarAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemGuncelleAsync),
        nameof(IKasaApi.IslemlerAsync), nameof(IKasaApi.GelenKaydetAsync), nameof(IKasaApi.GelenlerAsync))]
    public async Task Eski_istemcinin_surumsuz_govdesi_kabul_edilir_surum_yine_artar()
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await Editor();
        using var eski = F.CreateClient();
        eski.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await o.Depo.OkuAsync());
        async Task Kabul(HttpResponseMessage yanit)
        { using (yanit) Assert.True(yanit.IsSuccessStatusCode, $"{yanit.StatusCode}: {await yanit.Content.ReadAsStringAsync()}"); }

        var surum = (await o.Kasa.AyarlarAsync()).Surum;
        await Kabul(await eski.PutAsJsonAsync("api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 250m }, cancellationToken: ct));
        var ayar = await o.Kasa.AyarlarAsync();
        Assert.Equal((250m, surum + 1), (ayar.KasaAcilisDevri, ayar.Surum));

        var kanal = (await o.Kasa.KanallarAsync()).Single(k => k.Ad == "MEZAT");
        await Kabul(await eski.PutAsJsonAsync($"api/kanallar/{kanal.Id}", new { ad = "MEZAT", aktif = true, sira = 9, acilisDevri = 0m }, cancellationToken: ct));
        Assert.Equal((9, kanal.Surum + 1), (await o.Kasa.KanallarAsync()).Where(k => k.Id == kanal.Id).Select(k => (k.Sira, k.Surum)).Single());

        // Kayıt yeni istemciyle değişmiş (sürüm 1) olsa da eski istemcinin sürümsüz düzenlemesi geçer: son yazan kazanır.
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Eski istemci", 100m, "MEZAT", GiderTipi.Cari, null));
        gider = await o.Kasa.IslemGuncelleAsync(gider.Id, new IslemYaz(Bugun, "Eski istemci", 120m, "MEZAT", GiderTipi.Cari, null, Surum: gider.Surum));
        await Kabul(await eski.PutAsJsonAsync($"api/islemler/{gider.Id}",
            new { tarih = Bugun, cari = "Eski istemci", tutarTl = 90m, kanal = "MEZAT", tip = "Cari", not = (string?)null }, cancellationToken: ct));
        var satir = (await o.Kasa.IslemlerAsync()).Single(i => i.Id == gider.Id);
        Assert.Equal((90m, 2), (satir.TutarTl, satir.Surum));

        var gelen = await o.Kasa.GelenKaydetAsync(new GelenYaz(Baslangic, "MEZAT", 500m));
        await Kabul(await eski.PutAsJsonAsync("api/gelenler", new { donemStart = Baslangic, kanal = "MEZAT", tutarTl = 800m }, cancellationToken: ct));
        var okunan = Assert.Single(await o.Kasa.GelenlerAsync(Baslangic));
        Assert.Equal((800m, gelen.Surum + 1), (okunan.TutarTl, okunan.Surum));
    }
}
