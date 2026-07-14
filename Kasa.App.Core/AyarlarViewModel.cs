using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Editör ayarları: kanal CRUD + izleyici şifre + takip başlangıç/açılış devri (spec §6).</summary>
public partial class AyarlarViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public AyarlarViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KanalDto> Kanallar { get; } = new();

    [ObservableProperty] private DateTime _takipBaslangic = DateTime.Today;
    [ObservableProperty] private decimal _kasaAcilisDevri;

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
    [ObservableProperty] private bool _duzenKanalAktif = true;
    [ObservableProperty] private int _duzenKanalSira;
    [ObservableProperty] private decimal _duzenKanalAcilisDevri;

    // İzleyici şifre
    [ObservableProperty] private string _yeniIzleyiciSifre = "";

    private async Task DoldurAsync()
    {
        var ayar = await _api.AyarlarAsync();
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = ayar.KasaAcilisDevri;
        var kanallar = await _api.KanallarAsync();
        Kanallar.Clear();
        foreach (var k in kanallar) Kanallar.Add(k);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [RelayCommand]
    private void YeniKanal()
    {
        DuzenKanalId = 0; DuzenKanalAd = ""; DuzenKanalAktif = true;
        DuzenKanalSira = 0; DuzenKanalAcilisDevri = 0;
    }

    [RelayCommand]
    public void KanalDuzenle(KanalDto k)
    {
        DuzenKanalId = k.Id; DuzenKanalAd = k.Ad; DuzenKanalAktif = k.Aktif;
        DuzenKanalSira = k.Sira; DuzenKanalAcilisDevri = k.AcilisDevri;
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => CalistirAsync(async () =>
    {
        var g = new KanalYaz(DuzenKanalAd, DuzenKanalAktif, DuzenKanalSira, DuzenKanalAcilisDevri);
        if (DuzenKanalId == 0) await _api.KanalOlusturAsync(g);
        else await _api.KanalGuncelleAsync(DuzenKanalId, g);
        YeniKanal();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task KanalSilAsync(KanalDto k) => CalistirAsync(async () =>
    {
        await _api.KanalSilAsync(k.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task AyarKaydetAsync() => CalistirAsync(async () =>
        await _api.AyarGuncelleAsync(new AyarYaz(DateOnly.FromDateTime(TakipBaslangic), KasaAcilisDevri)));

    [RelayCommand]
    private Task IzleyiciSifreKaydetAsync() => CalistirAsync(async () =>
    {
        await _api.IzleyiciSifreAsync(YeniIzleyiciSifre);
        YeniIzleyiciSifre = "";
    });
}
