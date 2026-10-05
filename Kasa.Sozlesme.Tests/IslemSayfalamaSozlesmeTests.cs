using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

public class IslemSayfalamaSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IIslemSayfalamaApi.IslemlerSayfasiAsync))]
    public async Task Sayfali_islem_sozlesmesi_esit_tarihte_imlec_ve_kanal_filtresini_korur()
    {
        var o = await Editor();
        var ct = TestContext.Current.CancellationToken;
        var ilk = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "İlk", 10m, "MEZAT", GiderTipi.Cari, null));
        var baska = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Başka kanal", 20m, "PERAKENDE", GiderTipi.Cari, null));
        var son = await o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Son", 30m, "MEZAT", GiderTipi.Cari, null));

        var birinci = await o.Sayfalama.IslemlerSayfasiAsync(Bugun, Bugun, "MEZAT", limit: 1, ct: ct);
        Assert.Equal(son.Id, Assert.Single(birinci.Kayitlar).Id);
        Assert.True(birinci.DevamVar);
        Assert.False(string.IsNullOrWhiteSpace(birinci.SonrakiImlec));

        var ikinci = await o.Sayfalama.IslemlerSayfasiAsync(Bugun, Bugun, "MEZAT", imlec: birinci.SonrakiImlec, limit: 1, ct: ct);
        Assert.Equal(ilk.Id, Assert.Single(ikinci.Kayitlar).Id);
        Assert.False(ikinci.DevamVar);
        Assert.Null(ikinci.SonrakiImlec);
        Assert.DoesNotContain(new[] { birinci.Kayitlar[0].Id, ikinci.Kayitlar[0].Id }, id => id == baska.Id);
    }
}
