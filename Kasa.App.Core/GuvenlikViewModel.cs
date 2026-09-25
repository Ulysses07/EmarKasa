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
        YedekBilgisi = $"Otomatik yedek: {(y.OtomatikEtkin ? "açık" : "kapalı")}\nSon yedek: {Zaman(y.SonYedek)}\nSon doğrulama: {Zaman(y.SonDogrulama)}" + (string.IsNullOrWhiteSpace(y.Hata) ? "" : "\n" + y.Hata);
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
    public void EkrandanAyril() { BekleyenleriIptalEt(); Temizle(); }
    protected override void OturumTemizle() { Temizle(); IndirmeAdresi = null; YedekBilgisi = "Yedek durumu henüz alınmadı."; SurumBilgisi = $"Uygulama {IstemciSurumu}"; }
    public async Task<IndirilenDosya?> YedekIndirAsync()
    {
        IndirilenDosya? sonuc = null;
        await YurutAsync(async n => { var d = await api.YedekIndirAsync(); if (Gecerli(n)) sonuc = d; });
        return sonuc;
    }
}
