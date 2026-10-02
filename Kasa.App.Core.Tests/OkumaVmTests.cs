using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class OkumaVmTests
{
    [Fact]
    public async Task Haftalik_donemleri_yukler()
    {
        var api = new SahteApi
        {
            HaftalikListe = new List<HaftalikOzetDto>
            {
                new(new DonemDto(new DateOnly(2026,3,2), new DateOnly(2026,3,8), 2026, 3),
                    new List<KanalHaftalikDto>(), 0, 0, 100m, 100m),
            },
        };
        var vm = new HaftalikViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Donemler);
        Assert.Equal(100m, vm.Donemler[0].KasaSonucu);
    }

    /// <summary>HF-01: sunucu eskiden yeniye döner; içinde bulunulan hafta her seferinde sona kaydırmadan görünsün diye
    /// istemci yeniden eskiye sıralar.</summary>
    [Fact]
    public async Task Haftalik_donemler_en_yeniden_eskiye_siralanir()
    {
        var api = new SahteApi
        {
            HaftalikListe = new List<HaftalikOzetDto>
            {
                new(new DonemDto(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), 2026, 8), new List<KanalHaftalikDto>(), 0, 0, 10m, 0m),
                new(new DonemDto(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), 2026, 9), new List<KanalHaftalikDto>(), 0, 0, 30m, 0m),
                new(new DonemDto(new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 30), 2026, 8), new List<KanalHaftalikDto>(), 0, 0, 20m, 0m),
            },
        };
        var vm = new HaftalikViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(new[] { 30m, 20m, 10m }, vm.Donemler.Select(d => d.KasaSonucu));
    }

    [Fact]
    public async Task Aylik_secilen_yil_ayi_ister()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 4, new List<KanalAylikDto>()) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 4 };
        await vm.YukleAsync();
        Assert.Equal(2026, api.SonAylikYil);
        Assert.Equal(4, api.SonAylikAy);
        Assert.NotNull(vm.Rapor);
    }


    [Fact]
    public async Task Islemler_listeyi_yukler()
    {
        var api = new SahteApi
        {
            IslemlerListe = new List<IslemDto>
            {
                new(1, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null),
            },
        };
        var vm = new IslemlerViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        Assert.Single(vm.Islemler);
        Assert.Equal(GiderTipi.KrediKarti, vm.Islemler[0].Tip);
    }
}
