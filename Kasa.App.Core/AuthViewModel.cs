using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Login + açılış /me doğrulama + çıkış (spec §5).</summary>
public partial class AuthViewModel : ObservableObject
{
    private readonly IKasaApi _api;

    public AuthViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private bool _girisYapildi;
    [ObservableProperty] private Rol _aktifRol;

    [RelayCommand]
    private async Task GirisAsync()
    {
        Hata = null;
        Mesgul = true;
        try
        {
            var yanit = await _api.LoginAsync(string.IsNullOrWhiteSpace(Kullanici) ? null : Kullanici, Sifre);
            AktifRol = SekmeModeli.RolCoz(yanit.Rol);
            GirisYapildi = true;
            Sifre = "";
        }
        catch (KasaApiException)
        {
            Hata = "Giriş başarısız. Bilgileri kontrol edin.";
        }
        catch (Exception)
        {
            Hata = "Sunucuya ulaşılamadı.";
        }
        finally
        {
            Mesgul = false;
        }
    }

    /// <summary>Açılışta store'daki token'ı /me ile doğrular. true = geçerli oturum.</summary>
    public async Task<bool> AcilistaDogrulaAsync()
    {
        try
        {
            var rol = await _api.BenKimAsync();
            AktifRol = SekmeModeli.RolCoz(rol);
            GirisYapildi = true;
            return true;
        }
        catch (Exception)
        {
            GirisYapildi = false;
            return false;
        }
    }

    public async Task CikisAsync()
    {
        try { await _api.CikisAsync(); }
        catch (Exception) { /* çıkışta hata önemsiz */ }
        GirisYapildi = false;
    }
}
