using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Değişiklik geçmişi (denetim izi): istemci süzgeçleri sorgu dizesine doğru taşır, satır türü sunucununkiyle
/// birebir uyar, kilit açılışının kimliği ay kilidi geçmişindeki olayın kimliğidir; izleyici okuyamaz.</summary>
public class DenetimSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IDenetimApi.DenetimOlaylariAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemGuncelleAsync),
        nameof(IAylikGiderApi.AyKilidiDegistirAsync))]
    public async Task Degisiklik_gecmisi_suzgecleri_ve_kilit_penceresi_istemci_turune_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var gecenAy = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-1);
        var gider = await o.Kasa.IslemOlusturAsync(new IslemYaz(gecenAy.AddDays(2), "Tedarikçi", 1250.5m, "MEZAT", GiderTipi.Cari, "Fatura"));
        var kilit = await o.AylikGider.AyKilidiDegistirAsync(true, new AyKilidiYaz(Guid.NewGuid(), (await o.AylikGider.AyKilidiAsync()).Surum, gecenAy.Year, gecenAy.Month, "Ay kapandı"));
        kilit = await o.AylikGider.AyKilidiDegistirAsync(false, new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, gecenAy.Year, gecenAy.Month, "Fatura düzeltmesi"));
        await o.Kasa.IslemGuncelleAsync(gider.Id, new IslemYaz(gecenAy.AddDays(2), "Tedarikçi", 1300m, "MEZAT", GiderTipi.Cari, "Fatura"));

        var id = gider.Id.ToString(CultureInfo.InvariantCulture);
        var olaylar = await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id));
        Assert.Equal(["Degistir", "Ekle"], olaylar.Select(x => x.Tur));
        Assert.All(olaylar, x => Assert.Equal(("editor", "Islem", id), (x.AktorRol, x.Varlik, x.VarlikId)));
        Assert.Equal((1250.5m, 1300m), (JsonNode.Parse(olaylar[0].OncekiJson!)!["TutarTl"]!.GetValue<decimal>(), JsonNode.Parse(olaylar[0].YeniJson!)!["TutarTl"]!.GetValue<decimal>()));
        var acilis = kilit.Gecmis[0];
        Assert.Equal((acilis.Id, (int?)null), (olaylar[0].KilitAcmaOlayiId, olaylar[1].KilitAcmaOlayiId));

        var pencere = await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(KilitAcmaOlayiId: acilis.Id));
        Assert.Equal(["Degistir", "KilitAc"], pencere.Select(x => x.Tur));
        Assert.Equal("Fatura düzeltmesi", pencere[1].Gerekce);
        var ilk = Assert.Single(await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id, Adet: 1)));
        var sonraki = Assert.Single(await o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Varlik: "Islem", VarlikId: id, OncekiId: ilk.Id, Adet: 1)));
        Assert.Equal(olaylar.Select(x => x.Id), [ilk.Id, sonraki.Id]);
        Assert.NotEmpty(await o.Denetim.DenetimOlaylariAsync());

        await o.Kasa.IzleyiciSifreAsync("izleyici-denetim-1");
        var izleyici = Istemci();
        await izleyici.Kasa.LoginAsync(null, "izleyici-denetim-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<KasaApiException>(() => izleyici.Denetim.DenetimOlaylariAsync())).DurumKodu);
        var gecersiz = await Assert.ThrowsAsync<KasaApiException>(() => o.Denetim.DenetimOlaylariAsync(new DenetimSorgusu(Adet: 500)));
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.DurumKodu);
    }
}
