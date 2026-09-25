using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Login + açılış /me doğrulama + çıkış (spec §5).</summary>
public partial class AuthViewModel : ObservableObject
{
    private readonly IKasaApi _api;

    public AuthViewModel(IKasaApi api)
    {
        _api = api;
        if (api is IOturumBildirimleri bildirimler)
            bildirimler.OturumSonlandi += (_, _) =>
            {
                OturumSurumu++;
                GirisYapildi = false;
                Sifre = "";
                KurtarmaAlanlariniTemizle();
                Hata = "Oturumunuz sona erdi. Yeniden giriş yapın.";
                OturumSonlandi?.Invoke(this, EventArgs.Empty);
            };
    }

    public event EventHandler? OturumSonlandi;

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private bool _girisYapildi;
    [ObservableProperty] private Rol _aktifRol;
    [ObservableProperty] private int _oturumSurumu;
    [ObservableProperty] private bool _kurtarmaAcik;
    [ObservableProperty] private string _kurtarmaKodu = "";
    [ObservableProperty] private string _kurtarmaYeniSifre = "";
    [ObservableProperty] private string? _kurtarmaMesaji;

    [RelayCommand] private void KurtarmayiAcKapat() { KurtarmaAcik = !KurtarmaAcik; KurtarmaKodu = ""; KurtarmaYeniSifre = ""; }
    [RelayCommand] private async Task SifreKurtarAsync()
    {
        if (Mesgul || _api is not IYonetimApi yonetim) return;
        Hata = null; KurtarmaMesaji = null; Mesgul = true;
        var nesil = OturumSurumu;
        try
        {
            if (string.IsNullOrWhiteSpace(Kullanici) || string.IsNullOrWhiteSpace(KurtarmaKodu) || KurtarmaYeniSifre.Length < 12 || KurtarmaYeniSifre.Length > 1024)
            { Hata = "Kullanıcı adını, kurtarma kodunu ve 12–1024 karakterli yeni şifreyi yazın."; return; }
            await yonetim.SifreKurtarAsync(new(Kullanici.Trim(), KurtarmaKodu.Trim(), KurtarmaYeniSifre));
            if (nesil != OturumSurumu) return;
            KurtarmaKodu = ""; KurtarmaYeniSifre = ""; Sifre = ""; KurtarmaAcik = false;
            KurtarmaMesaji = "Şifreniz yenilendi. Yeni şifreyle giriş yapın.";
        }
        catch (KasaApiException ex) { if (nesil == OturumSurumu) Hata = ex.Message; }
        catch (HttpRequestException) { if (nesil == OturumSurumu) Hata = "Sunucuya ulaşılamadı. Bağlantınızı kontrol edin."; }
        catch (Exception) { if (nesil == OturumSurumu) Hata = "Şifre yenilenemedi. Yeniden deneyin."; }
        finally { if (nesil == OturumSurumu) Mesgul = false; }
    }
    private void KurtarmaAlanlariniTemizle() { KurtarmaKodu = ""; KurtarmaYeniSifre = ""; KurtarmaAcik = false; KurtarmaMesaji = null; }

    [RelayCommand]
    private async Task GirisAsync()
    {
        Hata = null;
        Mesgul = true;
        try
        {
            var yanit = await _api.LoginAsync(string.IsNullOrWhiteSpace(Kullanici) ? null : Kullanici, Sifre);
            AktifRol = SekmeModeli.RolCoz(yanit.Rol);
            OturumSurumu++;
            GirisYapildi = true;
            Sifre = "";
            KurtarmaAlanlariniTemizle();
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
            OturumSurumu++;
            GirisYapildi = true;
            KurtarmaAlanlariniTemizle();
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
        OturumSurumu++;
        KurtarmaAlanlariniTemizle();
        Mesgul = false;
        try { await _api.CikisAsync(); }
        catch (Exception) { /* çıkışta hata önemsiz */ }
        GirisYapildi = false;
    }
}
