using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public AylikViewModel(IKasaApi api)
    {
        _api = api;
        var bugun = DateTime.Today;
        _yil = bugun.Year;
        _ay = bugun.Month;
    }

    [ObservableProperty] private int _yil;
    [ObservableProperty] private int _ay;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagilimBekliyor))]
    [NotifyPropertyChangedFor(nameof(GenelGiderVar))]
    [NotifyPropertyChangedFor(nameof(GenelGelirVar))]
    [NotifyPropertyChangedFor(nameof(GenelAySonucu))]
    private AylikRaporDto? _rapor;
    public bool DagilimBekliyor => Rapor?.DagilimBekleyenTutar > 0;
    public bool GenelGiderVar => Rapor?.GenelGider != 0 && Rapor is not null;
    public bool GenelGelirVar => Rapor?.GenelGelir != 0 && Rapor is not null;
    public decimal GenelAySonucu => Rapor is { } r ? r.Kanallar.Sum(k => k.AySonucu) - r.DagilimBekleyenTutar - r.GenelGider + r.GenelGelir : 0;

    public override Task YukleAsync()
    {
        var yil = Yil;
        var ay = Ay;
        Rapor = null;
        return RaporYukleAsync(() => _api.AylikAsync(yil, ay), rapor => Rapor = rapor);
    }

    [RelayCommand]
    private Task OncekiAy()
    {
        if (Ay == 1) { Ay = 12; Yil--; }
        else Ay--;
        return YukleAsync();
    }

    [RelayCommand]
    private Task SonrakiAy()
    {
        if (Ay == 12) { Ay = 1; Yil++; }
        else Ay++;
        return YukleAsync();
    }
}
