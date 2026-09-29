using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// İstek kimliği (sunucu tekrar koruması, IstekId) sözleşmesi: aynı gövdenin yeniden gönderimi aynı kimliği taşır (yanıtı
/// kaybolan istek sunucuda ikinci kez işlenmez); gövde değişince yeni kimlik alınır; başarıdan, sunucu reddinden (400/409) ve
/// form sıfırlanmasından sonra kimlik yenilenir. Yalnız son gövde tutulur: önceki gövdeye dönmek önceki kimliği geri getirmez.
/// </summary>
public class TekrarAnahtariTests
{
    [Fact]
    public void Ayni_govde_ayni_kimlik_degisen_govde_yeni_kimlik_temizlenince_yeni_kimlik()
    {
        var anahtar = new TekrarAnahtari();
        var ilk = anahtar.Al(new { Id = 1, Tutar = 10m });
        Assert.NotEqual(Guid.Empty, ilk);
        Assert.Equal(ilk, anahtar.Al(new { Id = 1, Tutar = 10m }));

        var ikinci = anahtar.Al(new { Id = 1, Tutar = 11m });
        Assert.NotEqual(ilk, ikinci);
        var ucuncu = anahtar.Al(new { Id = 1, Tutar = 10m });
        Assert.NotEqual(ilk, ucuncu);
        Assert.NotEqual(ikinci, ucuncu);

        anahtar.Temizle();
        Assert.NotEqual(ucuncu, anahtar.Al(new { Id = 1, Tutar = 10m }));
    }

    [Fact]
    public void Kayit_basina_anahtar_baska_kaydin_istegiyle_ezilmez()
    {
        var anahtar = new KayitBasinaTekrarAnahtari();
        var bir = anahtar.Al(1, new { Id = 1, Tutar = 10m });
        var iki = anahtar.Al(2, new { Id = 2, Tutar = 10m });
        Assert.NotEqual(bir, iki);
        Assert.Equal(bir, anahtar.Al(1, new { Id = 1, Tutar = 10m }));

        anahtar.Temizle(1);
        Assert.NotEqual(bir, anahtar.Al(1, new { Id = 1, Tutar = 10m }));
        Assert.Equal(iki, anahtar.Al(2, new { Id = 2, Tutar = 10m }));

        anahtar.Temizle();
        Assert.NotEqual(iki, anahtar.Al(2, new { Id = 2, Tutar = 10m }));
    }

    // ---- Alış ödemesi ----

    private static AlisDto Alis(int id) => new(
        id, 2, 3, "Ayşe", new(2026, 9, 21), "Tedarikçi", null, "Taslak", null, 100m, 0m, 100m,
        [new AlisKalemDto(1, "Mal alımı", 100m, [new AlisDagilimDto(1, "MEZAT", 100m)])],
        []);

    private static async Task<(AlislarViewModel Vm, AlislarViewModelTests.SahteAlisApi Api)> AlisOdemesi()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [Alis(7), Alis(8)] };
        var vm = new AlislarViewModel(api, new SahteApi(), auth: new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        Sec(vm, 7);
        return (vm, api);
    }

    private static void Sec(AlislarViewModel vm, int id) => vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == id));

    [Fact]
    public async Task Alis_odemesi_yanit_kaybolunca_ayni_govdeyle_ayni_kimlik_degisen_govdede_yeni_kimlik()
    {
        var (vm, api) = await AlisOdemesi();
        api.OdemeHatasi = true;
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count);
        Assert.NotEqual(Guid.Empty, api.OdemeIstekleri[0].IstekId);
        Assert.Equal(api.OdemeIstekleri[0], api.OdemeIstekleri[1]);

        api.OdemeHatasi = false;
        api.OdemeIstisnasi = new TimeoutException(KasaZamanAsimlari.Ileti);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(api.OdemeIstekleri[0], api.OdemeIstekleri[2]);

        vm.OdemeTutari = 25m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.NotEqual(api.OdemeIstekleri[0].IstekId, api.OdemeIstekleri[3].IstekId);
        Assert.Equal(25m, api.OdemeIstekleri[3].Tutar);

        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.DoesNotContain(api.OdemeIstekleri[4].IstekId, api.OdemeIstekleri.Take(4).Select(o => o.IstekId));

        // Yanıtı kaybolan istek sonunda başarıyla yeniden gönderilir: aynı kimlikle.
        api.OdemeIstisnasi = null;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(api.OdemeIstekleri[4], api.OdemeIstekleri[5]);
        Assert.Null(vm.Hata);
        Assert.Equal(30m, vm.Odenen);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Alis_odemesi_sunucu_reddinden_sonra_ayni_govde_yeni_kimlik_alir(HttpStatusCode durum)
    {
        var (vm, api) = await AlisOdemesi();
        api.OdemeIstisnasi = new KasaApiException(durum, "Alış arada değişti.");
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Alış arada değişti.", vm.Hata);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count);
        Assert.NotEqual(api.OdemeIstekleri[0].IstekId, api.OdemeIstekleri[1].IstekId);
        Assert.Equal(api.OdemeIstekleri[0] with { IstekId = Guid.Empty }, api.OdemeIstekleri[1] with { IstekId = Guid.Empty });
    }

    [Fact]
    public async Task Alis_odemesi_basaridan_sonra_ayni_tutarla_yeni_kimlik_alir()
    {
        var (vm, api) = await AlisOdemesi();
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0m, vm.OdemeTutari);
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count);
        Assert.NotEqual(api.OdemeIstekleri[0].IstekId, api.OdemeIstekleri[1].IstekId);
    }

    [Fact]
    public async Task Alis_odemesi_baska_alisa_gecilince_yeni_kimlik_alir()
    {
        var (vm, api) = await AlisOdemesi();
        api.OdemeHatasi = true;
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);

        Sec(vm, 8);
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Sec(vm, 7);
        vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);

        Assert.Equal(3, api.OdemeIstekleri.Count);
        Assert.Equal(3, api.OdemeIstekleri.Select(o => o.IstekId).Distinct().Count());
    }
}
