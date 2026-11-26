using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class PanelViewModelTests
{
    [Fact]
    public async Task Yukle_panel_alanlarini_doldurur()
    {
        var api = new SahteApi
        {
            Panel = new PanelDto(90000m, new List<KanalBakiyeDto> { new("MEZAT", 150.5m) }, 10m, -5m),
        };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(90000m, vm.GuncelKasa);
        Assert.Single(vm.Kanallar);
        Assert.Equal("MEZAT", vm.Kanallar[0].Kanal);
    }
    /// <summary>Eksi genel kasa özet kutusunda ayrı renkle işaretlenir (docs/specs/2026-11-26-slop-temizligi.md §4).</summary>
    [Fact]
    public async Task Eksi_genel_kasa_isaretlenir()
    {
        var api = new SahteApi { Panel = new PanelDto(-60005.35m, new List<KanalBakiyeDto>(), 0m, 0m) };
        var vm = new PanelViewModel(api);
        var bildirildi = false;
        vm.PropertyChanged += (_, e) => bildirildi |= e.PropertyName == nameof(vm.GuncelKasaEksi);
        await vm.YukleAsync();
        Assert.True(vm.GuncelKasaEksi);
        Assert.True(bildirildi);
        api.Panel = new PanelDto(0m, new List<KanalBakiyeDto>(), 0m, 0m);
        await vm.YukleAsync();
        Assert.False(vm.GuncelKasaEksi);
    }
    [Fact]
    public async Task Kart_borcu_kimlikle_eslenir_mevcut_kasa_ve_belirsiz_pay_degismez()
    {
        var api = new SahteApi { Panel = new(900, new[] { new KanalBakiyeDto("ESKİ AD", 300, 1), new KanalBakiyeDto("MEZAT", 600, 2) }, 0, 0) };
        var vm = new PanelViewModel(api);
        await vm.YukleAsync();
        vm.KartBorclariniYansit(new[] { new TakipKanalPayi(1, "YENİ AD", 100), new TakipKanalPayi(2, "MEZAT", 200), new TakipKanalPayi(null, "Dağılım bekliyor", 50) });
        Assert.Equal(100, vm.Kanallar[0].KartBorcu);
        Assert.Equal(200, vm.Kanallar[1].KartBorcu);
        Assert.Equal(900, vm.GuncelKasa);
        Assert.Equal(300, vm.Kanallar[0].Bakiye);
        Assert.Equal(600, vm.Kanallar[1].Bakiye);
        vm.KartBorclariniYansit(null);
        Assert.All(vm.Kanallar, k => Assert.Null(k.KartBorcu));
    }

    /// <summary>Takipte olmayan (geçişi yapılmamış) kart ve krediler ana sayfada kalıcı uyarıyla gösterilir
    /// (gap-tarihsel-spec-ve-emekli-web-7); kayıt kalmayınca ya da eski sunucuda (alan yok) uyarı kalkar.</summary>
    [Fact]
    public async Task Takipte_olmayan_kayitlar_kalici_uyari_olarak_gosterilir_kalmayinca_kalkar()
    {
        var api = new SahteApi { Panel = new(900, new[] { new KanalBakiyeDto("MEZAT", 900, 1) }, 0, 0) };
        IReadOnlyList<TakipsizKayitDto>? kayitlar = [new("Kart", 4, "Bonus"), new("Kredi", 7, "Taşıt")];
        api.AnaSayfaGetir = (_, _) => Task.FromResult(new AnaSayfaDto(api.Panel!, null, null, kayitlar));
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();
        Assert.True(vm.TakipsizVar);
        Assert.Equal("Kart ve kredi takibinde olmayan kayıtlar var: Bonus (kart), Taşıt (kredi). Bu kayıtların hatırlatmaları eski kayıtlardan hesaplanır ve sınırlıdır; yeni ödeme ve güncel ekstre görünmez. Kartlar ve Krediler ekranından geçiş yapın.", vm.TakipsizUyari);

        kayitlar = null;
        await vm.YukleAsync();
        Assert.False(vm.TakipsizVar);
        Assert.Equal("", vm.TakipsizUyari);
    }
}
