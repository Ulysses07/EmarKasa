using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ayarlar kanal formu (tasarım 2026-10-02 §1): ad ön doğrulaması, sunucu alan eşlemesi ("ad", "acilisDevri"), eşlenmeyen
/// çakışma iletisi genel hatada, başka kanala geçince hatalar kalkar.</summary>
public class KanalFormuTests
{
    private static async Task<(AyarlarViewModel Vm, SahteApi Api)> Kur()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)] };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_ad_istek_gondermez_alanin_altinda_soylenir()
    {
        var (vm, api) = await Kur();

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonKanalOlustur);
        Assert.Equal("Kanal adı boş olamaz.", vm.KanalHatalari[nameof(vm.DuzenKanalAd)]);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_eslenir_hatalar_baska_kanala_gecince_kalkar()
    {
        var (vm, api) = await Kur();
        vm.KanalDuzenle(vm.Kanallar[0]);
        api.KanalGuncelleHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik",
            alanHatalari: new Dictionary<string, string> { ["ad"] = "Bu ad sistem tarafından kullanılıyor.", ["acilisdevri"] = "Tutar en fazla iki ondalık basamak içerebilir." });
        vm.DuzenKanalAd = "Ortak";

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.Equal("Bu ad sistem tarafından kullanılıyor.", vm.KanalHatalari[nameof(vm.DuzenKanalAd)]);
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", vm.KanalHatalari[nameof(vm.DuzenKanalAcilisDevri)]);
        vm.YeniKanalCommand.Execute(null);
        Assert.False(vm.KanalHatalari.Var);
    }
}
