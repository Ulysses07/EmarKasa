using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage, Controls.IYenilenebilir, Controls.IKisayolHedefi
{
    /// <summary>Ctrl+N boş forma döner (dar pencerede form bölmesi açılır), Ctrl+S formu kaydeder; yalnız editörde.</summary>
    public System.Windows.Input.ICommand? KisayolKomutu(Controls.Kisayol kisayol) => !_vm.EditorMu ? null : kisayol switch
    {
        Controls.Kisayol.Yeni => Controls.Kisayollar.Komut(async () =>
        {
            await _vm.YeniCommand.ExecuteAsync(null);
            IslemBolmeleri.IlkiAc();
        }),
        Controls.Kisayol.Kaydet => _vm.KaydetCommand,
        _ => null,
    };

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly IslemlerViewModel _vm;

    // Rol ve oturum modelden gelir (OturumluViewModel): sayfa rolü ekrana atamaz.
    public IslemlerPage(IslemlerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var gorunur = new Controls.GorunurYapici(FormKaydirici);
        vm.Hatalar.GosterIstendi += (_, _) =>
        {
            IslemBolmeleri.IlkiAc();
            gorunur.HatayaGit(IslemFormu, vm.Hatalar, FormHataKutusu);
        };
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }

    /// <summary>Satıra dokunmak düzenlemeyi açar (editör).</summary>
    private async void SatirTiklandi(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is IslemDto islem)
            await DuzenleAsync(islem);
    }

    /// <summary>"⋯": düzenle, sil ve (ekstreden gelen kayıtta) kaynak PDF.</summary>
    private async void EylemlerTiklandi(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: IslemDto islem } dugme || !_vm.EditorMu)
            return;
        var secenekler = new List<string> { "Düzenle", "Sil" };
        if (islem.EkstreKayitId is not null)
            secenekler.Add("Kaynak PDF");
        switch (await DisplayActionSheetAsync($"{islem.Cari} · {Bicim.Tl(islem.TutarTl)} ₺", "Vazgeç", null, [.. secenekler]))
        {
            case "Düzenle":
                await DuzenleAsync(islem);
                break;
            case "Sil":
                await SilmeOnayi.GosterAsync(this, dugme, $"{islem.Tarih:dd.MM.yyyy} · {islem.Cari} · {Bicim.Tl(islem.TutarTl)} ₺",
                    () => _vm.SilCommand.ExecuteAsync(islem));
                break;
            case "Kaynak PDF" when islem.EkstreKayitId is { } id:
                await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
                break;
        }
    }

    private async Task DuzenleAsync(IslemDto islem)
    {
        if (!_vm.EditorMu || !_vm.DuzenlemeyeGecCommand.CanExecute(islem))
            return;
        await _vm.DuzenlemeyeGecCommand.ExecuteAsync(islem);
        if (_vm.EditorMu && _vm.DuzenId == islem.Id)
            IslemBolmeleri.IlkiAc();
    }
}
