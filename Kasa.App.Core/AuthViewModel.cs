using System.Net;
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
            bildirimler.OturumSonlandi += (_, e) =>
            {
                OturumSurumu++;
                GirisYapildi = false;
                Sifre = "";
                KurtarmaAlanlariniTemizle();
                // Kendi şifre değişikliği bir hata değildir; girişte bilgi olarak gösterilir.
                var sifreDegisti = e is OturumSonlandiEventArgs { Neden: OturumSonuNedeni.SifreDegisti };
                Hata = sifreDegisti ? null : "Oturumunuz sona erdi. Yeniden giriş yapın.";
                Bilgi = sifreDegisti ? "Şifreniz değişti. Yeni şifrenizle giriş yapın." : null;
                OturumSonlandi?.Invoke(this, EventArgs.Empty);
            };
    }

    public event EventHandler? OturumSonlandi;

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private string? _bilgi;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private bool _girisYapildi;
    [ObservableProperty] private Rol _aktifRol;
    [ObservableProperty] private int _oturumSurumu;
    [ObservableProperty] private bool _kurtarmaAcik;
    [ObservableProperty] private string _kurtarmaKodu = "";
    [ObservableProperty] private string _kurtarmaYeniSifre = "";
    /// <summary>Kurtarma kodu tek kullanımlıktır: yeni şifrenin tekrarı uyuşmazsa istek gönderilmez, kod harcanmaz.</summary>
    [ObservableProperty] private string _kurtarmaYeniSifreTekrar = "";
    [ObservableProperty] private string? _kurtarmaMesaji;
    /// <summary>Kurtarmada "Yeni şifreyi göster" kutusu (kurtarma kodu maskeli kalır). Form kapanınca, başarıda ve oturum
    /// değişiminde kapanır: bir sonraki giriş açıkta başlamaz.</summary>
    [ObservableProperty] private bool _kurtarmaSifresiniGoster;

    [RelayCommand] private void KurtarmayiAcKapat() { KurtarmaAcik = !KurtarmaAcik; KurtarmaKodu = ""; KurtarmaYeniSifre = ""; KurtarmaYeniSifreTekrar = ""; KurtarmaSifresiniGoster = false; }
    [RelayCommand] private async Task SifreKurtarAsync()
    {
        if (Mesgul || _api is not IYonetimApi yonetim) return;
        Hata = null; Bilgi = null; KurtarmaMesaji = null; Mesgul = true;
        var nesil = OturumSurumu;
        try
        {
            if (string.IsNullOrWhiteSpace(Kullanici) || string.IsNullOrWhiteSpace(KurtarmaKodu) || KurtarmaYeniSifre.Length < 12 || KurtarmaYeniSifre.Length > 1024)
            { Hata = "Kullanıcı adını, kurtarma kodunu ve 12–1024 karakterli yeni şifreyi yazın."; return; }
            if (KurtarmaYeniSifre != KurtarmaYeniSifreTekrar) { Hata = GuvenlikViewModel.YeniSifreUyusmazMesaji; return; }
            await yonetim.SifreKurtarAsync(new(Kullanici.Trim(), KurtarmaKodu.Trim(), KurtarmaYeniSifre));
            if (nesil != OturumSurumu) return;
            KurtarmaKodu = ""; KurtarmaYeniSifre = ""; KurtarmaYeniSifreTekrar = ""; Sifre = ""; KurtarmaAcik = false; KurtarmaSifresiniGoster = false;
            KurtarmaMesaji = "Şifreniz yenilendi. Yeni şifreyle giriş yapın.";
        }
        catch (KasaApiException ex)
        {
            if (nesil == OturumSurumu)
                Hata = ex.DurumKodu == HttpStatusCode.Unauthorized ? "Kullanıcı adı veya kurtarma kodu hatalı."
                    : (int)ex.DurumKodu >= 500 && ex.DurumKodu != HttpStatusCode.ServiceUnavailable ? TemelViewModel.HataKoduEkle("Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin.", ex)
                    : TemelViewModel.HataKoduEkle(ex.Message, ex);
        }
        catch (HttpRequestException) { if (nesil == OturumSurumu) Hata = "Sunucuya ulaşılamadı. Bağlantınızı kontrol edin."; }
        catch (Exception) { if (nesil == OturumSurumu) Hata = "Şifre yenilenemedi. Yeniden deneyin."; }
        finally { if (nesil == OturumSurumu) Mesgul = false; }
    }
    private void KurtarmaAlanlariniTemizle() { KurtarmaKodu = ""; KurtarmaYeniSifre = ""; KurtarmaYeniSifreTekrar = ""; KurtarmaAcik = false; KurtarmaMesaji = null; KurtarmaSifresiniGoster = false; }

    [RelayCommand]
    private async Task GirisAsync()
    {
        Hata = null;
        Bilgi = null;
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
        catch (KasaApiException ex)
        {
            // 429: sunucunun Türkçe iletisi (bekleme süresi); yeniden denemek yanlış şifre sanılmasın.
            // 5xx: bilgiler yanlış değildir; sunucu hatası iz kimliğinin kısa "Hata kodu" ile gösterilir.
            Hata = ex.DurumKodu switch
            {
                HttpStatusCode.TooManyRequests => ex.Message,
                HttpStatusCode.Unauthorized => "Kullanıcı adı veya şifre hatalı.",
                >= HttpStatusCode.InternalServerError => TemelViewModel.HataKoduEkle("Sunucu girişi tamamlayamadı. Lütfen yeniden deneyin.", ex),
                _ => "Giriş başarısız. Bilgileri kontrol edin.",
            };
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
