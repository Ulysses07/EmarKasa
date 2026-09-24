using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class CarilerViewModel : TemelViewModel
{
    /// <summary>Arama kutusunda yazma bitince aramadan önce beklenen süre.</summary>
    public static readonly TimeSpan AramaGecikmesi = TimeSpan.FromMilliseconds(350);

    private readonly IKasaApi _api;
    public CarilerViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman) => _api = api;

    [ObservableProperty] private string? _ara;
    public ObservableCollection<CariDto> Cariler { get; } = new();

    /// <summary>Liste istek sürümü: yazarken geç gelen eski arama sonucu yenisini ezmesin.</summary>
    private int _surum;

    private async Task DoldurAsync()
    {
        var surum = ++_surum;
        var ara = string.IsNullOrWhiteSpace(Ara) ? null : Ara.Trim();
        IReadOnlyList<CariDto> liste;
        try { liste = await _api.CarilerAsync(ara); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        Cariler.Clear();
        foreach (var c in liste) Cariler.Add(c);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    /// <summary>Arama kutusundaki Enter / ara düğmesi: hemen arar.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AraAsync() => CalistirAsync(DoldurAsync);

    /// <summary>Yazarken kısa bir beklemeden sonra otomatik arar.</summary>
    partial void OnAraChanged(string? value) => _ = GecikmeliAraAsync(++_yazmaSayaci);
    private int _yazmaSayaci;

    private async Task GecikmeliAraAsync(int sayac)
    {
        try { await Task.Delay(AramaGecikmesi, Zaman); }
        catch (Exception) { return; }
        if (sayac == _yazmaSayaci) await CalistirAsync(DoldurAsync);
    }

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;        // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private bool _duzenAktif = true;

    [RelayCommand]
    private void Yeni() { DuzenId = 0; DuzenAd = ""; DuzenAktif = true; }

    [RelayCommand]
    public void Duzenle(CariDto c) { DuzenId = c.Id; DuzenAd = c.Ad; DuzenAktif = c.Aktif; }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new CariYaz(DuzenAd, DuzenAktif);
        if (DuzenId == 0) await _api.CariOlusturAsync(g);
        else await _api.CariGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });
}
