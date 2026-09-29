using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Yerel girdi doğrulaması ayrı türdür (<see cref="DogrulamaHatasi"/>): istek gönderilmeden verilir, kullanıcı
/// iletiyi eskisi gibi olduğu gibi görür; sunucunun 400 reddiyle (<see cref="KasaApiException"/>) karışmaz. Ekran düzeyindeki
/// davranış (istek gitmez, ileti Hata'da) GecersizTutarTests, FinansTakipTests ve EkstreAktarmaTests'te de sınanır.</summary>
public class DogrulamaHatasiTests
{
    private sealed class HataOkuyucu : TemelViewModel { public static string Oku(Exception e) => HataMesaji(e); }

    [Fact]
    public void Yerel_dogrulama_iletisi_oldugu_gibi_gosterilir_ve_sunucu_reddinden_ayrilir()
    {
        const string ileti = "Aynı kanalı iki kez seçmeyin.";
        var yerel = new DogrulamaHatasi(ileti);
        var sunucu = new KasaApiException(HttpStatusCode.BadRequest, ileti);

        // Kullanıcının gördüğü ileti değişmedi: yerel ret, eskiden taklit ettiği sunucu 400'üyle aynı metni verir.
        Assert.Equal(ileti, HataOkuyucu.Oku(yerel));
        Assert.Equal(HataOkuyucu.Oku(sunucu), HataOkuyucu.Oku(yerel));
        Assert.Equal(ileti, Yurutucu.OkumaHataMesaji(yerel));
        // Sunucu reddini (400) ayıran süzgeç yerel doğrulamayı yakalamaz.
        Assert.IsNotAssignableFrom<KasaApiException>(yerel);
        static bool SunucuReddi(Exception e) => e is KasaApiException { DurumKodu: HttpStatusCode.BadRequest };
        Assert.False(SunucuReddi(yerel));
        Assert.True(SunucuReddi(sunucu));
    }

    [Fact]
    public void Gecersiz_tutar_dogrulamasi_yerel_hata_verir_gecerli_tutarlar_gecer()
    {
        var hata = Assert.Throws<DogrulamaHatasi>(() => ParaAyristirici.Dogrula(10m, ParaAyristirici.Gecersiz));
        Assert.Equal(ParaAyristirici.GecersizMesaji, hata.Message);
        ParaAyristirici.Dogrula(10m, 0m, -5m);
    }
}
