using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var liste = await _api.HaftalikAsync();
        Donemler.Clear();
        foreach (var d in liste) Donemler.Add(d);
    });
}
