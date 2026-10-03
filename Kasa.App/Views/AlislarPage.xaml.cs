using Kasa.App.Core;
using Kasa.ApiClient;

namespace Kasa.App.Views;

public partial class AlislarPage : ContentPage, Controls.IYenilenebilir, IQueryAttributable
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly AlislarViewModel _vm;
    private int? _istenenAlisId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("AlisId", out var value) || !int.TryParse(value.ToString(), out var id))
            return;
        _istenenAlisId = id;
        if (_vm.VeriHazir)
        {
            _vm.IdIleSec(id);
            _istenenAlisId = null;
        }
    }
    // Oturum değişimini ve rolü model kendisi alır (AlislarViewModel : OturumluViewModel; appcore-10): sayfa ayrıca abone olmaz,
    // rolü ekrana atamaz.
    public AlislarPage(AlislarViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var gorunur = new Controls.GorunurYapici(DetayKaydirici);
        vm.Hatalar.GosterIstendi += (_, _) => gorunur.HatayaGit(AlisFormu, vm.Hatalar, FormHataKutusu);
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_vm.VeriHazir)
            await _vm.YukleAsync();
        if (_istenenAlisId is { } id && _vm.VeriHazir)
        {
            _vm.IdIleSec(id);
            _istenenAlisId = null;
        }
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
        if (await DisplayAlertAsync("Değişiklikleri bırak", "Kaydedilmemiş alış değişiklikleri silinecek. Devam edilsin mi?",
            KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon))
            _vm.DegisiklikleriBirakCommand.Execute(null);
    }
    private async void OdemeIptalTiklandi(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Ödemeyi iptal et",
            "Ödeme ve bağlı gider kaldırılacak; gerekçe işlem geçmişinde kalacak. Gerçekte yapılmış ödeme için bu işlemi kullanmayın. Devam edilsin mi?",
            "Ödemeyi iptal et", "Vazgeç"))
            await _vm.OdemeIptalAsync();
    }
    private async void KartAcTiklandi(object? sender, EventArgs e)
    {
        if (_vm.EditorMu && sender is Button { CommandParameter: AlisOdemeSatiri { Veri.KrediKartiId: { } id } })
            await Shell.Current.GoToAsync($"//kartlar?KartId={id}");
    }
    // İçerik türü, 10 MB sınırı ve oturum/seçim koruması AlislarViewModel.BelgeEkleAsync'tedir (maui-8); sayfa yalnız dosya
    // seçiciyi açar ve dönen uyarıyı gösterir.
    private async void BelgeEkleTiklandi(object? sender, EventArgs e)
    {
        var uyari = await _vm.BelgeEkleAsync(BelgeSecAsync, (BelgeOdemesi.SelectedItem as AlisOdemeSatiri)?.Veri.Id);
        if (uyari is not null)
            await DisplayAlertAsync(uyari.Baslik, uyari.Mesaj, "Tamam");
    }
    private static async Task<SecilenDosya?> BelgeSecAsync()
    {
        var dosya = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "PDF, PNG veya JPEG belge seçin",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = new[] { ".pdf", ".png", ".jpg", ".jpeg" } })
        });
        return dosya is null ? null : new SecilenDosya(dosya.FileName, dosya.OpenReadAsync);
    }
    private async void BelgeIndirTiklandi(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: BelgeDto belge } && !_vm.Mesgul)
            await DosyaIslemleri.IndirVeKaydetAsync(this, hedef => _vm.BelgeIndirAsync(belge, hedef), disKaynak: true);
    }
    /// <summary>Kaldırma yumuşaktır (gap-denetim-izi-gozlemlenebilirlik-9): belge ve kaldırma kaydı saklanır. Editör için gerekçe
    /// zorunludur (boşsa model göndermez ve söyler), alıcı için isteğe bağlıdır; vazgeçilirse ya da pencere açıkken oturum
    /// değişirse hiçbir şey gönderilmez (GerekceyleAsync).</summary>
    private async void BelgeSilTiklandi(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: BelgeDto belge } || _vm.Mesgul)
            return;
        await _vm.GerekceyleAsync(() => DisplayPromptAsync("Belgeyi kaldır", _vm.EditorMu
                ? $"{belge.DosyaAdi} listeden kaldırılacak; içeriği ve kaldırma kaydı saklanır. Kaldırma gerekçesini yazın (zorunlu)."
                : $"{belge.DosyaAdi} listeden kaldırılacak; editör kaldırılan belgeyi görmeye devam eder. İsterseniz gerekçe yazın.",
            "Kaldır", "Vazgeç", placeholder: "Gerekçe", maxLength: 2000), (gerekce, _) => _vm.BelgeSilAsync(belge, gerekce), bosGerekceGecerli: true);
    }
}
