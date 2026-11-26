using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Cariler ekranı (docs/specs/2026-11-25-erp12-aktarimi.md): liste, arama, carinin hareketleri ve durum metinleri.</summary>
public class CarilerViewModelTests
{
    private static readonly DateOnly Gun = new(2026, 11, 3);
    private static ErpCariDto Cari(string ad, string? kod = null, decimal borc = 0, decimal alacak = 0, int bekleyen = 0) =>
        new(kod is null ? "A:" + ad : "K:" + kod, kod, ad, borc, alacak, borc - alacak, 2, bekleyen, Gun);
    private static ErpCariHareketDto Hareket(string durum, decimal borc = 0, decimal alacak = 0, string? tur = null, int? id = null) =>
        new(1, 2, 3, Gun, "Havale", "B1", "Kasım", borc, alacak, durum, tur, id);

    internal sealed class Sahte : IErpApi
    {
        public IReadOnlyList<ErpCariDto> Liste = [Cari("AHMET YILMAZ", "120.01", 12_000, 5_000, 1), Cari("İNCİ KIRTASİYE", null, 350)];
        public IReadOnlyList<ErpCariHareketDto> Hareketler = [Hareket(ErpHareketDurumlari.Bekliyor, borc: 10)];
        public List<string> Acilan { get; } = [];
        public Task<IReadOnlyList<ErpCariDto>> ErpCarilerAsync(string? ara = null) => Task.FromResult(Liste);
        public Task<IReadOnlyList<ErpCariHareketDto>> ErpCariHareketleriAsync(string anahtar) { Acilan.Add(anahtar); return Task.FromResult(Hareketler); }
    }

    private static async Task<(CarilerViewModel Vm, Sahte Api, AuthViewModel Auth)> Hazir(Rol rol = Rol.Izleyici)
    {
        var (api, auth) = (new Sahte(), new AuthViewModel(new SahteApi()) { AktifRol = rol });
        var vm = new CarilerViewModel(api, auth);
        await vm.YukleAsync();
        return (vm, api, auth);
    }

    [Fact]
    public async Task Liste_ozet_ve_turkce_arama()
    {
        var (vm, api, _) = await Hazir();
        Assert.Equal(["AHMET YILMAZ · 120.01", "İNCİ KIRTASİYE"], vm.Cariler.Select(c => c.Baslik));
        Assert.Equal("Borç 12.000,00 ₺ · Alacak 5.000,00 ₺ · Bakiye 7.000,00 ₺ borçlu · 2 hareket, son 03.11.2026 · 1 bekliyor", vm.Cariler[0].Ozet);
        Assert.Equal("2 cari · Borç 12.350,00 ₺ · Alacak 5.000,00 ₺ · 1 hareket kasada karşılığını bekliyor", vm.Ozet);
        vm.Ara = "inci";
        Assert.Equal("İNCİ KIRTASİYE", Assert.Single(vm.Cariler).Baslik);
        vm.Ara = "120.01";
        Assert.Equal("AHMET YILMAZ · 120.01", Assert.Single(vm.Cariler).Baslik);
        vm.Ara = "";
        Assert.Equal(2, vm.Cariler.Count);

        api.Liste = [];
        await vm.YukleAsync();
        Assert.StartsWith("Henüz ERP12 hareketi aktarılmadı.", vm.Ozet);
    }

    [Fact]
    public async Task Cari_acilir_yeniden_basinca_kapanir_yenilemede_acik_kalir_oturum_degisince_temizlenir()
    {
        var (vm, api, auth) = await Hazir();
        await vm.SecCommand.ExecuteAsync(vm.Cariler[0]);
        Assert.True(vm.CariAcik);
        Assert.StartsWith("AHMET YILMAZ · 120.01\nBorç", vm.AcikBasligi);
        var h = Assert.Single(vm.Hareketler);
        Assert.Equal(("03.11.2026 · Havale · Belge B1", "Borç 10,00 ₺ · kasada karşılığı bekliyor · Kasım"), (h.Baslik, h.Ozet));
        await vm.YukleAsync();
        Assert.Equal((true, 1), (vm.CariAcik, vm.Hareketler.Count));
        Assert.Equal(["K:120.01", "K:120.01"], api.Acilan);
        await vm.SecCommand.ExecuteAsync(vm.Cariler[0]);
        Assert.Equal((false, 0, ""), (vm.CariAcik, vm.Hareketler.Count, vm.AcikBasligi));

        // Yenilemede artık olmayan cari kapanır.
        await vm.SecCommand.ExecuteAsync(vm.Cariler[1]);
        api.Liste = [api.Liste[0]];
        await vm.YukleAsync();
        Assert.Equal((false, 0), (vm.CariAcik, vm.Hareketler.Count));
        vm.Ara = "ahmet";
        TestOturumu.YeniOturum(auth, Rol.Izleyici);
        Assert.Equal((0, ""), (vm.Cariler.Count, vm.Ara));
    }

    [Fact]
    public void Durum_ve_bakiye_metinleri()
    {
        Assert.Equal(["kasa hareketi değil", "kasada karşılığı bekliyor", "eşleşti (Gider #9)", "kasaya yazıldı", "yok sayıldı", "Yeni"],
            new[]
            {
                Hareket(ErpHareketDurumlari.KasaDisi), Hareket(ErpHareketDurumlari.Bekliyor), Hareket(ErpHareketDurumlari.Eslesti, tur: EslesmeTurleri.Gider, id: 9),
                Hareket(ErpHareketDurumlari.Kaydedildi), Hareket(ErpHareketDurumlari.YokSayildi), Hareket("Yeni"),
            }.Select(CariMetni.Durum));
        Assert.Equal(["Bakiye 0,00 ₺", "Bakiye 5,00 ₺ borçlu", "Bakiye 5,00 ₺ alacaklı"], new[] { 0m, 5m, -5m }.Select(CariMetni.Bakiye));
        Assert.Equal("Alacak 4,00 ₺ · kasaya yazıldı · Kasım", new CariHareketSatiri(Hareket(ErpHareketDurumlari.Kaydedildi, alacak: 4)).Ozet);
    }
}
