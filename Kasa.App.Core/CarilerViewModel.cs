using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class CarilerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public CarilerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private string? _ara;
    public ObservableCollection<CariDto> Cariler { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.CarilerAsync(string.IsNullOrWhiteSpace(Ara) ? null : Ara);
        Cariler.Clear();
        foreach (var c in liste) Cariler.Add(c);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;        // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private bool _duzenAktif = true;

    [RelayCommand]
    private void Yeni() { DuzenId = 0; DuzenAd = ""; DuzenAktif = true; }

    [RelayCommand]
    public void Duzenle(CariDto c) { DuzenId = c.Id; DuzenAd = c.Ad; DuzenAktif = c.Aktif; }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new CariYaz(DuzenAd, DuzenAktif);
        if (DuzenId == 0) await _api.CariOlusturAsync(g);
        else await _api.CariGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });
}
