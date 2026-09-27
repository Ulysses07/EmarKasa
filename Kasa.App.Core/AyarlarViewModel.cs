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
    [ObservableProperty] private string? _ayarUyarisi;

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
    [ObservableProperty] private bool _duzenKanalAktif = true;
    [ObservableProperty] private int _duzenKanalSira;
    [ObservableProperty] private decimal _duzenKanalAcilisDevri;
    [ObservableProperty] private string? _kanalUyarisi;

    // İzleyici şifre
    [ObservableProperty] private string _yeniIzleyiciSifre = "";

    // Boşaltılan para alanı 0 olur: kayıtlı sıfır olmayan açılış devrini 0'a indirmek, gelir formundaki gibi
    // ikinci basışta kaydedilir. Değer ya da düzenlenen alan/kanal değişince onay sıfırlanır.
    private decimal _kayitliKasaAcilisDevri, _kayitliKanalAcilisDevri;
    private bool _kasaSifirOnayi, _kanalSifirOnayi;

    partial void OnTakipBaslangicChanged(DateTime value) => AyarOnayiniSifirla();
    partial void OnKasaAcilisDevriChanged(decimal value) => AyarOnayiniSifirla();
    partial void OnDuzenKanalIdChanged(int value) => KanalOnayiniSifirla();
    partial void OnDuzenKanalAcilisDevriChanged(decimal value) => KanalOnayiniSifirla();
    private void AyarOnayiniSifirla() { _kasaSifirOnayi = false; AyarUyarisi = null; }
    private void KanalOnayiniSifirla() { _kanalSifirOnayi = false; KanalUyarisi = null; }

    /// <summary>Kayıtlı sıfır olmayan tutar 0'a iniyorsa ve henüz onaylanmadıysa onay metni; aksi halde null.</summary>
    private static string? SifirOnayMetni(string ad, decimal kayitli, decimal yeni, bool onaylandi) =>
        kayitli != 0 && yeni == 0 && !onaylandi
            ? $"{ad} {Bicim.Tl(kayitli)} ₺ yerine 0,00 ₺ yapılacak. Alan boş bırakılmış olabilir. Onaylamak için yeniden kaydedin."
            : null;

    private async Task DoldurAsync()
    {
        var ayar = await _api.AyarlarAsync();
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = _kayitliKasaAcilisDevri = ayar.KasaAcilisDevri;
        var kanallar = await _api.KanallarAsync();
        Kanallar.Clear();
        foreach (var k in kanallar) Kanallar.Add(k);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [RelayCommand]
    private void YeniKanal()
    {
        DuzenKanalId = 0; DuzenKanalAd = ""; DuzenKanalAktif = true;
        DuzenKanalSira = 0; DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = 0;
    }

    [RelayCommand]
    public void KanalDuzenle(KanalDto k)
    {
        DuzenKanalId = k.Id; DuzenKanalAd = k.Ad; DuzenKanalAktif = k.Aktif;
        DuzenKanalSira = k.Sira; DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = k.AcilisDevri;
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => CalistirAsync(async () =>
    {
        if (!ParaAyristirici.GecerliMi(DuzenKanalAcilisDevri)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (SifirOnayMetni($"{DuzenKanalAd} açılış devri", _kayitliKanalAcilisDevri, DuzenKanalAcilisDevri, _kanalSifirOnayi) is { } onay)
        {
            _kanalSifirOnayi = true; KanalUyarisi = onay; return;
        }
        var g = new KanalYaz(DuzenKanalAd, DuzenKanalAktif, DuzenKanalSira, DuzenKanalAcilisDevri);
        if (DuzenKanalId == 0) await _api.KanalOlusturAsync(g);
        else await _api.KanalGuncelleAsync(DuzenKanalId, g);
        YeniKanal();
        KanalOnayiniSifirla();
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
    {
        if (!ParaAyristirici.GecerliMi(KasaAcilisDevri)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (SifirOnayMetni("Kasa açılış devri", _kayitliKasaAcilisDevri, KasaAcilisDevri, _kasaSifirOnayi) is { } onay)
        {
            _kasaSifirOnayi = true; AyarUyarisi = onay; return;
        }
        var devir = KasaAcilisDevri;
        await _api.AyarGuncelleAsync(new AyarYaz(DateOnly.FromDateTime(TakipBaslangic), devir));
        _kayitliKasaAcilisDevri = devir;
        AyarOnayiniSifirla();
    });

    [RelayCommand]
    private Task IzleyiciSifreKaydetAsync() => CalistirAsync(async () =>
    {
        await _api.IzleyiciSifreAsync(YeniIzleyiciSifre);
        YeniIzleyiciSifre = "";
    });
}
