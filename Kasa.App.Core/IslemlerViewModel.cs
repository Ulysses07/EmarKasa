using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private bool _editorMu;

    // İşlem düzenleme
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private DateTime _duzenTarih = DateTime.Today;
    [ObservableProperty] private string _duzenCari = "";
    [ObservableProperty] private decimal _duzenTutar;
    [ObservableProperty] private string _duzenKanal = "";
    [ObservableProperty] private GiderTipi _duzenTip = GiderTipi.Cari;
    [ObservableProperty] private string? _duzenNot;

    // Gelen girişi
    [ObservableProperty] private DateTime _gelenTarih = DateTime.Today;
    [ObservableProperty] private string _gelenKanal = "";
    [ObservableProperty] private decimal _gelenTutar;

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenTarih = DateTime.Today; DuzenCari = "";
        DuzenTutar = 0; DuzenKanal = ""; DuzenTip = GiderTipi.Cari; DuzenNot = null;
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        DuzenId = i.Id; DuzenTarih = i.Tarih.ToDateTime(TimeOnly.MinValue);
        DuzenCari = i.Cari; DuzenTutar = i.TutarTl; DuzenKanal = i.Kanal;
        DuzenTip = i.Tip; DuzenNot = i.Not;
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new IslemYaz(DateOnly.FromDateTime(DuzenTarih), DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot);
        if (DuzenId == 0) await _api.IslemOlusturAsync(g);
        else await _api.IslemGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(IslemDto i) => CalistirAsync(async () =>
    {
        await _api.IslemSilAsync(i.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task GelenKaydetAsync() => CalistirAsync(async () =>
    {
        await _api.GelenKaydetAsync(new GelenYaz(DateOnly.FromDateTime(GelenTarih), GelenKanal, GelenTutar));
        GelenKanal = ""; GelenTutar = 0;
    });
}
