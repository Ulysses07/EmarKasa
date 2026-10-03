using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir. Yenileme ve hata son başarılı veriyi silmez (tasarım 2026-10-02 §3):
/// hata verirse veri eski işaretlenir (<see cref="VeriEski"/>, soluk gösterilir); başka sorguya geçen alt sınıf (Aylık'ta ay)
/// eski sorgunun verisini kendisi kaldırır. Yeni yükleme ve ekrandan ayrılma süren isteği iptal eder (istek ağda da bırakılır,
/// sunucu hesabı keser); iptal hata sayılmaz. Yürütme yürütücünün son istek hattıdır (<see cref="SonIstekHatti"/>).</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private readonly SonIstekHatti _hat;
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private bool _veriEski;

    /// <summary>Her başarılı yüklemeden sonra (değerler yazıldıktan sonra); Kasalar alt bölümleri bunu dinler.</summary>
    public event EventHandler? Yuklendi;

    private readonly BaglantiDurumu? _baglanti;
    private readonly AuthViewModel? _auth;

    /// <summary>Ekranda veri yokken son veri önbelleğinden (aynı oturum, aynı sorgu) eski veri gösterildi (ekran denemesi H-1);
    /// Kasalar alt bölümleri kendi son verilerini bununla gösterir. Başarılı yüklemede <see cref="Yuklendi"/> gelir.</summary>
    public event EventHandler? OnbellektenGosterildi;

    /// <param name="baglanti">Uygulamanın bağlantı durumu; kopukken okumanın bağlantı hatası sayfaya yazılmaz.</param>
    /// <param name="auth">Verilirse oturum değişince (çıkış, yeni giriş; <see cref="AuthViewModel.OturumSurumu"/>) son başarılı
    /// veri sıfırlanır (K-4): başka kullanıcının bakiyeleri görünmez.</param>
    protected RaporViewModel(BaglantiDurumu? baglanti = null, AuthViewModel? auth = null)
    {
        _baglanti = baglanti;
        _auth = auth;
        _hat = new SonIstekHatti(Yurutucu);
        if (auth is not null)
            OturumDeginceSifirla(auth);
    }

    /// <summary>Oturum değişince (K-4) son başarılı veriyi sıfırlar; bildirim modelin kurulduğu UI bağlamına (gerekirse Post ile)
    /// gelir (OturumluViewModel.OturumDegisiminiDinle ile aynı desen).</summary>
    private void OturumDeginceSifirla(AuthViewModel auth)
    {
        var ui = SynchronizationContext.Current;
        void UiBaglaminda(Action eylem)
        {
            if (ui is not null && SynchronizationContext.Current != ui)
                ui.Post(_ => eylem(), null);
            else
                eylem();
        }
        auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.OturumSurumu))
                UiBaglaminda(SonVeriyiSifirla);
        };
    }

    /// <summary>Oturum değişince son başarılı veriyi sıfırlar (K-4). Alt sınıf kendi verisini (Rapor, Donemler, …) de temizlemek
    /// için geçersiz kılabilir; temel uygulama görünürlüğü ve zaman damgasını sıfırlar.</summary>
    protected virtual void SonVeriyiSifirla()
    {
        VeriVar = false;
        VeriEski = false;
        SonGuncelleme = null;
    }

    protected override bool BaglantiKopuk => _baglanti?.Kopuk == true;

    /// <summary>Takip sayfalarıyla aynı biçim (<see cref="Bicim.SonGuncelleme"/>).</summary>
    public string SonGuncellemeMetni => Bicim.SonGuncelleme(SonGuncelleme, VeriEski);

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();

    /// <summary>Ekrandan ayrılınca süren rapor isteği iptal edilir; sonucu ve hatası ekrana yansımaz.</summary>
    public void EkrandanAyril()
    {
        _hat.Birak();
        Mesgul = false;
    }

    /// <summary>İptal belirteci almayan çağrılar için: yalnız son isteğin sonucu uygulanır.</summary>
    protected Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula) where T : notnull => RaporYukleAsync(_ => getir(), uygula);
    protected Task RaporYukleAsync<T>(string? sorgu, Func<Task<T>> getir, Action<T> uygula) where T : notnull
        => RaporYukleAsync(sorgu, _ => getir(), uygula);

    protected Task RaporYukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula) where T : notnull
        => RaporYukleAsync(null, getir, uygula);

    /// <param name="sorgu">Verilirse (ve model oturumla kurulduysa) başarılı yanıt son veri önbelleğine bu sorgu anahtarıyla yazılır;
    /// ekranda veri yokken aynı sorgunun önbellekteki verisi istekten önce eski (soluk) gösterilir (ekran denemesi H-1).</param>
    protected Task RaporYukleAsync<T>(string? sorgu, Func<CancellationToken, Task<T>> getir, Action<T> uygula) where T : notnull
    {
        var anahtar = sorgu is null ? null : GetType().Name + "|" + sorgu;
        if (anahtar is not null && SonGuncelleme is null && _auth?.SonVeri.Oku<T>(anahtar, out var onceki, out var zaman) == true)
        {
            uygula(onceki);
            SonGuncelleme = zaman;
            VeriEski = true;
            VeriVar = true;
            OnbellektenGosterildi?.Invoke(this, EventArgs.Empty);
        }
        return _hat.YukleAsync(getir, veri =>
        {
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriEski = false;
            VeriVar = true;
            if (anahtar is not null)
                _auth?.SonVeri.Yaz(anahtar, veri, SonGuncelleme.Value);
            Yuklendi?.Invoke(this, EventArgs.Empty);
        }, hata =>
        {
            Yurutucu.OkumaHatasiniYaz(hata);
            VeriEski = VeriVar;
        });
    }
}
