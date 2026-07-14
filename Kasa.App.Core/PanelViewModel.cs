using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<KanalBakiyeDto> Kanallar { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var p = await _api.PanelAsync();
            GuncelKasa = p.GuncelKasa;
            BuHaftaSonucu = p.BuHaftaSonucu;
            BuAySonucu = p.BuAySonucu;
            Kanallar.Clear();
            foreach (var k in p.Kanallar) Kanallar.Add(k);
        }
        finally { Mesgul = false; }
    }
}
