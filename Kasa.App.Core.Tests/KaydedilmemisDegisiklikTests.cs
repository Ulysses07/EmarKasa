using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core.Tests;

/// <summary>Kaydedilmemiş değişiklik ölçütü (form açıldığı andaki değerlerden farklı) ve bırakma onayı.</summary>
public partial class KaydedilmemisDegisiklikTests
{
    private sealed partial class FormVm : OturumluViewModel
    {
        public FormVm() : base(TestOturumu.Ac()) => Degisiklik = new(() => new { Ad, Tutar });
        public KaydedilmemisDegisiklik Degisiklik { get; }
        [ObservableProperty] private string _ad = "";
        [ObservableProperty] private decimal _tutar;
        public Task<bool> BirakilabilirMi() => BirakilabilirAsync(Degisiklik);
        protected override void OturumTemizle() { }
    }

    [Fact]
    public void Acilmamis_form_degisiklik_saymaz_acilinca_farki_gorur_geri_donunce_kalkar()
    {
        var vm = new FormVm();
        vm.Ad = "Ege";
        Assert.False(vm.Degisiklik.Var);

        vm.Degisiklik.Ac();
        Assert.False(vm.Degisiklik.Var);
        vm.Tutar = 10m;
        Assert.True(vm.Degisiklik.Var);
        vm.Tutar = 0m;
        Assert.False(vm.Degisiklik.Var);

        vm.Ad = "Başka";
        vm.Degisiklik.Kapat();
        Assert.False(vm.Degisiklik.Var);
        Assert.False(vm.Degisiklik.Acik);
    }

    [Fact]
    public async Task Degisiklik_varken_onay_sorulur_onaysiz_model_birakmaz()
    {
        var vm = new FormVm();
        vm.Degisiklik.Ac();
        Assert.True(await vm.BirakilabilirMi());

        vm.Ad = "Yazıldı";
        Assert.False(await vm.BirakilabilirMi());   // onay bağlı değil: form korunur

        var sorulan = new List<string>();
        vm.BirakmaOnayi = ileti => { sorulan.Add(ileti); return Task.FromResult(false); };
        Assert.False(await vm.BirakilabilirMi());
        vm.BirakmaOnayi = ileti => { sorulan.Add(ileti); return Task.FromResult(true); };
        Assert.True(await vm.BirakilabilirMi());
        Assert.Equal([KaydedilmemisDegisiklik.Ileti, KaydedilmemisDegisiklik.Ileti], sorulan);
        Assert.Equal("Kaydedilmemiş değişiklik var. Bırakılsın mı?", KaydedilmemisDegisiklik.Ileti);
    }
}
