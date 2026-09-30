using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kart ekranının eski borç devri düzeltmesi (finance-2) ve kilitli avans dağıtımı (finance-8) davranışı: devir açık
/// eylemle okunur ve form doldurulur, gerekçesiz ya da engelli düzeltme gönderilmez, gövde seçili kartın sürümünü, etkin
/// devrin kimliğini ve kanal paylarını taşır; devir satırı ve avans dağıtımı istemcide de iptal edilmez.</summary>
public class KartDevirVmTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 25);
    private static readonly KartHarcamaDto DevirSatiri = new(11, null, Tarih, "Onaylanan eski borç devri", 100m, 1, false, [new TakipKanalPayi(1, "MEZAT", 100m)]);
    private static readonly KartHarcamaDto IadeSatiri = new(12, null, Tarih, "Satıcı iadesi", -30m, 1, false, [new TakipKanalPayi(1, "MEZAT", 30m)], KasadaSayilanDuzeltme: 30m);
    private static readonly KartTakipOdemeDto Dagitim = new(13, Tarih, 0m, 0m, "Kilitli avans dağıtımı", false, [new TakipKanalPayi(1, "MEZAT", 50m), new TakipKanalPayi(null, "", -50m)], AvansKaynakOdemeId: 3);
    private static KartDevirDto Devir(bool duzeltilebilir = true, string? engel = null) =>
        new(11, Tarih, 100m, 80m, 0m, [new TakipKanalPayi(1, "MEZAT", 100m)], "IslemTarihi", 80m, 0m, 0m, 80m, 80m, duzeltilebilir, engel);

    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Vm(KartDevirDto? devir = null)
    {
        var api = new FinansTakipTests.Sahte { Devir = devir ?? Devir() };
        api.Kart = FinansTakipTests.Sahte.OrnekKart() with { Gecis = new KartGecisDto("IslemTarihi", "Banka", null), Harcamalar = [DevirSatiri, IadeSatiri], Odemeler = [Dagitim] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0)] };
        var vm = new KartTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Kartlar[0]);
        return (vm, api);
    }

    [Fact]
    public async Task Devir_okununca_form_dolar_gerekceyle_duzeltme_gonderilir()
    {
        var (vm, api) = await Vm();
        Assert.Null(vm.DevirOzeti);
        Assert.False(vm.DevirDuzeltilebilir);
        await vm.DevirYukleCommand.ExecuteAsync(null);
        Assert.Contains("kasada önceden sayılan 80,00", vm.DevirOzeti);
        Assert.True(vm.DevirDuzeltilebilir);
        Assert.Equal((100m, 80m), (vm.DevirKalanBorc, vm.DevirOncedenSayilan));
        Assert.Equal((1, 100m), (Assert.Single(vm.DevirPaylari).Kanal!.Id, vm.DevirPaylari[0].Tutar));

        vm.DevirKalanBorc = 80m;
        vm.DevirOncedenSayilan = 80m;
        vm.DevirPaylari[0].Tutar = 80m;
        await vm.DevirDuzeltCommand.ExecuteAsync(null);
        Assert.Empty(api.DevirDuzeltmeleri);
        Assert.Contains("gerekçesini", vm.Hata);

        vm.DevirAciklama = "  Banka ekstresine göre  ";
        await vm.DevirDuzeltCommand.ExecuteAsync(null);
        var g = Assert.Single(api.DevirDuzeltmeleri);
        Assert.Equal((3, (int?)11, 80m, 80m, "Banka ekstresine göre"), (g.Surum, g.HarcamaId, g.KalanBorc, g.KasadaOncedenSayilanTutar, g.Aciklama));
        Assert.Equal([new KanalPayYaz(1, 80m)], g.Dagilimlar);
        Assert.NotEqual(Guid.Empty, g.IstekId);
        Assert.Null(vm.Devir);
        Assert.Contains("düzeltildi", vm.Mesaj);
    }

    [Fact]
    public async Task Engelli_devir_duzeltilmez_devir_ve_avans_dagitimi_istemcide_iptal_edilmez()
    {
        var (vm, api) = await Vm(Devir(false, "Devre ödeme kaydedilmiş."));
        await vm.DevirYukleCommand.ExecuteAsync(null);
        Assert.False(vm.DevirDuzeltilebilir);
        Assert.Contains("Düzeltilemez: Devre ödeme kaydedilmiş.", vm.DevirOzeti);
        vm.DevirAciklama = "Deneme";
        await vm.DevirDuzeltCommand.ExecuteAsync(null);
        Assert.Empty(api.DevirDuzeltmeleri);
        Assert.Equal("Devre ödeme kaydedilmiş.", vm.Hata);

        vm.Gerekce = "Yanlış";
        var devirSatiri = vm.Harcamalar.Single(h => h.Veri.Id == 11);
        Assert.True(vm.DevirSatiri(devirSatiri));
        Assert.False(vm.DevirSatiri(vm.Harcamalar.Single(h => h.Veri.Id == 12)));
        await vm.HarcamaIptalAsync(devirSatiri);
        Assert.Contains("düzeltin", vm.Hata);
        var dagitim = Assert.Single(vm.Odemeler);
        Assert.True(dagitim.AvansDagitimi);
        await vm.OdemeIptalAsync(dagitim);
        Assert.Contains("avans dağıtımı ayrıca iptal edilemez", vm.Hata);
    }

    [Fact]
    public void Satirlar_iadenin_kasaya_donen_tutarini_ve_avans_dagitimini_anlatir()
    {
        Assert.Contains("Önceden sayılan 30,00 ₺ iade tarihinde kasaya döndü.", new HarcamaSatiri(IadeSatiri).Ozet);
        Assert.DoesNotContain("kasaya döndü", new HarcamaSatiri(DevirSatiri).Ozet);
        var satir = new KartOdemeSatiri(Dagitim);
        Assert.Contains("kilitli avans dağıtımı", satir.Baslik);
        Assert.Contains("Kasa değişmez", satir.Ozet);
        Assert.Contains("ödeme 10,00", new KartOdemeSatiri(new KartTakipOdemeDto(1, Tarih, 10m, 10m, null, false, [])).Baslik);
    }
}
