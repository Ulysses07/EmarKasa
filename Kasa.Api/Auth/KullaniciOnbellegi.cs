using System.Security.Claims;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

/// <summary>
/// Kullanıcıların oturum için gereken özetini (ad, rol, aktiflik, oturum sürümü) ve editör oturum
/// süresini bellekte tutar:
/// token doğrulaması her istekte DB'ye gitmesin. Tek süreç varsayımıyla çalışır (bkz.
/// <see cref="OturumOnbellegi"/>); kullanıcıyı değiştiren her uç işlem bittikten sonra
/// <see cref="Gecersiz"/> çağırır, liste ilk ihtiyaçta yeniden okunur.
/// </summary>
public sealed class KullaniciOnbellegi
{
    public sealed record Ozet(int Id, string AdSoyad, string KullaniciAdi, string Rol, bool Aktif, int OturumSurumu, bool Yerlesik);

    private sealed record Anlik(IReadOnlyDictionary<int, Ozet> Liste, Ozet? Yerlesik, int EditorOturumGun);

    private volatile Anlik? _anlik;

    public void Yukle(KasaDbContext db)
    {
        var liste = db.Kullanicilar.AsNoTracking()
            .Select(k => new Ozet(k.Id, k.AdSoyad, k.KullaniciAdi, k.Rol, k.Aktif, k.OturumSurumu, k.Yerlesik))
            .ToList();
        var gun = db.GuvenlikAyarlari.AsNoTracking().OrderBy(a => a.Id).Select(a => (int?)a.EditorOturumGun).FirstOrDefault()
                  ?? GuvenlikKurallari.VarsayilanOturumGun;
        _anlik = new Anlik(liste.ToDictionary(k => k.Id), liste.FirstOrDefault(k => k.Yerlesik), gun);
    }

    /// <summary>Kullanıcı tablosu ya da güvenlik ayarı değişti: bir sonraki okumada yeniden yüklenir.</summary>
    public void Gecersiz() => _anlik = null;

    private Anlik Al(KasaDbContext db)
    {
        var a = _anlik;
        if (a is not null) return a;
        Yukle(db);
        return _anlik!;
    }

    public Ozet? Bul(KasaDbContext db, int id) => Al(db).Liste.GetValueOrDefault(id);

    /// <summary>.env editörünün satırı (editör yapılandırılmamışsa null).</summary>
    public Ozet? Yerlesik(KasaDbContext db) => Al(db).Yerlesik;

    /// <summary>Editör oturumunun en uzun süresi (gün; güvenlik ayarı).</summary>
    public int EditorOturumGun(KasaDbContext db) => Al(db).EditorOturumGun;

    /// <summary>
    /// Token'ın ait olduğu hesap: kişi claim'i varsa o; yoksa editör token'ı (bu sürümden önce verilmiş)
    /// yerleşik editöre aittir. Ortak izleyici şifresiyle alınmış token'da null.
    /// </summary>
    public int? HesapId(KasaDbContext db, ClaimsPrincipal? u)
    {
        if (u is null) return null;
        if (int.TryParse(u.FindFirstValue(KimlikClaimleri.KullaniciId), out var id)) return id;
        return u.FindFirstValue(ClaimTypes.Role) == Roller.Editor ? Yerlesik(db)?.Id : null;
    }
}

/// <summary>HTTP isteğindeki kimlikten geçmiş için kişi adı ve cihaz.</summary>
public static class KimlikBilgisi
{
    public static string? Ad(HttpContext? h)
    {
        if (h?.User is not { Identity.IsAuthenticated: true } u) return null;
        var onbellek = h.RequestServices.GetService<KullaniciOnbellegi>();
        var db = h.RequestServices.GetService<KasaDbContext>();
        if (onbellek is not null && db is not null && onbellek.HesapId(db, u) is { } id && onbellek.Bul(db, id) is { } k)
            return k.AdSoyad;
        return u.FindFirstValue(KimlikClaimleri.Ad);
    }

    /// <summary>Token'daki cihaz; bu sürümden önce verilmiş token'da yoksa isteğin <c>X-Kasa-Cihaz</c> başlığı.</summary>
    public static string? Cihaz(HttpContext? h)
        => h?.User is { Identity.IsAuthenticated: true } u ? u.FindFirstValue(KimlikClaimleri.Cihaz) ?? CihazAdi.Oku(h.Request) : null;
}
