using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>
/// Ayarlar › Güvenlik bölümü (editör): Hesabım, Kullanıcılar, Oturumlar ve giriş günlüğü. Her alt
/// model kendi hatasını ve meşguliyetini taşır; bu sınıf yalnız birlikte yükler.
/// </summary>
public partial class GuvenlikViewModel : ObservableObject
{
    public GuvenlikViewModel(HesapGuvenligiViewModel hesap, KullanicilarViewModel kullanicilar, OturumlarViewModel oturumlar)
    {
        Hesap = hesap;
        Kullanicilar = kullanicilar;
        Oturumlar = oturumlar;
    }

    public HesapGuvenligiViewModel Hesap { get; }
    public KullanicilarViewModel Kullanicilar { get; }
    public OturumlarViewModel Oturumlar { get; }

    public Task YukleAsync() => Task.WhenAll(Hesap.YukleAsync(), Kullanicilar.YukleAsync(), Oturumlar.YukleAsync());
}
