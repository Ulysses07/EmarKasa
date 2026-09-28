using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class AlislarPage : ContentPage, IQueryAttributable
{
    private readonly AlislarViewModel _vm;
    private readonly AuthViewModel _auth;
    private int? _istenenAlisId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("AlisId", out var value) && int.TryParse(value.ToString(), out var id))
        { _istenenAlisId = id; if (_vm.VeriHazir) { _vm.IdIleSec(id); _istenenAlisId = null; } }
    }
    public AlislarPage(AlislarViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
        _auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.OturumSurumu))
            {
                _vm.BekleyenIslemleriGecersizKil();
                MainThread.BeginInvokeOnMainThread(() => _vm.OturumuAyarla(_auth.OturumSurumu, _auth.AktifRol == Rol.Editor));
            }
        };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.OturumuAyarla(_auth.OturumSurumu, _auth.AktifRol == Rol.Editor);
        if (!_vm.VeriHazir) await _vm.YukleAsync();
        if (_istenenAlisId is { } id && _vm.VeriHazir) { _vm.IdIleSec(id); _istenenAlisId = null; }
    }
    private async void HesaplarTiklandi(object? sender, EventArgs e)
    {
        _vm.HesaplariAcKapatCommand.Execute(null);
        if (_vm.HesaplarAcik)
        {
            await Task.Yield();
            await DetayKaydirici.ScrollToAsync(AliciHesapAlani, ScrollToPosition.Start, true);
        }
    }
    private async void DegisiklikleriBirakTiklandi(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Değişiklikleri bırak", "Kaydedilmemiş alış değişiklikleri silinecek. Devam edilsin mi?", "Bırak", "Vazgeç"))
            _vm.DegisiklikleriBirakCommand.Execute(null);
    }
    private async void OdemeIptalTiklandi(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Ödemeyi iptal et", "Ödeme ve bağlı gider kaldırılacak; gerekçe işlem geçmişinde kalacak. Gerçekte yapılmış ödeme için bu işlemi kullanmayın. Devam edilsin mi?", "Ödemeyi iptal et", "Vazgeç"))
            await _vm.OdemeIptalAsync();
    }
    private async void KartAcTiklandi(object? sender, EventArgs e)
    {
        if (_auth.AktifRol != Rol.Alici && sender is Button { CommandParameter: AlisOdemeSatiri { Veri.KrediKartiId: { } id } })
            await Shell.Current.GoToAsync($"//kartlar?KartId={id}");
    }
    // İçerik türü, 10 MB sınırı ve oturum/seçim koruması AlislarViewModel.BelgeEkleAsync'tedir (maui-8); sayfa yalnız dosya
    // seçiciyi açar ve dönen uyarıyı gösterir.
    private async void BelgeEkleTiklandi(object? sender, EventArgs e)
    {
        var uyari = await _vm.BelgeEkleAsync(BelgeSecAsync, (BelgeOdemesi.SelectedItem as AlisOdemeSatiri)?.Veri.Id);
        if (uyari is not null) await DisplayAlertAsync(uyari.Baslik, uyari.Mesaj, "Tamam");
    }
    private static async Task<SecilenDosya?> BelgeSecAsync()
    {
        var dosya = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "PDF, PNG veya JPEG belge seçin", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = new[] { ".pdf", ".png", ".jpg", ".jpeg" } }) });
        return dosya is null ? null : new SecilenDosya(dosya.FileName, dosya.OpenReadAsync);
    }
    private async void BelgeIndirTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: BelgeDto belge } && !_vm.Mesgul)
            await DosyaIslemleri.IndirVeKaydetAsync(this, hedef => _vm.BelgeIndirAsync(belge, hedef), disKaynak: true);
    }
    private async void BelgeSilTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: BelgeDto belge } && await DisplayAlertAsync("Belgeyi sil", $"{belge.DosyaAdi} silinecek. Devam edilsin mi?", "Sil", "Vazgeç"))
            await _vm.BelgeSilAsync(belge);
    }
}
