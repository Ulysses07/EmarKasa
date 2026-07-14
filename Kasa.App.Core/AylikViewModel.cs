using CommunityToolkit.Mvvm.ComponentModel;
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
}
