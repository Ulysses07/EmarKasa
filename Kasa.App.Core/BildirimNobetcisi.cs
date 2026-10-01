using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulama açıkken masaüstü bildirimlerinin yöneticisi (tasarım 2026-09-30 masaüstü bildirimleri §1-2): editör oturumu açılınca
/// zamanlanmış görevi sunucudaki bildirim saatine göre günceller ve bir kez bakar; kabuk (AppShell) <see cref="Aralik"/>ta bir
/// <see cref="TikAsync"/> çağırır. Oturum kapanınca ya da rol editör değilse rozet ve durum sıfırlanır. Bildirimler ekranının anahtarı,
/// deneme bildirimi ve saat kaydı buradan geçer. Zamanlayıcı ve pencere Kasa.App'tedir; bu sınıf Windows'tan bağımsızdır ve sınanır.
/// <para>Görev işlemleri (kurma, silme) tek sıradadır: süren kurma bitmeden başka kurma ya da silme başlamaz ve sıradaki kurma ayarı
/// yeniden okur; böylece kurma sürerken anahtar kapatılırsa sonunda görev kurulu kalmaz. Ayar kapalıyken kurulu görünen görev
/// (önceki silme başarısız olduysa) açılışta yeniden silinir.</para>
/// <para>Ömür: AuthViewModel ile aynı (DI'da ikisi de singleton); kurucu oturum olaylarına abone olur ve abonelik bırakılmaz.</para>
/// </summary>
public sealed class BildirimNobetcisi
{
    public static readonly TimeSpan Aralik = TimeSpan.FromMinutes(5);

    private readonly IBildirimApi _api;
    private readonly IBildirimGorevi _gorev;
    private readonly IBildirimAyari _ayar;
    private readonly IBildirimGosterici _gosterici;
    private readonly AuthViewModel _auth;
    /// <summary>Görev işlemlerinin sırası (bkz. sınıf belgesi).</summary>
    private readonly SemaphoreSlim _gorevSirasi = new(1, 1);

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

    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı; ayar okunamazsa false (uyarı gösterilmez).</summary>
    public bool WindowsAyarindaKapali
    {
        get
        {
            try
            {
                return _gosterici.WindowsAyarindaKapali;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Bildirimler ekranındaki durum satırı.</summary>
    public string DurumMetni => !Acik ? "Bu bilgisayarda Windows bildirimleri kapalı."
        : Yoklayici.SonSonuc?.Metin ?? "Henüz kontrol edilmedi.";

    /// <summary>Uygulama açılışında ve girişte (AppShell.MenuyuAc): ayar kapalıyken kurulu görünen görev (rolden bağımsız) silinir;
    /// editörse ve ayar açıksa görev saati güncellenir ve bir kez bakılır.</summary>
    public async Task OturumAcildiAsync()
    {
        if (!_ayar.Acik && _gorev.Kurulu)
            await SiradaAsync(GoreviKapaliysaSilAsync);
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
            Yoklayici.Sifirla();
            await SiradaAsync(GoreviKapaliysaSilAsync);
        }
    }

    /// <summary>Hatırlatma saati kaydedildi: bu bilgisayardaki görevin saati de güncellenir (ayar açıksa).</summary>
    public Task SaatDegistiAsync(int saat, int dakika) => SiradaAsync(() => GoreviKurAsync(saat, dakika));

    /// <summary>Sunucuya gitmeden örnek Windows bildirimi; gösterilemezse ya da gösterici hata verirse false.</summary>
    public bool DenemeGoster()
    {
        try
        {
            return _gosterici.DenemeGoster();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Bildirim tıklaması: okundu işaretlenir, açılacak rota döner; editör oturumu yoksa null.</summary>
    public Task<string?> TiklamayiIsleAsync(BildirimTiklamasi tiklama) => Yoklayici.TiklandiAsync(tiklama, EditorOturumu);

    private async Task GoreviSunucuSaatineGoreGuncelleAsync()
    {
        try
        {
            var ayar = await _api.BildirimAyarlariAsync();
            await SiradaAsync(() => GoreviKurAsync(ayar.Saat, ayar.Dakika));
        }
        catch (Exception)
        {
            // Sunucu saati alınamazsa görev bir sonraki açılışta güncellenir; bakma sürer.
        }
    }

    /// <summary>Görev işlemini sırayla çalıştırır (bkz. sınıf belgesi).</summary>
    private async Task SiradaAsync(Func<Task> islem)
    {
        await _gorevSirasi.WaitAsync();
        try
        {
            await islem();
        }
        finally
        {
            _gorevSirasi.Release();
        }
    }

    /// <summary>Sıra içinde: ayar hâlâ açıksa görevi kurar; bu arada kapatıldıysa kurmaz, kurulu görünen görevi siler.</summary>
    private async Task GoreviKurAsync(int saat, int dakika)
    {
        if (_ayar.Acik)
            await _gorev.GuncelleAsync(saat, dakika);
        else if (_gorev.Kurulu)
            await _gorev.SilAsync();
    }

    /// <summary>Sıra içinde: ayar hâlâ kapalıysa görevi siler (bu arada yeniden açıldıysa sıradaki kurma işi görür).</summary>
    private async Task GoreviKapaliysaSilAsync()
    {
        if (!_ayar.Acik)
            await _gorev.SilAsync();
    }

    private void OturumDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(AuthViewModel.GirisYapildi) or nameof(AuthViewModel.AktifRol)) && !EditorOturumu)
            Yoklayici.Sifirla();
    }
}
