using System.ComponentModel;
using Kasa.App.Core;

namespace Kasa.App;

/// <summary>
/// Menü görünürlüğü ve oturum yönlendirmesi. Tek doğruluk kaynağı <see cref="AuthViewModel.GirisYapildi"/>:
/// true → menü açılır (bir kez), false → menü kapanır ve Login'e dönülür (401, çıkış, tüm oturumları
/// kapat). Bildirimden gelen sayfa isteği ancak oturum açıkken uygulanır.
/// </summary>
public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;
    private readonly Yonlendirme _yonlendirme;
    private bool _menuAcik;

    public AppShell(AuthViewModel auth, Yonlendirme yonlendirme)
    {
        InitializeComponent();
        Routing.RegisterRoute(Views.KartMutabakatPage.Rota, typeof(Views.KartMutabakatPage));   // Paket D
        _auth = auth;
        _yonlendirme = yonlendirme;
        KimlikAlani.BindingContext = _auth;   // menü altında oturumdaki kişi
        _auth.PropertyChanged += AuthDegisti;
        _yonlendirme.Istendi += (_, _) => AnaIsParcaciginda(BekleyenYonlendirmeyiUygula);
        Loaded += async (_, _) => await _auth.AcilistaDogrulaAsync();   // menü GirisYapildi değişimiyle açılır
    }

    private void AuthDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AuthViewModel.GirisYapildi)) return;
        AnaIsParcaciginda(() =>
        {
            if (_auth.GirisYapildi) MenuyuAc();
            else MenuyuKapat();
        });
    }

    private static void AnaIsParcaciginda(Action eylem)
    {
        if (MainThread.IsMainThread) eylem();
        else MainThread.BeginInvokeOnMainThread(eylem);
    }

    private void MenuyuAc()
    {
        if (_menuAcik) return;
        _menuAcik = true;
        var bolumler = SekmeModeli.Bolumler(_auth.AktifRol);
        PanelItem.IsVisible = true;
        KasaSayimiItem.IsVisible = bolumler.Contains(Bolum.KasaSayimi);   // her iki rol; izleyici yalnız görür
        HaftalikItem.IsVisible = true;
        AylikItem.IsVisible = true;
        GrafiklerItem.IsVisible = CariOzetiItem.IsVisible = true;   // paket B raporları: her iki rol (salt okunur)
        CarilerItem.IsVisible = true;
        IslemlerItem.IsVisible = true;
        GelenlerItem.IsVisible = true;                                     // paket C: izleyici yalnız görür
        TopluGirisItem.IsVisible = _auth.AktifRol == Rol.Editor;           // paket C: yalnız editör yazar
        KartlarItem.IsVisible = true;
        CeklerItem.IsVisible = bolumler.Contains(Bolum.Cekler);
        SorularItem.IsVisible = bolumler.Contains(Bolum.Sorular);
        GecmisItem.IsVisible = bolumler.Contains(Bolum.Gecmis);
        AyarlarItem.IsVisible = bolumler.Contains(Bolum.Ayarlar);
        _ = GitAsync(_yonlendirme.Al() ?? "//panel");   // bildirimle açıldıysa o sayfaya
        _ = PaketABildirimleriAsync();   // Paket A: haftalık özet vb. (AppShell.A.cs)
    }

    private void MenuyuKapat()
    {
        _menuAcik = false;
        PanelItem.IsVisible = false;
        KasaSayimiItem.IsVisible = false;
        HaftalikItem.IsVisible = false;
        AylikItem.IsVisible = false;
        GrafiklerItem.IsVisible = CariOzetiItem.IsVisible = false;
        CarilerItem.IsVisible = false;
        IslemlerItem.IsVisible = false;
        GelenlerItem.IsVisible = false;
        TopluGirisItem.IsVisible = false;
        KartlarItem.IsVisible = false;
        CeklerItem.IsVisible = false;
        SorularItem.IsVisible = false;
        GecmisItem.IsVisible = false;
        AyarlarItem.IsVisible = false;
        _ = GitAsync("//login");
    }

    /// <summary>Bildirim isteği: oturum açıksa hemen git; değilse giriş sonrası MenuyuAc alır.</summary>
    private void BekleyenYonlendirmeyiUygula()
    {
        if (!_menuAcik || !_auth.GirisYapildi) return;
        if (_yonlendirme.Al() is { } rota) _ = GitAsync(rota);
    }

    private async Task GitAsync(string rota)
    {
        try { await GoToAsync(rota); }
        catch (Exception) { /* geçersiz rota / gezinme sürerken: menüden gidilebilir */ }
    }

    private async void CikisTiklandi(object? sender, EventArgs e)
    {
        await _auth.CikisAsync();          // GirisYapildi=false → MenuyuKapat
        if (_menuAcik) MenuyuKapat();      // zaten false idiyse değişim bildirilmez
    }
}
