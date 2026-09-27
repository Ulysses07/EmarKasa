using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir; yüklenirken/eski veride finansal rakamları gizler. Yeni yükleme
/// ve ekrandan ayrılma süren isteği iptal eder (istek ağda da bırakılır, sunucu hesabı keser); iptal hata sayılmaz.</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private int _istekNo;
    // CancelAfter kullanılmadığı için kaynak Dispose gerektirmez; iptal edilen eski kaynak çöp toplayıcıya kalır.
    private CancellationTokenSource? _iptal;
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
        : "Veriler henüz yüklenmedi.";

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();

    /// <summary>Ekrandan ayrılınca süren rapor isteği iptal edilir; sonucu ve hatası ekrana yansımaz.</summary>
    public void EkrandanAyril()
    {
        Interlocked.Increment(ref _istekNo);
        Interlocked.Exchange(ref _iptal, null)?.Cancel();
        Mesgul = false;
    }

    /// <summary>İptal belirteci almayan çağrılar için: yalnız son isteğin sonucu uygulanır.</summary>
    protected Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula) => RaporYukleAsync(_ => getir(), uygula);

    protected async Task RaporYukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
    {
        var istek = Interlocked.Increment(ref _istekNo);
        var iptal = new CancellationTokenSource();
        Interlocked.Exchange(ref _iptal, iptal)?.Cancel();
        VeriVar = false;
        Hata = null;
        Mesgul = true;
        try
        {
            var veri = await getir(iptal.Token);
            if (istek != _istekNo) return;
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriVar = true;
        }
        catch (OperationCanceledException) when (iptal.IsCancellationRequested) { /* vazgeçildi: hata değil */ }
        catch (Exception hata)
        {
            if (istek == _istekNo) Hata = OkumaHataMesaji(hata);
        }
        finally
        {
            if (istek == _istekNo) Mesgul = false;
            Interlocked.CompareExchange(ref _iptal, null, iptal);
        }
    }
}
