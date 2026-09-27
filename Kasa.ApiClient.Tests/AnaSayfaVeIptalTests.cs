using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>Ana sayfa özeti tek istekte (panel + kanal eşikleri + takip özeti, sunucunun tek anlık görüntüsü); eski sunucuda
/// uç yoksa (404) panele geri düşülür. Haftalık raporun veri sağlığı uyarısı okunur; rapor okumaları iptal edilebilir.</summary>
public class AnaSayfaVeIptalTests
{
    private sealed class Kayitci(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> yanit) : HttpMessageHandler
    {
        public List<string> Istekler { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
        {
            Istekler.Add(istek.RequestUri!.PathAndQuery);
            return yanit(istek, ct);
        }
    }

    private static KasaApiClient Client(HttpMessageHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    private static Task<HttpResponseMessage> Json(string govde) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(govde, Encoding.UTF8, "application/json") });
    private static Task<HttpResponseMessage> Durum(HttpStatusCode kod) => Task.FromResult(new HttpResponseMessage(kod));

    private const string PanelJson = """{"guncelKasa":900,"kanallar":[{"kanal":"MEZAT","bakiye":600,"kanalId":1},{"kanal":"PERAKENDE","bakiye":300,"kanalId":2}],"buHaftaSonucu":10,"buAySonucu":-5,"dagilimBekleyenTutar":7}""";
    private const string AnaSayfaJson = """{"panel":""" + PanelJson + """
        ,"kasaEsikleri":[{"kanalId":1,"kanal":"MEZAT","surum":2,"tutar":1000,"etkin":true,"bakiye":600,"esikAltinda":true}],
         "takipOzeti":{"tarih":"2026-09-28","kartBorcu":120,"kalanKrediPlani":40,"olaylar":[],"kanalKartBorclari":[{"kanalId":1,"kanal":"MEZAT","tutar":120}],"kartAlacakBakiyesi":0}}
        """;

    [Fact]
    public async Task Ana_sayfa_panel_esik_ve_takip_ozetini_tek_istekte_ve_gun_parametresiyle_okur()
    {
        var h = new Kayitci((_, _) => Json(AnaSayfaJson));

        var a = await Client(h).AnaSayfaAsync(7);

        Assert.Equal(new[] { "/api/rapor/ana-sayfa?gun=7" }, h.Istekler);
        Assert.Equal(900, a.Panel.GuncelKasa); Assert.Equal(2, a.Panel.Kanallar[1].KanalId); Assert.Equal(7, a.Panel.DagilimBekleyenTutar);
        var esik = Assert.Single(a.KasaEsikleri!); Assert.True(esik.EsikAltinda); Assert.Equal(600, esik.Bakiye);
        Assert.Equal(120, a.TakipOzeti!.KartBorcu); Assert.Equal(1, a.TakipOzeti.KanalKartBorclari!.Single().KanalId);
    }

    [Fact]
    public async Task Eski_sunucuda_uc_yoksa_panele_geri_duser_ve_sonraki_yuklemede_yeniden_denemez()
    {
        var h = new Kayitci((istek, _) => istek.RequestUri!.AbsolutePath == "/api/rapor/panel" ? Json(PanelJson) : Durum(HttpStatusCode.NotFound));
        var c = Client(h);

        var ilk = await c.AnaSayfaAsync();
        var ikinci = await c.AnaSayfaAsync();

        Assert.Equal(new[] { "/api/rapor/ana-sayfa?gun=30", "/api/rapor/panel", "/api/rapor/panel" }, h.Istekler);
        Assert.Equal(900, ilk.Panel.GuncelKasa); Assert.Equal(900, ikinci.Panel.GuncelKasa);
        // Eşikler ve takip özeti eski uçlardan ayrıca yüklenir (eski davranış); boş liste uydurulmaz.
        Assert.Null(ilk.KasaEsikleri); Assert.Null(ilk.TakipOzeti);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Ana_sayfa_sunucu_hatasi_eski_uca_dusmeden_hata_olarak_kalir(HttpStatusCode kod)
    {
        var h = new Kayitci((_, _) => Durum(kod));
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => Client(h).AnaSayfaAsync());
        Assert.Equal(kod, hata.DurumKodu);
        Assert.Equal(new[] { "/api/rapor/ana-sayfa?gun=30" }, h.Istekler);
    }

    [Fact]
    public async Task Haftalik_veri_sagligi_uyarisi_okunur_yoksa_null_kalir()
    {
        var h = new Kayitci((_, _) => Json("""
            [{"donem":{"start":"2027-09-20","end":"2027-09-26","yil":2027,"ay":9},"kanallar":[],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":900},
             {"donem":{"start":"2027-09-27","end":"2027-09-30","yil":2027,"ay":9},"kanallar":[],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":900,
              "veriSagligiUyarisi":"Rapor ufkunun (30.09.2027) ötesinde 1 kayıt var; en geç 22.06.2206."}]
            """));

        var liste = await Client(h).HaftalikAsync(CancellationToken.None);

        Assert.Null(liste[0].VeriSagligiUyarisi);
        Assert.Contains("22.06.2206", liste[1].VeriSagligiUyarisi);
    }

    [Fact]
    public async Task Rapor_okumasi_cagiranin_iptaliyle_durur_zaman_asimi_sayilmaz()
    {
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = new Kayitci(async (_, ct) => { basladi.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); });
        using var iptal = new CancellationTokenSource();

        var ana = Client(h).AnaSayfaAsync(30, iptal.Token);
        await basladi.Task; iptal.Cancel();

        var hata = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ana);
        Assert.IsNotType<TimeoutException>(hata.InnerException);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(h).HaftalikAsync(iptal.Token));
    }
}
