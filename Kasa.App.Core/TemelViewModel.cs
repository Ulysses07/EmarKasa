using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Ortak Mesgul + Hata durumu ve güvenli çalıştırma sarmalayıcısı.</summary>
public partial class TemelViewModel : ObservableObject
{
    private readonly TimeProvider _zaman;

    protected TemelViewModel(TimeProvider? zaman = null) => _zaman = zaman ?? TimeProvider.System;

    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _hata;

    /// <summary>Aynı anda süren işlem sayısı: Mesgul ancak hepsi bitince iner.</summary>
    private int _aktifIslem;

    /// <summary>Yerel saatle bugünün tarihi (testlerde sabitlenebilir).</summary>
    protected DateTime Bugun => _zaman.GetLocalNow().Date;
    protected DateOnly BugunTarih => DateOnly.FromDateTime(Bugun);
    protected TimeProvider Zaman => _zaman;

    /// <summary>İşlemi Mesgul/Hata sarmalayıcısında çalıştırır; istisnada Hata yazılır.</summary>
    protected async Task CalistirAsync(Func<Task> islem)
    {
        Hata = null;
        _aktifIslem++;
        Mesgul = true;
        try { await islem(); }
        catch (Exception ex) { Hata = HataMesaji.Coz(ex); }
        finally
        {
            _aktifIslem--;
            Mesgul = _aktifIslem > 0;
        }
    }

    /// <summary>İstemci doğrulaması: mesaj <see cref="Hata"/>'ya yazılır, işlem durur.</summary>
    protected static void Dogrula(bool kosul, string mesaj)
    {
        if (!kosul) throw new DogrulamaHatasi(mesaj);
    }
}
