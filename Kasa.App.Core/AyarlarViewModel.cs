using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Editör ayarları: kanal ve sabit gider kalemi CRUD + izleyici şifre + takip başlangıç/açılış devri (spec §6).</summary>
public partial class AyarlarViewModel : TemelViewModel
{
    /// <summary>"Tüm oturumları kapat" onayının geçerli kaldığı süre.</summary>
    public static readonly TimeSpan OnayZamanAsimi = TimeSpan.FromSeconds(5);

    private readonly IKasaApi _api;
    public AyarlarViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _takipBaslangic = Bugun;
    }

    public ObservableCollection<KanalDto> Kanallar { get; } = new();
    public ObservableCollection<GiderKalemiDto> GiderKalemleri { get; } = new();

    // Sabit gider kalemi düzenleme
    [ObservableProperty] private int _duzenKalemId;      // 0 = yeni
    [ObservableProperty] private string _duzenKalemAd = "";
    [ObservableProperty] private bool _duzenKalemAktif = true;

    [ObservableProperty] private DateTime _takipBaslangic;
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
        var ayarGorevi = _api.AyarlarAsync();
        var kanalGorevi = _api.KanallarAsync();
        var kalemGorevi = _api.GiderKalemleriAsync();
        var ayar = await ayarGorevi;
        var kanallar = await kanalGorevi;
        var kalemler = await kalemGorevi;
        GiderKalemleri.Clear();
        foreach (var k in kalemler) GiderKalemleri.Add(k);
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = ayar.KasaAcilisDevri;
        Kanallar.Clear();
        foreach (var k in kanallar) Kanallar.Add(k);
    }

    public Task YukleAsync()
    {
        OturumKapatOnayBekliyor = false;   // sayfa önbellekte kalır; eski onay yeniden açılışta geçersiz
        return CalistirAsync(DoldurAsync);
    }

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
        if (DuzenKanalId == k.Id) YeniKanal();
        await DoldurAsync();
    });

    [RelayCommand]
    private void YeniKalem() { DuzenKalemId = 0; DuzenKalemAd = ""; DuzenKalemAktif = true; }

    [RelayCommand]
    public void KalemDuzenle(GiderKalemiDto k) { DuzenKalemId = k.Id; DuzenKalemAd = k.Ad; DuzenKalemAktif = k.Aktif; }

    /// <summary>Kalem adı değişince o kalemle girilmiş eski sabit gider işlemleri de yeni adı alır (sunucu).</summary>
    [RelayCommand]
    private Task KalemKaydetAsync() => CalistirAsync(async () =>
    {
        var g = new GiderKalemiYaz(DuzenKalemAd, DuzenKalemAktif);
        if (DuzenKalemId == 0) await _api.GiderKalemiOlusturAsync(g);
        else await _api.GiderKalemiGuncelleAsync(DuzenKalemId, g);
        YeniKalem();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task KalemSilAsync(GiderKalemiDto k) => CalistirAsync(async () =>
    {
        await _api.GiderKalemiSilAsync(k.Id);
        if (DuzenKalemId == k.Id) YeniKalem();
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

    /// <summary>İlk basış onay ister; ~5 sn içindeki ikinci basış tüm cihazlardaki oturumları kapatır.</summary>
    [ObservableProperty] private bool _oturumKapatOnayBekliyor;

    private DateTimeOffset _onayZamani;
    private int _onaySayaci;

    public string OturumKapatMetni => OturumKapatOnayBekliyor
        ? "Emin misiniz? Herkes çıkış yapacak, 5 sn içinde tekrar basın"
        : "Tüm oturumları kapat";

    partial void OnOturumKapatOnayBekliyorChanged(bool value) => OnPropertyChanged(nameof(OturumKapatMetni));

    [RelayCommand]
    private Task OturumlariKapatAsync()
    {
        var simdi = Zaman.GetUtcNow();
        if (!OturumKapatOnayBekliyor || simdi - _onayZamani > OnayZamanAsimi)
        {
            _onayZamani = simdi;
            OturumKapatOnayBekliyor = true;
            _ = OnayiSonraSifirlaAsync(++_onaySayaci);
            return Task.CompletedTask;
        }
        OturumKapatOnayBekliyor = false;
        // Başarıda istemci token'ı siler ve oturum olayını tetikler → uygulama Login'e döner.
        return CalistirAsync(() => _api.OturumlariKapatAsync());
    }

    /// <summary>Onay süresi dolunca düğme metnini eski haline döndürür.</summary>
    private async Task OnayiSonraSifirlaAsync(int sayac)
    {
        try { await Task.Delay(OnayZamanAsimi, Zaman); }
        catch (Exception) { return; }
        if (sayac == _onaySayaci) OturumKapatOnayBekliyor = false;
    }
}
