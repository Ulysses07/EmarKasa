using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        BekleyenGiderler.CollectionChanged += (_, _) => OnPropertyChanged(nameof(BekleyenVar));
    }

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    public ObservableCollection<KanalBakiyeDto> Kanallar { get; } = new();

    /// <summary>Editör mü? Bekleyen giderler kartı yalnız editöre yüklenir ve gösterilir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BekleyenVar))]
    private bool _editorMu;

    /// <summary>Vadesi gelmiş, girilmemiş/atlanmamış tekrarlayan giderler (vadeye göre).</summary>
    public ObservableCollection<BekleyenGiderGorunum> BekleyenGiderler { get; } = new();

    /// <summary>Kart görünürlüğü: liste boşsa kart gizlenir.</summary>
    public bool BekleyenVar => EditorMu && BekleyenGiderler.Count > 0;

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    private async Task DoldurAsync()
    {
        var panelGorevi = _api.PanelAsync();
        var bekleyenGorevi = EditorMu
            ? TekrarlayanYukleme.Oku(_api.BekleyenGiderlerAsync)
            : Task.FromResult<IReadOnlyList<BekleyenGiderDto>>(Array.Empty<BekleyenGiderDto>());
        await Task.WhenAll(panelGorevi, bekleyenGorevi);
        var p = panelGorevi.Result;
        GuncelKasa = p.GuncelKasa;
        BuHaftaSonucu = p.BuHaftaSonucu;
        BuAySonucu = p.BuAySonucu;
        Kanallar.Clear();
        foreach (var k in p.Kanallar) Kanallar.Add(k);
        BekleyenleriKur(bekleyenGorevi.Result);
    }

    private void BekleyenleriKur(IReadOnlyList<BekleyenGiderDto> liste)
    {
        var eskiler = BekleyenGiderler.ToList();
        BekleyenGiderler.Clear();
        foreach (var d in liste)
        {
            var yeni = new BekleyenGiderGorunum(d);
            if (eskiler.FirstOrDefault(yeni.AyniKayit) is { } eski) yeni.GirisiDevral(eski);
            BekleyenGiderler.Add(yeni);
        }
    }

    /// <summary>
    /// Bekleyen ayı sabit gider işlemi olarak girer: tarih vade günüdür (ileri tarih asla),
    /// tutar satırdaki tutar. Kasa özeti de tazelenir.
    /// </summary>
    [RelayCommand]
    private Task BekleyenKaydetAsync(BekleyenGiderGorunum b) => CalistirAsync(async () =>
    {
        Dogrula(b.Tutar > 0, "Tutar sıfırdan büyük olmalı.");
        var tarih = b.Vade > BugunTarih ? BugunTarih : b.Vade;
        await KararVerAsync(() => _api.TekrarlayanOnaylaAsync(b.TekrarlayanGiderId, new TekrarlayanOnayYaz(b.Ay, tarih, b.Tutar)));
        BekleyenGiderler.Remove(b);
        await DoldurAsync();
    });

    /// <summary>Bu ayı işlem girmeden kapatır (ödeme yapılmayacak ya da elle girildi).</summary>
    [RelayCommand]
    private Task BekleyenAtlaAsync(BekleyenGiderGorunum b) => CalistirAsync(async () =>
    {
        await KararVerAsync(() => _api.TekrarlayanAtlaAsync(b.TekrarlayanGiderId, b.Ay));
        BekleyenGiderler.Remove(b);
        await DoldurAsync();
    });

    /// <summary>
    /// Başka cihazda karar verilmiş (409) ya da kayıt silinmişse (404) liste tazelenir ki eski
    /// satır kalmasın; sunucunun mesajı yine gösterilir.
    /// </summary>
    private async Task KararVerAsync(Func<Task> karar)
    {
        try { await karar(); }
        catch (KasaApiException ex) when (ex.DurumKodu is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            try { await DoldurAsync(); } catch (Exception) { /* asıl hata gösterilir */ }
            throw;
        }
    }
}
