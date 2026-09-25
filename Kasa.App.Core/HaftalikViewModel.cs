using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    public override Task YukleAsync() => RaporYukleAsync(_api.HaftalikAsync, liste =>
    {
        Donemler.Clear();
        foreach (var d in liste) Donemler.Add(d);
    });
}
