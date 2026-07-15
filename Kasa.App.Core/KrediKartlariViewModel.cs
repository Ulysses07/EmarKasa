using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.KrediKartlariAsync();
        Kartlar.Clear();
        foreach (var k in liste)
        {
            var g = new KrediKartiGorunum(k);
            var odemeler = await _api.KartOdemelerAsync(k.Id);
            foreach (var o in odemeler) g.Odemeler.Add(o);
            Kartlar.Add(g);
        }
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private DateTime _duzenKesim = DateTime.Today;
    [ObservableProperty] private DateTime _duzenSonOdeme = DateTime.Today;
    [ObservableProperty] private decimal _duzenLimit;
    [ObservableProperty] private decimal _duzenBorc;   // açılış borcu (baz/elle ayar)

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenAd = ""; DuzenKesim = DateTime.Today;
        DuzenSonOdeme = DateTime.Today; DuzenLimit = 0; DuzenBorc = 0;
    }

    [RelayCommand]
    public void Duzenle(KrediKartiGorunum k)
    {
        DuzenId = k.Id; DuzenAd = k.Ad;
        DuzenKesim = k.KesimTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenSonOdeme = k.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenLimit = k.Limit; DuzenBorc = k.AcilisBorc;   // form açılış borcunu düzenler
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new KrediKartiYaz(DuzenAd, DateOnly.FromDateTime(DuzenKesim), DateOnly.FromDateTime(DuzenSonOdeme), DuzenLimit, DuzenBorc);
        if (DuzenId == 0) await _api.KrediKartiOlusturAsync(g);
        else await _api.KrediKartiGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(KrediKartiGorunum k) => CalistirAsync(async () =>
    {
        await _api.KrediKartiSilAsync(k.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task OdemeEkleAsync(KrediKartiGorunum k) => CalistirAsync(async () =>
    {
        await _api.KartOdemeKaydetAsync(new KartOdemeYaz(k.Id, DateOnly.FromDateTime(k.OdemeTarihGiris), k.OdemeTutarGiris, null));
        k.OdemeTutarGiris = 0;
        await DoldurAsync();
    });

    [RelayCommand]
    private Task OdemeSilAsync(KartOdemeDto o) => CalistirAsync(async () =>
    {
        await _api.KartOdemeSilAsync(o.Id);
        await DoldurAsync();
    });
}
