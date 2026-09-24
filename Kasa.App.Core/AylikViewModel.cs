using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly IDosyaKaydedici? _kaydedici;

    public AylikViewModel(IKasaApi api, TimeProvider? zaman = null, IDosyaKaydedici? kaydedici = null) : base(zaman)
    {
        _api = api;
        _kaydedici = kaydedici;
        var bugun = Bugun;
        _yil = bugun.Year;
        _ay = bugun.Month;
    }

    [ObservableProperty] private int _yil;
    [ObservableProperty] private int _ay;
    [ObservableProperty] private AylikRaporDto? _rapor;

    /// <summary>İstek sürümü: hızlı ay değişiminde geç gelen eski ayın raporu yenisini ezmesin.</summary>
    private int _surum;

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var surum = ++_surum;
        int yil = Yil, ay = Ay;
        AylikRaporDto r;
        try { r = await _api.AylikAsync(yil, ay); }
        catch when (surum != _surum) { return; }
        if (surum == _surum) Rapor = r;
    });

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OncekiAy()
    {
        if (Ay == 1) { Ay = 12; Yil--; }
        else Ay--;
        return YukleAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SonrakiAy()
    {
        if (Ay == 12) { Ay = 1; Yil++; }
        else Ay++;
        return YukleAsync();
    }

    /// <summary>Son "Excel'e aktar"ın kaydettiği dosyanın tam yolu (sayfada gösterilir).</summary>
    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Ekranda seçili ayın raporunu CSV olarak kaydeder.</summary>
    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        int yil = Yil, ay = Ay;
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, () => _api.AylikCsvAsync(yil, ay));
    });
}
