using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir; yüklenirken/eski veride finansal rakamları gizler.</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private int _istekNo;
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
        : "Veriler henüz yüklenmedi.";

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();

    protected async Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula)
    {
        var istek = Interlocked.Increment(ref _istekNo);
        VeriVar = false;
        Hata = null;
        Mesgul = true;
        try
        {
            var veri = await getir();
            if (istek != _istekNo) return;
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriVar = true;
        }
        catch (Exception hata)
        {
            if (istek == _istekNo) Hata = HataMesaji(hata);
        }
        finally
        {
            if (istek == _istekNo) Mesgul = false;
        }
    }
}
