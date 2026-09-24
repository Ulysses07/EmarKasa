using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Kişisel giriş ve iki adımlı giriş (paket E). Şifre doğru ama hesapta iki adımlı giriş açıksa sunucu
/// token vermez, kod ister: <see cref="KodGerekli"/> true olur, giriş ekranı kod alanını gösterir ve
/// aynı Giriş düğmesi kullanıcı adı + şifre + kodla yeniden dener. Kod alanına kurtarma kodu da yazılabilir.
/// </summary>
public partial class AuthViewModel
{
    /// <summary>Şifre kabul edildi, iki adımlı giriş kodu bekleniyor.</summary>
    [ObservableProperty] private bool _kodGerekli;
    /// <summary>Kimlik doğrulayıcıdaki 6 haneli kod ya da bir kurtarma kodu ("ABCDE-FGHJK").</summary>
    [ObservableProperty] private string? _kod;
    /// <summary>Kod alanının üstündeki açıklama (sunucunun mesajı: "kodu girin" ya da kilit uyarısı).</summary>
    [ObservableProperty] private string? _kodBilgisi;

    /// <summary>Oturumdaki kişinin adı (ortak izleyici şifresinde null).</summary>
    [ObservableProperty] private string? _aktifAd;
    [ObservableProperty] private int? _aktifKullaniciId;

    /// <summary>Menü altındaki kimlik: "EMAR · Editör" ya da "İzleyici".</summary>
    public string AktifKimlik => string.IsNullOrWhiteSpace(AktifAd) ? RolAdi(AktifRol) : $"{AktifAd} · {RolAdi(AktifRol)}";

    public static string RolAdi(Rol rol) => rol == Rol.Editor ? "Editör" : "İzleyici";

    partial void OnAktifAdChanged(string? value) => OnPropertyChanged(nameof(AktifKimlik));
    partial void OnAktifRolChanged(Rol value) => OnPropertyChanged(nameof(AktifKimlik));

    partial void OnGirisYapildiChanged(bool value)
    {
        if (value) return;
        // Çıkış / oturum bitişi: kişi bilgisi ve yarım kalmış kod adımı temizlenir.
        KodGerekli = false;
        Kod = null;
        KodBilgisi = null;
        AktifAd = null;
        AktifKullaniciId = null;
    }

    /// <summary>
    /// Sunucuya giriş isteği. Kod isteniyorsa null döner (ekran kod adımına geçer); kod yazılmış ama
    /// kabul edilmemişse sunucunun mesajı <see cref="AuthViewModel.Hata"/>'ya yazılır.
    /// </summary>
    private async Task<GirisSonucu?> GirisIsteAsync()
    {
        var kod = KodGerekli && !string.IsNullOrWhiteSpace(Kod) ? Kod.Trim() : null;
        var sonuc = await _api.GirisAsync(string.IsNullOrWhiteSpace(Kullanici) ? null : Kullanici.Trim(), Sifre, kod);
        if (sonuc.KodGerekli)
        {
            if (kod is not null) Hata = sonuc.Mesaj ?? "Kod hatalı.";
            else KodBilgisi = sonuc.Mesaj ?? "İki adımlı giriş kodunu girin.";
            KodGerekli = true;
            Kod = null;
            return null;
        }
        KodGerekli = false;
        Kod = null;
        KodBilgisi = null;
        AktifAd = sonuc.Ad;
        AktifKullaniciId = sonuc.KullaniciId;
        return sonuc;
    }

    /// <summary>Açılışta /me: rolü döner, kişi adını da tutar.</summary>
    private async Task<string?> BenOkuAsync()
    {
        var ben = await _api.BenAsync();
        AktifAd = ben.Ad;
        AktifKullaniciId = ben.KullaniciId;
        return ben.Rol;
    }

    /// <summary>Kod adımından şifre adımına dön (başka hesapla girmek için).</summary>
    [RelayCommand]
    private void KodIptal()
    {
        KodGerekli = false;
        Kod = null;
        KodBilgisi = null;
        Hata = null;
        Sifre = "";
    }
}
