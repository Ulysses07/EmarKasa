using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : TemelViewModel
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
    [ObservableProperty] private AylikRaporDto? _rapor;

    public Task YukleAsync() => CalistirAsync(async () => Rapor = await _api.AylikAsync(Yil, Ay));

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
