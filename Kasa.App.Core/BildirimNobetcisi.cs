using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulama açıkken masaüstü bildirimlerinin yöneticisi (tasarım 2026-09-30 masaüstü bildirimleri §1-2): editör oturumu açılınca
/// zamanlanmış görevi sunucudaki bildirim saatine göre günceller ve bir kez bakar; kabuk (AppShell) <see cref="Aralik"/>ta bir
/// <see cref="TikAsync"/> çağırır. Oturum kapanınca ya da rol editör değilse rozet ve durum sıfırlanır. Bildirimler ekranının anahtarı,
/// deneme bildirimi ve saat kaydı buradan geçer. Zamanlayıcı ve pencere Kasa.App'tedir; bu sınıf Windows'tan bağımsızdır ve sınanır.
/// </summary>
public sealed class BildirimNobetcisi
{
    public static readonly TimeSpan Aralik = TimeSpan.FromMinutes(5);

    private readonly IBildirimApi _api;
    private readonly IBildirimGorevi _gorev;
    private readonly IBildirimAyari _ayar;
    private readonly IBildirimGosterici _gosterici;
    private readonly AuthViewModel _auth;

    public BildirimNobetcisi(BildirimYoklayici yoklayici, IBildirimApi api, IBildirimGorevi gorev, IBildirimAyari ayar,
        IBildirimGosterici gosterici, AuthViewModel auth)
    {
        Yoklayici = yoklayici;
        _api = api;
        _gorev = gorev;
        _ayar = ayar;
        _gosterici = gosterici;
        _auth = auth;
        auth.PropertyChanged += OturumDegisti;
    }

    public BildirimYoklayici Yoklayici { get; }

    public bool EditorOturumu => _auth.GirisYapildi && _auth.AktifRol == Rol.Editor;

    /// <summary>Bu bilgisayarda Windows bildirimleri açık.</summary>
    public bool Acik => _ayar.Acik;

    public bool WindowsAyarindaKapali => _gosterici.WindowsAyarindaKapali;

    /// <summary>Bildirimler ekranındaki durum satırı.</summary>
    public string DurumMetni => !Acik ? "Bu bilgisayarda Windows bildirimleri kapalı."
        : Yoklayici.SonSonuc?.Metin ?? "Henüz kontrol edilmedi.";

    /// <summary>Uygulama açılışında ve girişte (AppShell.MenuyuAc): editörse görev saati güncellenir ve bir kez bakılır.</summary>
    public async Task OturumAcildiAsync()
    {
        if (!EditorOturumu)
        {
            Yoklayici.Sifirla();
            return;
        }
        if (!_ayar.Acik)
            return;
        await GoreviSunucuSaatineGoreGuncelleAsync();
        await Yoklayici.YoklaAsync(EditorOturumu);
    }

    /// <summary>5 dakikalık bakma (AppShell zamanlayıcısı); editör oturumu yoksa ya da ayar kapalıysa null.</summary>
    public Task<YoklamaSonucu?> TikAsync() => Yoklayici.YoklaAsync(EditorOturumu);

    /// <summary>Bildirimler ekranının anahtarı: açınca görev kurulur ve bakılır, kapatınca görev silinir ve rozet gizlenir. Değer
    /// değişmediyse hiçbir şey yapılmaz.</summary>
    public async Task AcikAyarlaAsync(bool acik)
    {
        if (acik == _ayar.Acik)
            return;
        _ayar.Acik = acik;
        if (acik)
        {
            await GoreviSunucuSaatineGoreGuncelleAsync();
            await Yoklayici.YoklaAsync(EditorOturumu);
        }
        else
        {
            await _gorev.SilAsync();
            Yoklayici.Sifirla();
        }
    }

    /// <summary>Hatırlatma saati kaydedildi: bu bilgisayardaki görevin saati de güncellenir (ayar açıksa).</summary>
    public async Task SaatDegistiAsync(int saat, int dakika)
    {
        if (_ayar.Acik)
            await _gorev.GuncelleAsync(saat, dakika);
    }

    public bool DenemeGoster() => _gosterici.DenemeGoster();

    /// <summary>Bildirim tıklaması: okundu işaretlenir, açılacak rota döner; editör oturumu yoksa null.</summary>
    public Task<string?> TiklamayiIsleAsync(BildirimTiklamasi tiklama) => Yoklayici.TiklandiAsync(tiklama, EditorOturumu);

    private async Task GoreviSunucuSaatineGoreGuncelleAsync()
    {
        try
        {
            var ayar = await _api.BildirimAyarlariAsync();
            await _gorev.GuncelleAsync(ayar.Saat, ayar.Dakika);
        }
        catch (Exception)
        {
            // Sunucu saati alınamazsa görev bir sonraki açılışta güncellenir; bakma sürer.
        }
    }

    private void OturumDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(AuthViewModel.GirisYapildi) or nameof(AuthViewModel.AktifRol)) && !EditorOturumu)
            Yoklayici.Sifirla();
    }
}
