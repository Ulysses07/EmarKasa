using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KredilerViewModelTests
{
    [Fact]
    public async Task Yukle_kredileri_doldurur()
    {
        var api = new SahteApi
        {
            KredilerListe = new List<KrediDto>
            {
                new(1, "Konut", 500000m, new DateOnly(2026, 1, 10), 120, 6000m, 10, "Ortak"),
            },
        };
        var vm = new KredilerViewModel(api);

        await vm.YukleAsync();

        Assert.Single(vm.Krediler);
        Assert.Equal("Konut", vm.Krediler[0].Ad);
        Assert.Equal(720000m, vm.Krediler[0].Toplam);   // aylık × taksit
    }

    [Fact]
    public async Task Yukle_bos_liste_bos_koleksiyon()
    {
        var vm = new KredilerViewModel(new SahteApi());
        await vm.YukleAsync();
        Assert.Empty(vm.Krediler);
    }

    [Fact]
    public async Task Yukle_kanal_secenekleri_aktif_kanallar_plus_ortak()
    {
        var api = new SahteApi
        {
            KanallarListe = new List<KanalDto>
            {
                new(1, "B Kanal", true, 2, 0m),
                new(2, "A Kanal", true, 1, 0m),
                new(3, "Pasif", false, 3, 0m),
            },
        };
        var vm = new KredilerViewModel(api);

        await vm.YukleAsync();

        // Sira'ya göre sıralı aktifler + sonda "Ortak"; pasif elenmiş.
        Assert.Equal(new[] { "A Kanal", "B Kanal", "Ortak" }, vm.KanalSecenekleri);
    }

    [Fact]
    public async Task Kaydet_yeni_kredi_ekler()
    {
        var api = new SahteApi();
        var vm = new KredilerViewModel(api)
        {
            DuzenId = 0,
            DuzenAd = "Taşıt",
            DuzenCekilenTutar = 300000m,
            DuzenCekimTarihi = new DateTime(2026, 3, 5),
            DuzenTaksitSayisi = 36,
            DuzenAylikOdeme = 9500m,
            DuzenOdemeGunu = 15,
            DuzenKanal = "Ortak",
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKrediEkle);
        Assert.Equal("Taşıt", api.SonKrediEkle!.Ad);
        Assert.Equal(300000m, api.SonKrediEkle.CekilenTutar);
        Assert.Equal(new DateOnly(2026, 3, 5), api.SonKrediEkle.CekimTarihi);
        Assert.Equal(36, api.SonKrediEkle.TaksitSayisi);
        Assert.Equal(9500m, api.SonKrediEkle.AylikOdeme);
        Assert.Equal(15, api.SonKrediEkle.OdemeGunu);
        Assert.Equal("Ortak", api.SonKrediEkle.Kanal);
        Assert.Null(api.SonKrediGuncelle);
    }

    [Fact]
    public async Task Duzenle_sonra_kaydet_gunceller()
    {
        var api = new SahteApi();
        var vm = new KredilerViewModel(api);
        var satir = new KrediGorunum(new KrediDto(7, "Konut", 500000m, new DateOnly(2026, 1, 10), 120, 6000m, 10, "Ortak"));

        vm.Duzenle(satir);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKrediGuncelle);
        Assert.Equal(7, api.SonKrediGuncelle!.Value.Id);
        Assert.Equal("Konut", api.SonKrediGuncelle.Value.K.Ad);
        Assert.Null(api.SonKrediEkle);
    }

    [Fact]
    public async Task Sil_satiri_id_ile_siler()
    {
        var api = new SahteApi();
        var vm = new KredilerViewModel(api);
        var satir = new KrediGorunum(new KrediDto(9, "Konut", 500000m, new DateOnly(2026, 1, 10), 120, 6000m, 10, "Ortak"));

        await vm.SilCommand.ExecuteAsync(satir);

        Assert.Equal(9, api.SonKrediSil);
    }
}
