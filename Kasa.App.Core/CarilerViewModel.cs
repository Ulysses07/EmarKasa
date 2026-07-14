using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class CarilerViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public CarilerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _ara;
    public ObservableCollection<CariDto> Cariler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.CarilerAsync(string.IsNullOrWhiteSpace(Ara) ? null : Ara);
            Cariler.Clear();
            foreach (var c in liste) Cariler.Add(c);
        }
        finally { Mesgul = false; }
    }
}
