using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KrediKartOdemeTests
{
    [Fact]
    public async Task Yukle_turetilmis_guncel_borc_ve_kirilim_doldurur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new List<KrediKartiDto>
            {
                new(1, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 1000m,
                    GuncelBorc: 1300m, AcilisBorc: 1000m, HarcamaToplam: 500m, OdemeToplam: 200m),
            },
        };
        var vm = new KrediKartlariViewModel(api);

        await vm.YukleAsync();

        var k = vm.Kartlar.Single();
        Assert.Equal(1300m, k.GuncelBorc);
        Assert.Equal(98700m, k.KalanLimit);       // limit - güncel borç
        Assert.Contains("Açılış", k.BorcKirilim);
        Assert.Contains("+", k.BorcKirilim);
    }

    [Fact]
    public async Task Yukle_odeme_gecmisini_karta_doldurur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new List<KrediKartiDto>
            {
                new(5, "World", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 50000m, 0m),
            },
            KartOdemelerListe = new List<KartOdemeDto>
            {
                new(1, 5, new DateOnly(2026,7,20), 750m, "temmuz"),
            },
        };
        var vm = new KrediKartlariViewModel(api);

        await vm.YukleAsync();

        Assert.Single(vm.Kartlar.Single().Odemeler);
        Assert.Equal(750m, vm.Kartlar.Single().Odemeler[0].Tutar);
    }

    [Fact]
    public async Task Odeme_ekle_dogru_argumanla_kaydeder()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new List<KrediKartiDto>
            {
                new(5, "World", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 50000m, 0m),
            },
        };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();
        vm.DuzenOdemeTarih = new DateTime(2026, 7, 20);
        vm.DuzenOdemeTutar = 750m;

        await vm.OdemeEkleCommand.ExecuteAsync(vm.Kartlar.Single());

        Assert.NotNull(api.SonKartOdemeKaydet);
        Assert.Equal(5, api.SonKartOdemeKaydet!.KrediKartiId);
        Assert.Equal(750m, api.SonKartOdemeKaydet!.Tutar);
        Assert.Equal(new DateOnly(2026, 7, 20), api.SonKartOdemeKaydet!.Tarih);
    }

    [Fact]
    public async Task Odeme_sil_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);

        await vm.OdemeSilCommand.ExecuteAsync(new KartOdemeDto(8, 5, new DateOnly(2026,7,20), 750m, null));

        Assert.Equal(8, api.SonKartOdemeSil);
    }

    [Fact]
    public void Duzenle_acilis_borcunu_forma_alir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);

        vm.Duzenle(new KrediKartiGorunum(new KrediKartiDto(3, "World", new DateOnly(2026,7,1),
            new DateOnly(2026,7,20), 50000m, 1000m, GuncelBorc: 1300m, AcilisBorc: 1000m)));

        Assert.Equal(1000m, vm.DuzenBorc);
    }
}
