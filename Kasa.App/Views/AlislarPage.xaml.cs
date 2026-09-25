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
    private async void BelgeEkleTiklandi(object? sender, EventArgs e)
    {
        if (_vm.Mesgul || _vm.Secili is null) return;
        var oturum = _auth.OturumSurumu; var alisId = _vm.Secili.Id;
        try
        {
            var dosya = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "PDF, PNG veya JPEG belge seçin", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = new[] { ".pdf", ".png", ".jpg", ".jpeg" } }) });
            if (dosya is null || _auth.OturumSurumu != oturum || _vm.Secili?.Id != alisId) return;
            var tur = Path.GetExtension(dosya.FileName).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => null };
            if (tur is null) { await DisplayAlertAsync("Desteklenmeyen belge", "PDF, PNG veya JPEG seçin.", "Tamam"); return; }
            await using var stream = await dosya.OpenReadAsync();
            using var bellek = new MemoryStream();
            var buffer = new byte[81920];
            int okunan;
            while ((okunan = await stream.ReadAsync(buffer)) > 0)
            {
                if (bellek.Length + okunan > 10 * 1024 * 1024) { await DisplayAlertAsync("Belge büyük", "En fazla 10 MB belge yükleyebilirsiniz.", "Tamam"); return; }
                bellek.Write(buffer, 0, okunan);
            }
            if (_auth.OturumSurumu == oturum && _vm.Secili?.Id == alisId)
                await _vm.BelgeYukleAsync(dosya.FileName, tur, bellek.ToArray(), (BelgeOdemesi.SelectedItem as AlisOdemeSatiri)?.Veri.Id);
        }
        catch (Exception) { await DisplayAlertAsync("Belge okunamadı", "Dosyayı kontrol edip yeniden seçin.", "Tamam"); }
    }
    private async void BelgeIndirTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: BelgeDto belge } && await _vm.BelgeIndirAsync(belge) is { } dosya)
            await DosyaIslemleri.KaydetAsync(this, dosya);
    }
    private async void BelgeSilTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: BelgeDto belge } && await DisplayAlertAsync("Belgeyi sil", $"{belge.DosyaAdi} silinecek. Devam edilsin mi?", "Sil", "Vazgeç"))
            await _vm.BelgeSilAsync(belge);
    }
}
