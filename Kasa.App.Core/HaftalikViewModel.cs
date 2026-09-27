using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
    /// raporun tamamı için geçerlidir; yüklenirken ve hatada null.</summary>
    [ObservableProperty] private string? _veriSagligiUyarisi;

    public override Task YukleAsync()
    {
        VeriSagligiUyarisi = null;
        return RaporYukleAsync(ct => _api.HaftalikAsync(ct), liste =>
        {
            Donemler.Clear();
            foreach (var d in liste) Donemler.Add(d);
            VeriSagligiUyarisi = liste.LastOrDefault(d => !string.IsNullOrWhiteSpace(d.VeriSagligiUyarisi))?.VeriSagligiUyarisi;
        });
    }
}
