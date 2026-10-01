using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Windows bildirimi gösteren platform katmanı (Kasa.App WindowsBildirimGosterici: AppNotificationManager). Kasa.App.Core
/// Windows'a bağlı değildir; testler sahtesini kullanır.</summary>
public interface IBildirimGosterici
{
    /// <summary>Sunucu bildirimini gösterir: başlık <see cref="BildirimDto.Baslik"/>, metin <see cref="BildirimDto.Mesaj"/>; tıklama
    /// argümanları kimlik ve hedeftir (<see cref="BildirimTiklamasi"/>).</summary>
    void Goster(BildirimDto bildirim);

    /// <summary>Sunucuya gitmeden örnek bildirim gösterir; gösterilemezse false.</summary>
    bool DenemeGoster();

    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı (ya da desteklenmiyor).</summary>
    bool WindowsAyarindaKapali { get; }
}

/// <summary>Bildirime tıklama: sunucu bildiriminin kimliği (deneme bildiriminde yok) ve hedefi (BildirimDto.Hedef).</summary>
public sealed record BildirimTiklamasi(int? BildirimId, string? Hedef)
{
    /// <summary>Windows bildirim argümanı: sunucu bildiriminin kimliği.</summary>
    public const string KimlikAnahtari = "bildirim";
    /// <summary>Windows bildirim argümanı: sunucu bildiriminin hedefi (web rotası).</summary>
    public const string HedefAnahtari = "hedef";
    /// <summary>Deneme bildiriminin hedefi: Bildirimler sayfası (tanınmayan hedef).</summary>
    public const string DenemeHedefi = "/#notifications";

    /// <summary>Bildirim argümanlarından (AppNotificationActivatedEventArgs.Arguments) okur; eksik ya da geçersiz kimlik null'dır.</summary>
    public static BildirimTiklamasi Coz(IEnumerable<KeyValuePair<string, string>> argumanlar)
    {
        int? kimlik = null;
        string? hedef = null;
        foreach (var (anahtar, deger) in argumanlar)
        {
            if (anahtar == KimlikAnahtari && int.TryParse(deger, NumberStyles.None, CultureInfo.InvariantCulture, out var sayi) && sayi > 0)
                kimlik = sayi;
            else if (anahtar == HedefAnahtari)
                hedef = deger;
        }
        return new BildirimTiklamasi(kimlik, hedef);
    }
}

/// <summary>Bekleyen bildirim tıklaması: tıklama çalışan uygulamada olayla ya da uygulama tıklamayla başlarken gelir; kabuk (AppShell)
/// oturum açıkken <see cref="Al"/> ile alır ve uygular. Tek yer vardır: son tıklama geçerlidir.</summary>
public sealed class BildirimTiklamalari
{
    private readonly Lock _kilit = new();
    private BildirimTiklamasi? _bekleyen;

    /// <summary>Yeni tıklama geldi (Ekle'yi çağıran iş parçacığında).</summary>
    public event EventHandler? Istendi;

    public void Ekle(BildirimTiklamasi tiklama)
    {
        lock (_kilit)
            _bekleyen = tiklama;
        Istendi?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bekleyen tıklamayı alır ve temizler (yoksa null).</summary>
    public BildirimTiklamasi? Al()
    {
        lock (_kilit)
        {
            var tiklama = _bekleyen;
            _bekleyen = null;
            return tiklama;
        }
    }
}
