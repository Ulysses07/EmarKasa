using System.Net;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Alış ödemesine bağlanabilir giderler (webui-6) ve oluşturma tekrar anahtarı (appcore-5) gerçek sunucuya karşı: istemcinin
/// sayfa türü ve sorgu dizesi sunucunun ucuyla birebir uyar; aynı istek kimliğiyle yeniden gönderilen gider ve alış ikinci
/// kayıt açmaz.
/// </summary>
public class BaglanabilirGiderSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IAlisApi.BaglanabilirGiderlerAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IAlisApi.AlisOlusturAsync))]
    public async Task Baglanabilir_giderler_sayfasi_ve_olusturma_tekrari_istemci_turlerine_uyar()
    {
        var editor = await Editor();
        await editor.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var gider = new IslemYaz(Bugun, "Kargo sözleşme", 42.5m, "MEZAT", GiderTipi.Cari, "Bağlanacak", IstekId: Guid.NewGuid());
        var ilk = await editor.Kasa.IslemOlusturAsync(gider);
        Assert.Equal(HttpStatusCode.Created, editor.SonYanit.Durum);
        var tekrar = await editor.Kasa.IslemOlusturAsync(gider);
        Assert.Equal(HttpStatusCode.OK, editor.SonYanit.Durum); Assert.Equal(ilk.Id, tekrar.Id);
        await editor.Kasa.IslemOlusturAsync(gider with { Cari = "Diğer gider", IstekId = Guid.NewGuid() });

        var sayfa = await editor.Alis.BaglanabilirGiderlerAsync(limit: 1);
        Assert.True(sayfa.DevamVar); Assert.NotNull(sayfa.SonrakiImlec);
        var sonraki = await editor.Alis.BaglanabilirGiderlerAsync(imlec: sayfa.SonrakiImlec, limit: 1);
        Assert.Equal(new[] { ilk.Id }, sonraki.Ogeler.Select(o => o.Id));
        var bulunan = Assert.Single((await editor.Alis.BaglanabilirGiderlerAsync("Kargo", 42.5m, Baslangic, Bugun)).Ogeler);
        Assert.Equal((ilk.Id, 42.5m, GiderTipi.Cari, "Bağlanacak"), (bulunan.Id, bulunan.TutarTl, bulunan.Tip, bulunan.Not));

        var alis = new AlisYaz(0, Bugun, "Tekrar Ltd", null, [new AlisKalemYaz("Mal", 42.5m, [new(1, 42.5m)])], IstekId: Guid.NewGuid());
        var olusan = await editor.Alis.AlisOlusturAsync(alis);
        Assert.Equal(HttpStatusCode.Created, editor.SonYanit.Durum);
        Assert.Equal(olusan.Id, (await editor.Alis.AlisOlusturAsync(alis)).Id);
        Assert.Equal(HttpStatusCode.OK, editor.SonYanit.Durum);
        Assert.Single(await editor.Alis.AlislarAsync());
    }
}
