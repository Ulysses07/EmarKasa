using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly IDosyaKaydedici? _kaydedici;

    public HaftalikViewModel(IKasaApi api, IDosyaKaydedici? kaydedici = null)
    {
        _api = api;
        _kaydedici = kaydedici;
    }

    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    /// <summary>Son "Excel'e aktar"ın kaydettiği dosyanın tam yolu (sayfada gösterilir).</summary>
    [ObservableProperty] private string? _aktarilanDosya;

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var liste = await _api.HaftalikAsync();
        Donemler.Clear();
        foreach (var d in liste) Donemler.Add(d);
    });

    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, _api.HaftalikCsvAsync);
    });
}
