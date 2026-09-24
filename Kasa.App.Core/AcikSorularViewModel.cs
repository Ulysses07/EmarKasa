using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Panel'deki "Açık sorular (N)" kartı: son açık sorular ve Sorular sayfasına geçiş. Açık soru yoksa
/// (ya da okunamadıysa) kart gizlenir; Panel'in kendi hatasını bozmaz.
/// </summary>
public partial class AcikSorularViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly Yonlendirme? _yonlendirme;

    public AcikSorularViewModel(IKasaApi api, Yonlendirme? yonlendirme = null, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _yonlendirme = yonlendirme;
    }

    /// <summary>Editörde alt metin "cevap bekliyor" vurgusu yapar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AltMetin))]
    private bool _editorMu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Baslik), nameof(Gorunur), nameof(AltMetin))]
    private int _acikSayisi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AltMetin))]
    private int _cevapBekleyen;

    public ObservableCollection<SoruSatiri> SonSorular { get; } = new();

    public string Baslik => $"Açık sorular ({AcikSayisi})";
    public bool Gorunur => AcikSayisi > 0;

    public string AltMetin => CevapBekleyen == 0
        ? "Tüm açık sorular cevaplandı; kapatılmayı bekliyor."
        : EditorMu ? $"{CevapBekleyen} soru cevabınızı bekliyor." : $"{CevapBekleyen} soru cevap bekliyor.";

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        try
        {
            var ozet = await _api.SoruOzetAsync();
            AcikSayisi = ozet.AcikSayisi;
            CevapBekleyen = ozet.CevapBekleyen;
            SonSorular.Clear();
            foreach (var s in ozet.SonAciklar) SonSorular.Add(new SoruSatiri(s, Zaman.LocalTimeZone, editorMu: false));
        }
        catch (Exception)
        {
            // Kart yardımcıdır: okunamazsa gizlenir, Panel'in hata kutusu kendi verisine aittir.
            AcikSayisi = 0;
            CevapBekleyen = 0;
            SonSorular.Clear();
        }
    });

    [RelayCommand]
    private void SorularaGit() => _yonlendirme?.Iste("sorular");
}
