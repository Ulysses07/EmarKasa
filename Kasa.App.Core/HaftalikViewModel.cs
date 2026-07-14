using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.HaftalikAsync();
            Donemler.Clear();
            foreach (var d in liste) Donemler.Add(d);
        }
        finally { Mesgul = false; }
    }
}
