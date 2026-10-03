using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly IslemlerViewModel _vm;

    // Rol ve oturum modelden gelir (OturumluViewModel): sayfa rolü ekrana atamaz.
    public IslemlerPage(IslemlerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var gorunur = new Controls.GorunurYapici(FormKaydirici);
        vm.Hatalar.GosterIstendi += (_, _) => gorunur.HatayaGit(IslemFormu, vm.Hatalar, FormHataKutusu);
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }

    private async void SilTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: IslemDto islem } dugme)
            await SilmeOnayi.GosterAsync(this, dugme, $"{islem.Tarih:dd.MM.yyyy} · {islem.Cari} · {Bicim.Tl(islem.TutarTl)} ₺",
                () => _vm.SilCommand.ExecuteAsync(islem));
    }
    private void KaynakBaglamiDegisti(object? sender, EventArgs e)
    {
        if (sender is Button b)
            b.IsVisible = b.BindingContext is IslemDto { EkstreKayitId: not null };
    }
    private async void KaynakTiklandi(object? sender, EventArgs e)
    {
        if (_vm.EditorMu && sender is Button { CommandParameter: IslemDto { EkstreKayitId: { } id } })
            await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
    }
}
