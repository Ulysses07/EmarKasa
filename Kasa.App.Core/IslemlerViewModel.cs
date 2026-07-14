using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public IslemlerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<IslemDto> Islemler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.IslemlerAsync();
            Islemler.Clear();
            foreach (var i in liste) Islemler.Add(i);
        }
        finally { Mesgul = false; }
    }
}
