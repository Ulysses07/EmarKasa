namespace Kasa.App.Core;

/// <summary>
/// Bildirimden (toast) gelen sayfa isteğini, oturum açılana kadar bekletir. Giriş yapılmadan
/// korumalı bir sayfaya gidilmesini engeller: Shell yalnız oturum açıkken <see cref="Al"/> ile hedefi alır.
/// </summary>
public sealed class Yonlendirme
{
    /// <summary>Bildirim argümanı → Shell rotası. Bilinmeyen hedefler yok sayılır.</summary>
    public static string? RotaCoz(string? hedef) => hedef switch
    {
        "kredikartlari" or "kartlar" => "//kartlar",
        "panel" => "//panel",
        "islemler" => "//islemler",
        // Paket A bildirimleri (haftalık özet, vadesi geçen çek, geçmişe dönük düzeltme).
        "cekler" => "//cekler",
        "kasasayimi" => "//kasasayimi",
        "gecmis" => "//gecmis",
        "haftalik" => "//haftalik",
        "sorular" => "//sorular",
        _ => null,
    };

    private readonly object _kilit = new();
    private string? _bekleyen;

    /// <summary>Yeni bir istek geldiğinde (herhangi bir iş parçacığından) tetiklenir.</summary>
    public event EventHandler? Istendi;

    /// <summary>Bildirim hedefini kaydeder. Geçersiz hedef yok sayılır.</summary>
    public void Iste(string? hedef)
    {
        var rota = RotaCoz(hedef);
        if (rota is null) return;
        lock (_kilit) _bekleyen = rota;
        Istendi?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bekleyen rotayı alır ve temizler (yoksa null).</summary>
    public string? Al()
    {
        lock (_kilit)
        {
            var r = _bekleyen;
            _bekleyen = null;
            return r;
        }
    }

    public bool BekleyenVar { get { lock (_kilit) return _bekleyen is not null; } }
}
