using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public IslemlerViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<IslemDto> Islemler { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.IslemlerAsync();
        Islemler.Clear();
        foreach (var i in liste) Islemler.Add(i);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);
}
