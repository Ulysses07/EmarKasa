namespace Kasa.App.Core.Tests;

/// <summary>Oturumlu ekranların ortak yüklemesi (OturumluViewModel.VeriYukleAsync): hata son başarılı veriyi silmez, veri eski
/// işaretlenir ve gövde görünür kalır; bağlantı kopukken bağlantı hatası sayfaya yazılmaz.</summary>
public class SonVeriTests
{
    private sealed class YukleyenVm(AuthViewModel auth) : OturumluViewModel(auth)
    {
        public List<string> Veri { get; } = [];
        public Task YukleAsync(Func<Task<string>> getir) => VeriYukleAsync(async n =>
        {
            var v = await getir();
            if (!Gecerli(n))
                return;
            Veri.Clear();
            Veri.Add(v);
            Tamamlandi();
        });
        public void HazirDegil() => VeriHazir = false;
        protected override void OturumTemizle() => Veri.Clear();
    }

    [Fact]
    public async Task Ilk_yukleme_hatasinda_govde_gizli_sonraki_hatada_son_veri_eski_isaretlenir()
    {
        var vm = new YukleyenVm(TestOturumu.Ac());

        await vm.YukleAsync(() => throw new HttpRequestException());
        Assert.False(vm.GovdeGorunur);
        Assert.False(vm.VeriEski);
        Assert.Equal("Henüz yüklenmedi.", vm.SonGuncellemeMetni);
        Assert.Contains("Sunucuya ulaşılamadı", vm.Hata);

        await vm.YukleAsync(() => Task.FromResult("kartlar"));
        Assert.True(vm.GovdeGorunur);
        Assert.Null(vm.Hata);

        vm.HazirDegil();   // takip ekranları yüklemeye başlarken VeriHazir'ı indirir
        await vm.YukleAsync(() => throw new HttpRequestException());
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.Equal(["kartlar"], vm.Veri);
        Assert.StartsWith("Son güncelleme: ", vm.SonGuncellemeMetni);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);

        await vm.YukleAsync(() => Task.FromResult("yeni"));
        Assert.False(vm.VeriEski);
    }

    [Fact]
    public async Task Kopukken_yuklemenin_baglanti_hatasi_yazilmaz_oturum_degisince_son_veri_kalkar()
    {
        var auth = TestOturumu.Ac();
        var vm = new YukleyenVm(auth);
        await vm.YukleAsync(() => Task.FromResult("kartlar"));
        auth.Baglanti.Ulasilamadi();

        await vm.YukleAsync(() => throw new TimeoutException());

        Assert.Null(vm.Hata);
        Assert.True(vm.VeriEski);

        TestOturumu.YeniOturum(auth, Rol.Editor);
        Assert.False(vm.GovdeGorunur);
        Assert.False(vm.VeriEski);
        Assert.Empty(vm.Veri);
    }
}
