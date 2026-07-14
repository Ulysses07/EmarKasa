using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.KrediKartlariAsync();
        Kartlar.Clear();
        foreach (var k in liste) Kartlar.Add(new KrediKartiGorunum(k));
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);
}
