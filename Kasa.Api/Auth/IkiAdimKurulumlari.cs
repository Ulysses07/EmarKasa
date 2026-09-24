using System.Collections.Concurrent;

namespace Kasa.Api.Auth;

/// <summary>
/// Kurulmakta olan iki adımlı giriş sırları: "Başlat" yeni sırrı burada (bellekte) tutar, uygulamadan
/// ilk kod doğrulanınca hesaba yazılır. Onaylanmayan sır <see cref="Sure"/> sonra düşer; yeniden
/// başlatmak eskisini ezer. Uygulama yeniden başlarsa kurulum baştan yapılır.
/// </summary>
public sealed class IkiAdimKurulumlari
{
    public static readonly TimeSpan Sure = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<int, (string Sir, DateTime BitisUtc)> _bekleyen = new();

    public string Baslat(int kullaniciId, DateTime simdiUtc)
    {
        foreach (var (k, v) in _bekleyen)
            if (v.BitisUtc <= simdiUtc) _bekleyen.TryRemove(k, out _);
        var sir = Totp.SirUret();
        _bekleyen[kullaniciId] = (sir, simdiUtc + Sure);
        return sir;
    }

    public string? Bekleyen(int kullaniciId, DateTime simdiUtc)
    {
        if (!_bekleyen.TryGetValue(kullaniciId, out var b)) return null;
        if (b.BitisUtc > simdiUtc) return b.Sir;
        _bekleyen.TryRemove(kullaniciId, out _);
        return null;
    }

    public void Bitir(int kullaniciId) => _bekleyen.TryRemove(kullaniciId, out _);
}
