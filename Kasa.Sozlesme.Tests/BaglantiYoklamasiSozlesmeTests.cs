using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Bağlantı yoklaması (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3): kopukken istemci gerçek sunucunun
/// sağlık ucunu oturumsuz yoklar; başarılı yoklama ulaşılabilirliği bildirir.</summary>
public class BaglantiYoklamasiSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IBaglantiYoklamasi.YoklaAsync))]
    public async Task Yoklama_oturumsuz_saglik_ucuna_ulasir_ve_ulasilabilirligi_bildirir()
    {
        var o = Istemci();
        var ulasildi = 0;
        o.Istemci.SunucuyaUlasildi += (_, _) => ulasildi++;
        await o.Yoklama.YoklaAsync(TestContext.Current.CancellationToken);
        Assert.True(ulasildi >= 1);
    }
}
