using System.Collections.Concurrent;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

/// <summary>
/// Token doğrulamasında her istekte DB'ye gitmemek için oturum sürümlerini ve iptal
/// edilmiş token kimliklerini (jti) bellekte tutar. Tek süreç (tek konteyner) varsayımıyla
/// çalışır: sürümü/iptali değiştiren her uç bu önbelleği de günceller.
/// </summary>
public sealed class OturumOnbellegi
{
    private readonly object _kilit = new();
    private (int Editor, int Izleyici)? _surumler;
    private ConcurrentDictionary<string, DateTime>? _iptaller;

    /// <summary>Önbelleği DB'den (yeniden) yükler.</summary>
    public void Yukle(KasaDbContext db)
    {
        var a = db.Ayarlar.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.EditorOturumSurumu, x.IzleyiciOturumSurumu }).FirstOrDefault();
        var simdi = DateTime.UtcNow;
        var iptaller = db.IptalEdilenTokenlar.AsNoTracking().Where(t => t.BitisUtc > simdi)
            .ToDictionary(t => t.Jti, t => t.BitisUtc);
        lock (_kilit)
        {
            _surumler = a is null ? null : (a.EditorOturumSurumu, a.IzleyiciOturumSurumu);
            _iptaller = new ConcurrentDictionary<string, DateTime>(iptaller);
        }
    }

    private void Hazirla(KasaDbContext db)
    {
        if (_surumler is null || _iptaller is null) Yukle(db);
    }

    /// <summary>Rolün geçerli oturum sürümü; ayar satırı yoksa null.</summary>
    public int? GecerliSurum(KasaDbContext db, string? rol)
    {
        Hazirla(db);
        var s = _surumler;
        if (s is null) return null;
        return rol == "editor" ? s.Value.Editor : s.Value.Izleyici;
    }

    /// <summary>Ayar satırındaki oturum sürümleri değişti (şifre değişimi, oturumları kapat).</summary>
    public void SurumleriAyarla(AyarEntity a)
    {
        lock (_kilit) _surumler = (a.EditorOturumSurumu, a.IzleyiciOturumSurumu);
    }

    public bool IptalMi(KasaDbContext db, string? jti)
    {
        if (string.IsNullOrEmpty(jti)) return false;
        Hazirla(db);
        return _iptaller!.TryGetValue(jti, out var bitis) && bitis > DateTime.UtcNow;
    }

    /// <summary>Token'ı DB'de ve önbellekte iptal eder, süresi dolmuş kayıtları temizler.</summary>
    public void IptalEt(KasaDbContext db, string jti, DateTime bitisUtc)
    {
        Hazirla(db);
        var simdi = DateTime.UtcNow;
        db.IptalEdilenTokenlar.Where(t => t.BitisUtc <= simdi).ExecuteDelete();
        if (bitisUtc > simdi && !db.IptalEdilenTokenlar.Any(t => t.Jti == jti))
        {
            db.IptalEdilenTokenlar.Add(new IptalEdilenTokenEntity { Jti = jti, BitisUtc = bitisUtc });
            db.SaveChanges();
        }
        _iptaller![jti] = bitisUtc;
        foreach (var (k, v) in _iptaller)
            if (v <= simdi) _iptaller.TryRemove(k, out _);
    }
}
