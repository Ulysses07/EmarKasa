using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    [ObservableProperty] private bool _mesgul;

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.KrediKartlariAsync();
            Kartlar.Clear();
            foreach (var k in liste) Kartlar.Add(new KrediKartiGorunum(k));
        }
        finally { Mesgul = false; }
    }
}
