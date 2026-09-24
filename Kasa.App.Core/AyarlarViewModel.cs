using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Editör ayarları: kanal, sabit gider kalemi ve tekrarlayan gider CRUD + izleyici şifre + takip başlangıç/açılış devri (spec §6).</summary>
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
        var tekrarGorevi = TekrarlayanYukleme.Oku(_api.TekrarlayanGiderlerAsync);
        var ayar = await ayarGorevi;
        var kanallar = await kanalGorevi;
        var kalemler = await kalemGorevi;
        var tekrarlar = await tekrarGorevi;
        GiderKalemleri.Clear();
        foreach (var k in kalemler) GiderKalemleri.Add(k);
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = ayar.KasaAcilisDevri;
        Kanallar.Clear();
        foreach (var k in kanallar) Kanallar.Add(k);
        TekrarlayanGiderler.Clear();
        foreach (var t in tekrarlar) TekrarlayanGiderler.Add(new TekrarlayanGiderSatiri(t));
        TekrarCipleriniKur();
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

    // ---- Tekrarlayan giderler ----

    public const string OrtakKanal = "Ortak";

    public ObservableCollection<TekrarlayanGiderSatiri> TekrarlayanGiderler { get; } = new();
    /// <summary>Kalem çipleri: aktif sabit gider kalemleri (+ düzenlenen kaydın pasif kalemi).</summary>
    public ObservableCollection<SecimCipi> TekrarKalemCipleri { get; } = new();
    /// <summary>Kanal çipleri: aktif kanallar + "Ortak" (+ düzenlenen kaydın pasif kanalı).</summary>
    public ObservableCollection<SecimCipi> TekrarKanalCipleri { get; } = new();
    /// <summary>Seçilecek kalem yoksa form yerine "önce kalem ekleyin" ipucu gösterilir.</summary>
    public bool TekrarKalemYok => TekrarKalemCipleri.Count == 0;

    [ObservableProperty] private int _duzenTekrarId;      // 0 = yeni
    [ObservableProperty] private string _duzenTekrarKalem = "";
    [ObservableProperty] private string _duzenTekrarKanal = "";
    [ObservableProperty] private decimal _duzenTekrarTutar;
    [ObservableProperty] private int _duzenTekrarGun = 1;
    [ObservableProperty] private bool _duzenTekrarAktif = true;

    public const string KalemSecinMesaji = "Gider kalemi seçin.";
    public const string KanalSecinMesaji = "Kanal seçin.";
    public const string TutarMesaji = "Tutar sıfırdan büyük olmalı.";
    public const string GunMesaji = "Ayın günü 1 ile 31 arasında olmalı.";

    [RelayCommand] private void SecTekrarKalem(SecimCipi s) => DuzenTekrarKalem = s.Ad;
    [RelayCommand] private void SecTekrarKanal(SecimCipi s) => DuzenTekrarKanal = s.Ad;

    partial void OnDuzenTekrarKalemChanged(string value)
    {
        foreach (var c in TekrarKalemCipleri) c.Secili = c.Ad == value;
    }

    partial void OnDuzenTekrarKanalChanged(string value)
    {
        foreach (var c in TekrarKanalCipleri) c.Secili = c.Ad == value;
    }

    private void TekrarCipleriniKur()
    {
        var kalemler = GiderKalemleri.Where(k => k.Aktif).Select(k => k.Ad).ToList();
        if (DuzenTekrarKalem.Length > 0 && !kalemler.Contains(DuzenTekrarKalem)
            && GiderKalemleri.Any(k => k.Ad == DuzenTekrarKalem))
            kalemler.Add(DuzenTekrarKalem);
        TekrarKalemCipleri.Clear();
        foreach (var ad in kalemler) TekrarKalemCipleri.Add(new SecimCipi(ad) { Secili = ad == DuzenTekrarKalem });
        OnPropertyChanged(nameof(TekrarKalemYok));

        var kanallar = Kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad).ToList();
        if (DuzenTekrarKanal.Length > 0 && DuzenTekrarKanal != OrtakKanal && !kanallar.Contains(DuzenTekrarKanal)
            && Kanallar.Any(k => k.Ad == DuzenTekrarKanal))
            kanallar.Add(DuzenTekrarKanal);
        kanallar.Add(OrtakKanal);
        TekrarKanalCipleri.Clear();
        foreach (var ad in kanallar) TekrarKanalCipleri.Add(new SecimCipi(ad) { Secili = ad == DuzenTekrarKanal });
    }

    [RelayCommand]
    private void YeniTekrar()
    {
        DuzenTekrarId = 0; DuzenTekrarKalem = ""; DuzenTekrarKanal = "";
        DuzenTekrarTutar = 0; DuzenTekrarGun = 1; DuzenTekrarAktif = true;
        TekrarCipleriniKur();   // düzenlemede eklenen pasif kalem/kanal çipi kalksın
    }

    [RelayCommand]
    public void TekrarDuzenle(TekrarlayanGiderSatiri s)
    {
        var g = s.Gider;
        DuzenTekrarId = g.Id; DuzenTekrarKalem = g.Kalem; DuzenTekrarKanal = g.Kanal;
        DuzenTekrarTutar = g.Tutar; DuzenTekrarGun = g.AyinGunu; DuzenTekrarAktif = g.Aktif;
        TekrarCipleriniKur();   // pasif kalem/kanal da seçili görünsün
    }

    /// <summary>
    /// Yeni kayıt bu aydan başlar (başlangıç ayını sunucu Türkiye saatine göre koyar);
    /// güncellemede başlangıç ayı değişmez.
    /// </summary>
    [RelayCommand]
    private Task TekrarKaydetAsync() => CalistirAsync(async () =>
    {
        Dogrula(DuzenTekrarKalem.Trim().Length > 0, KalemSecinMesaji);
        Dogrula(DuzenTekrarKanal.Trim().Length > 0, KanalSecinMesaji);
        Dogrula(DuzenTekrarTutar > 0, TutarMesaji);
        Dogrula(DuzenTekrarGun is >= 1 and <= 31, GunMesaji);
        var g = new TekrarlayanGiderYaz(DuzenTekrarKalem.Trim(), DuzenTekrarKanal.Trim(), DuzenTekrarTutar, DuzenTekrarGun, DuzenTekrarAktif);
        if (DuzenTekrarId == 0) await _api.TekrarlayanGiderOlusturAsync(g);
        else await _api.TekrarlayanGiderGuncelleAsync(DuzenTekrarId, g);
        YeniTekrar();
        await DoldurAsync();
    });

    /// <summary>Kaydı ve ay kararlarını siler; daha önce girilmiş işlemler kalır.</summary>
    [RelayCommand]
    private Task TekrarSilAsync(TekrarlayanGiderSatiri s) => CalistirAsync(async () =>
    {
        await _api.TekrarlayanGiderSilAsync(s.Id);
        if (DuzenTekrarId == s.Id) YeniTekrar();
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
