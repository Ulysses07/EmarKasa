using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kodla yazılmış takip sayfalarının "Yenile / tekrar dene" düğmesi (görev 14-19 incelemesi): Kartlar, Çekler,
/// Krediler ve Aylık giderler yenilemede izlenen formu artık korur (son veri koruma), bu yüzden düğme onay sormaz.</summary>
public class TakipSayfasiYenileTests
{
    /// <summary>IKaydedilmemisForm'u sahte kirlilikle taşıyan en küçük model: TakipSayfasi'nin "Yenile" düğmesinin
    /// onay akışını (BirakmaOnayi) tetikleyip tetiklemediğini ölçer.</summary>
    private sealed class DirtyVm(AuthViewModel auth) : OturumluViewModel(auth), IKaydedilmemisForm
    {
        public bool KaydedilmemisDegisiklikVar { get; set; }
        public void DegisiklikleriBirak() => KaydedilmemisDegisiklikVar = false;
        protected override void OturumTemizle() { }
    }

    private sealed class DenemeSayfasi(DirtyVm vm, Func<Task> yukle) : TakipSayfasi<DirtyVm>(vm, "Deneme", "Açıklama", yukle);

    [Fact]
    public void Yenile_kirli_formu_koruyarak_yukler_onay_sormaz()
    {
        GorunumOrtami.Kur();
        var vm = new DirtyVm(new AuthViewModel(new SahteApi())) { KaydedilmemisDegisiklikVar = true };
        var sorulan = 0;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(true); };
        var yukleSayisi = 0;
        var sayfa = new DenemeSayfasi(vm, () => { yukleSayisi++; return Task.CompletedTask; });
        var dugme = sayfa.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Yenile / tekrar dene");

        ((IButtonController)dugme).SendClicked();

        Assert.Equal(1, yukleSayisi);
        Assert.Equal(0, sorulan);
        Assert.True(vm.KaydedilmemisDegisiklikVar);   // form korunur: Bırak çağrılmadı, kirlilik düşmedi.
    }
}
