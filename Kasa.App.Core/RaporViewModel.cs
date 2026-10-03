using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir; yüklenirken/eski veride finansal rakamları gizler. Yeni yükleme
/// ve ekrandan ayrılma süren isteği iptal eder (istek ağda da bırakılır, sunucu hesabı keser); iptal hata sayılmaz.
/// Yürütme yürütücünün son istek hattıdır (<see cref="SonIstekHatti"/>).</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private readonly SonIstekHatti _hat;
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    private readonly BaglantiDurumu? _baglanti;

    /// <param name="baglanti">Uygulamanın bağlantı durumu; kopukken okumanın bağlantı hatası sayfaya yazılmaz.</param>
    protected RaporViewModel(BaglantiDurumu? baglanti = null)
    {
        _baglanti = baglanti;
        _hat = new SonIstekHatti(Yurutucu);
    }

    protected override bool BaglantiKopuk => _baglanti?.Kopuk == true;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
        : "Veriler henüz yüklenmedi.";

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();

    /// <summary>Ekrandan ayrılınca süren rapor isteği iptal edilir; sonucu ve hatası ekrana yansımaz.</summary>
    public void EkrandanAyril()
    {
        _hat.Birak();
        Mesgul = false;
    }

    /// <summary>İptal belirteci almayan çağrılar için: yalnız son isteğin sonucu uygulanır.</summary>
    protected Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula) => RaporYukleAsync(_ => getir(), uygula);

    protected Task RaporYukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
    {
        VeriVar = false;
        return _hat.YukleAsync(getir, veri =>
        {
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriVar = true;
        });
    }
}
