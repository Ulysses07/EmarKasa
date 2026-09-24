using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _duzenKesim = Bugun;
        _duzenSonOdeme = Bugun;
    }

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    /// <summary>Açılış borcu alanının altındaki uyarı (çift düşüm riski).</summary>
    public const string AcilisBorcuIpucu =
        "Açılış borcuna, İşlemler'de kartsız \"Kredi kartı\" olarak zaten girilmiş harcamaları dahil etmeyin; aksi halde aynı borç kasadan iki kez düşer.";

    /// <summary>
    /// Kartları ve tüm ödemeleri iki istekte çeker (kart başına istek yok). Liste ancak her şey
    /// geldikten sonra değiştirilir; kaydedilmemiş ödeme girişleri korunur.
    /// </summary>
    private async Task DoldurAsync(int? girisiSifirlanacakKartId = null)
    {
        var kartlarGorevi = _api.KrediKartlariAsync();
        var odemelerGorevi = _api.TumKartOdemeleriAsync();
        var liste = await kartlarGorevi;
        var odemeler = await odemelerGorevi;

        var karta = odemeler
            .GroupBy(o => o.KrediKartiId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList());
        var eskiler = Kartlar.ToDictionary(k => k.Id);
        var bugun = BugunTarih;

        var yeniler = new List<KrediKartiGorunum>();
        foreach (var k in liste)
        {
            var g = new KrediKartiGorunum(k, Bugun);
            if (karta.TryGetValue(k.Id, out var ods))
                foreach (var o in ods) g.Odemeler.Add(o);
            g.OdemeBekliyor = KartHatirlatici.OdemeBekliyor(g, bugun);
            if (k.Id != girisiSifirlanacakKartId && eskiler.TryGetValue(k.Id, out var eski))
                g.GirisiDevral(eski);
            yeniler.Add(g);
        }

        Kartlar.Clear();
        foreach (var g in yeniler) Kartlar.Add(g);
        KartIsteginiUygula();   // Paket A: "Ödeme gir" derin bağlantısı
    }

    public Task YukleAsync() => CalistirAsync(() => DoldurAsync());

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private DateTime _duzenKesim;
    [ObservableProperty] private DateTime _duzenSonOdeme;
    [ObservableProperty] private decimal _duzenLimit;
    [ObservableProperty] private decimal _duzenBorc;   // açılış borcu (baz/elle ayar)

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenAd = ""; DuzenKesim = Bugun;
        DuzenSonOdeme = Bugun; DuzenLimit = 0; DuzenBorc = 0;
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
        if (DuzenId == k.Id) Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task OdemeEkleAsync(KrediKartiGorunum k) => CalistirAsync(async () =>
    {
        Dogrula(k.OdemeTutarGiris > 0, "Ödeme tutarı sıfırdan büyük olmalı.");
        await _api.KartOdemeKaydetAsync(new KartOdemeYaz(k.Id, DateOnly.FromDateTime(k.OdemeTarihGiris), k.OdemeTutarGiris, null));
        k.OdemeTutarGiris = 0;
        await DoldurAsync(girisiSifirlanacakKartId: k.Id);
    });

    [RelayCommand]
    private Task OdemeSilAsync(KartOdemeDto o) => CalistirAsync(async () =>
    {
        await _api.KartOdemeSilAsync(o.Id);
        await DoldurAsync();
    });
}
