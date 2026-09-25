using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage
{
    private readonly IslemlerViewModel _vm;
    private readonly AuthViewModel _auth;

    public IslemlerPage(IslemlerViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
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
        if (sender is Button b) b.IsVisible = b.BindingContext is IslemDto { EkstreKayitId: not null };
    }
    private async void KaynakTiklandi(object? sender, EventArgs e)
    {
        if (_auth.AktifRol == Rol.Editor && sender is Button { CommandParameter: IslemDto { EkstreKayitId: { } id } })
            await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
    }
}
