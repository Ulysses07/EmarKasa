using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KredilerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public KredilerViewModel(IKasaApi api) => _api = api;

    /// <summary>Belirli bir kanala ait olmayan ortak kredi etiketi (motorla birebir eşleşmeli).</summary>
    private const string OrtakKanal = "Ortak";

    public ObservableCollection<KrediGorunum> Krediler { get; } = new();

    /// <summary>Kredi formu kanal seçenekleri: aktif kanallar + "Ortak".</summary>
    public ObservableCollection<string> KanalSecenekleri { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.KredilerAsync();
        Krediler.Clear();
        foreach (var k in liste) Krediler.Add(new KrediGorunum(k));

        var kanallar = await _api.KanallarAsync();
        KanalSecenekleri.Clear();
        foreach (var ad in kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad))
            KanalSecenekleri.Add(ad);
        KanalSecenekleri.Add(OrtakKanal);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private decimal _duzenCekilenTutar;
    [ObservableProperty] private DateTime _duzenCekimTarihi = DateTime.Today;
    [ObservableProperty] private int _duzenTaksitSayisi;
    [ObservableProperty] private decimal _duzenAylikOdeme;
    [ObservableProperty] private int _duzenOdemeGunu;
    [ObservableProperty] private string _duzenKanal = "";

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenAd = ""; DuzenCekilenTutar = 0;
        DuzenCekimTarihi = DateTime.Today; DuzenTaksitSayisi = 0;
        DuzenAylikOdeme = 0; DuzenOdemeGunu = 0; DuzenKanal = "";
    }

    [RelayCommand]
    public void Duzenle(KrediGorunum k)
    {
        DuzenId = k.Id; DuzenAd = k.Ad; DuzenCekilenTutar = k.CekilenTutar;
        DuzenCekimTarihi = k.CekimTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenTaksitSayisi = k.TaksitSayisi; DuzenAylikOdeme = k.AylikOdeme;
        DuzenOdemeGunu = k.OdemeGunu; DuzenKanal = k.Kanal;
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var dto = new KrediDto(DuzenId, DuzenAd, DuzenCekilenTutar, DateOnly.FromDateTime(DuzenCekimTarihi),
            DuzenTaksitSayisi, DuzenAylikOdeme, DuzenOdemeGunu, DuzenKanal);
        if (DuzenId == 0) await _api.KrediEkleAsync(dto);
        else await _api.KrediGuncelleAsync(DuzenId, dto);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(KrediGorunum k) => CalistirAsync(async () =>
    {
        await _api.KrediSilAsync(k.Id);
        await DoldurAsync();
    });
}
