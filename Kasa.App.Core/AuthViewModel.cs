using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Login + açılış /me doğrulama + çıkış + merkezi oturum bitişi (spec §5).
/// <see cref="GirisYapildi"/> uygulamanın tek oturum durumudur: true → menü açılır,
/// false → menü kapanır ve Login'e dönülür (Shell bu property'yi dinler).
/// </summary>
public partial class AuthViewModel : ObservableObject
{
    private readonly IKasaApi _api;

    public AuthViewModel(IKasaApi api)
    {
        _api = api;
        _api.OturumSonaErdi += (_, neden) => OturumSonaErdi(neden);
    }

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private bool _girisYapildi;
    [ObservableProperty] private Rol _aktifRol;

    /// <summary>Açılışta sunucuya ulaşılamadı: Login yerine "çevrimdışı · tekrar dene" gösterilir.</summary>
    [ObservableProperty] private bool _cevrimdisi;

    [RelayCommand]
    private async Task GirisAsync()
    {
        Hata = null;
        Mesgul = true;
        try
        {
            var yanit = await _api.LoginAsync(string.IsNullOrWhiteSpace(Kullanici) ? null : Kullanici, Sifre);
            AktifRol = SekmeModeli.RolCoz(yanit.Rol);
            Cevrimdisi = false;
            Sifre = "";
            GirisYapildi = true;
        }
        catch (Exception ex)
        {
            Hata = HataMesaji.GirisIcin(ex);
        }
        finally
        {
            Mesgul = false;
        }
    }

    /// <summary>
    /// Açılışta store'daki token'ı /me ile doğrular. true = geçerli oturum.
    /// Sunucuya ulaşılamazsa <see cref="Cevrimdisi"/> true olur (token silinmez; tekrar denenebilir).
    /// </summary>
    public async Task<bool> AcilistaDogrulaAsync()
    {
        try
        {
            var rol = await _api.BenKimAsync();
            AktifRol = SekmeModeli.RolCoz(rol);
            Cevrimdisi = false;
            Hata = null;
            GirisYapildi = true;
            return true;
        }
        catch (Exception ex) when (HataMesaji.BaglantiHatasiMi(ex))
        {
            Cevrimdisi = true;
            GirisYapildi = false;
            return false;
        }
        catch (Exception)
        {
            Cevrimdisi = false;
            GirisYapildi = false;
            return false;
        }
    }

    /// <summary>Çevrimdışı ekranındaki "Tekrar dene".</summary>
    [RelayCommand]
    private async Task TekrarDeneAsync()
    {
        Mesgul = true;
        try { await AcilistaDogrulaAsync(); }
        finally { Mesgul = false; }
    }

    /// <summary>Çevrimdışı ekranından normal giriş formuna geç.</summary>
    [RelayCommand]
    private void GirisFormunaGec() => Cevrimdisi = false;

    /// <summary>Çıkış: sunucuya ulaşılamasa da yerel oturum kapanır (token istemcide silinir).</summary>
    public async Task CikisAsync()
    {
        try { await _api.CikisAsync(); }
        catch (Exception) { /* çıkışta sunucu hatası önemsiz; token zaten silindi */ }
        Hata = null;
        GirisYapildi = false;
    }

    private void OturumSonaErdi(OturumBitisNedeni neden)
    {
        // Açılışta kayıtlı token reddedilirse de (GirisYapildi zaten false) mesaj Login'de görünür.
        Hata = neden == OturumBitisNedeni.OturumlarKapatildi ? HataMesaji.OturumlarKapatildi : HataMesaji.OturumSonaErdi;
        Sifre = "";
        GirisYapildi = false;
    }
}
