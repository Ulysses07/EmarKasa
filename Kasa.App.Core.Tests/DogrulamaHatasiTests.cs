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

    /// <summary>İstemci sunucu hatası üretmez: KasaApiException yalnız ApiClient'ta sunucu yanıtından oluşur. App.Core ya da MAUI
    /// kodu yerel ret için onu (sahte 400) kurarsa sunucu reddiyle ayırt edilemez; yerel ret <see cref="DogrulamaHatasi"/>'dır.</summary>
    [Fact]
    public void Uygulama_kodu_sahte_sunucu_hatasi_uretmez()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "Kasa.slnx")))
            kok = kok.Parent;
        Assert.NotNull(kok);
        var dosyalar = new[] { "Kasa.App.Core", "Kasa.App" }
            .SelectMany(p => Directory.GetFiles(Path.Combine(kok.FullName, p), "*.cs", SearchOption.AllDirectories))
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(dosyalar.Count > 50, $"Kaynak dosyaları okunamadı ({dosyalar.Count}).");
        var kuran = dosyalar.Where(d => File.ReadAllText(d).Contains("new KasaApiException(")).Select(Path.GetFileName).ToList();
        Assert.True(kuran.Count == 0, "KasaApiException kuran uygulama dosyaları: " + string.Join(", ", kuran));
        Assert.Contains(dosyalar, d => File.ReadAllText(d).Contains("throw new DogrulamaHatasi("));
    }

    [Fact]
    public void Gecersiz_tutar_dogrulamasi_yerel_hata_verir_gecerli_tutarlar_gecer()
    {
        var hata = Assert.Throws<DogrulamaHatasi>(() => ParaAyristirici.Dogrula(10m, ParaAyristirici.Gecersiz));
        Assert.Equal(ParaAyristirici.GecersizMesaji, hata.Message);
        ParaAyristirici.Dogrula(10m, 0m, -5m);
    }
}
