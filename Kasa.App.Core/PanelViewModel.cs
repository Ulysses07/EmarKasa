using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    public ObservableCollection<KanalBakiyeDto> Kanallar { get; } = new();

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var p = await _api.PanelAsync();
        GuncelKasa = p.GuncelKasa;
        BuHaftaSonucu = p.BuHaftaSonucu;
        BuAySonucu = p.BuAySonucu;
        Kanallar.Clear();
        foreach (var k in p.Kanallar) Kanallar.Add(k);
    });
}
