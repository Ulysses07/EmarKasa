using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
}
