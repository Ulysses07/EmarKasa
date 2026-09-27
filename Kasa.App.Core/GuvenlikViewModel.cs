using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class GuvenlikViewModel(IYonetimApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public const string IstemciSurumu = "2.3.0";
    [ObservableProperty] private string _mevcutSifre = "";
    [ObservableProperty] private string _yeniSifre = "";
    [ObservableProperty] private string? _kurtarmaKodu;
    [ObservableProperty] private string _surumBilgisi = $"Uygulama {IstemciSurumu}";
    [ObservableProperty] private string _yedekBilgisi = "Yedek durumu henüz alınmadı.";
    /// <summary>Sunucunun yedek hatası ya da rotasyon uyarısı (silinemeyen eski yedek); yoksa null.</summary>
    [ObservableProperty] private string? _yedekUyarisi;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(YedekIptalCommand))]
    private bool _yedekIndiriliyor;
    private CancellationTokenSource? _yedekIptal;
    [ObservableProperty] private bool _guncellemeGerekli;
    [ObservableProperty] private string? _indirmeAdresi;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var s = await api.SurumAsync();
        if (!Gecerli(n)) return;
        GuncellemeGerekli = Version.TryParse(s.MinimumIstemci, out var minimum) && minimum > Version.Parse(IstemciSurumu);
        IndirmeAdresi = Uri.TryCreate(s.IndirmeAdresi, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.AbsoluteUri : null;
        SurumBilgisi = $"Uygulama {IstemciSurumu} · sunucu {s.Surum}" + (GuncellemeGerekli ? "\nDevam etmek için uygulamayı güncelleyin." : "") + (string.IsNullOrWhiteSpace(s.Notlar) ? "" : "\n" + s.Notlar);
        var y = await api.YedekDurumuAsync();
        if (!Gecerli(n)) return;
        YedekBilgisi = $"Otomatik yedek: {(y.OtomatikEtkin ? "açık" : "kapalı")}\nSon yedek: {Zaman(y.SonYedek)}\nSon doğrulama: {Zaman(y.SonDogrulama)}";
        var uyarilar = new[] { y.Hata, y.RotasyonUyarisi }.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        YedekUyarisi = uyarilar.Count == 0 ? null : string.Join("\n", uyarilar);
    });
    private static string Zaman(DateTimeOffset? tarih) => tarih?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Henüz yok";
    [RelayCommand] private Task SifreDegistirAsync() => YurutAsync(async n =>
    {
        Mesaj = null;
        if (string.IsNullOrWhiteSpace(MevcutSifre) || YeniSifre.Length < 12 || YeniSifre.Length > 1024) { Hata = "Mevcut şifreyi ve 12–1024 karakterli yeni şifreyi yazın."; return; }
        await api.SifreDegistirAsync(new(MevcutSifre, YeniSifre));
        if (!Gecerli(n)) return;
        Temizle(); Mesaj = "Şifre değiştirildi. Yeni şifrenizle giriş yapın.";
    });
    [RelayCommand] private Task KurtarmaKoduOlusturAsync() => YurutAsync(async n =>
    {
        Mesaj = null; KurtarmaKodu = null;
        if (string.IsNullOrWhiteSpace(MevcutSifre)) { Hata = "Mevcut şifrenizi yazın."; return; }
        var yanit = await api.KurtarmaKoduOlusturAsync(MevcutSifre);
        if (!Gecerli(n)) return;
        KurtarmaKodu = yanit.Kod;
        MevcutSifre = "";
        Mesaj = "Bu kod yalnız şimdi gösterilir. Güvenli bir yerde saklayın. Yeni kod önceki kodu geçersiz kılar.";
    });
    public void Temizle() { MevcutSifre = ""; YeniSifre = ""; KurtarmaKodu = null; }
    /// <summary>Ekrandan ayrılınca süren yedek indirmesi de iptal edilir; sonucu zaten kullanılmayacaktı.</summary>
    public void EkrandanAyril() { _yedekIptal?.Cancel(); BekleyenleriIptalEt(); Temizle(); }
    protected override void OturumTemizle() { _yedekIptal?.Cancel(); Temizle(); IndirmeAdresi = null; YedekBilgisi = "Yedek durumu henüz alınmadı."; YedekUyarisi = null; SurumBilgisi = $"Uygulama {IstemciSurumu}"; }
    /// <summary>Yedeği sunucudan belleğe almadan <paramref name="hedef"/>'e yazar. Sunucu yedeği isteğin içinde hazırladığı
    /// için büyük veritabanında dakikalar sürebilir; kullanıcı <see cref="YedekIptalCommand"/> ile vazgeçebilir.
    /// Başarıda indirme bilgisi, hata ya da iptalde null döner (hedefteki yarım içerik çağıranca silinir).</summary>
    public async Task<IndirmeBilgisi?> YedekIndirAsync(Stream hedef)
    {
        IndirmeBilgisi? sonuc = null;
        await YurutAsync(async n =>
        {
            using var iptal = new CancellationTokenSource();
            _yedekIptal = iptal; YedekIndiriliyor = true;
            try
            {
                var bilgi = await api.YedekIndirAsync(hedef, iptal.Token);
                if (!Gecerli(n)) return;
                sonuc = bilgi; Mesaj = $"Yedek indirildi ({Bicim.Boyut(bilgi.Boyut)}).";
            }
            catch (OperationCanceledException) when (iptal.IsCancellationRequested) { if (Gecerli(n)) Mesaj = "Yedek indirme iptal edildi."; }
            finally { _yedekIptal = null; YedekIndiriliyor = false; }
        });
        return sonuc;
    }
    [RelayCommand(CanExecute = nameof(YedekIndiriliyor))] private void YedekIptal() => _yedekIptal?.Cancel();
}
