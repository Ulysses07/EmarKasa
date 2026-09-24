using Kasa.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>Kullanıcılar, giriş günlüğü, açık oturumlar, sorular ve yedek doğrulaması (paket E).</summary>
public partial class KasaDbContext
{
    public DbSet<KullaniciEntity> Kullanicilar => Set<KullaniciEntity>();
    public DbSet<GirisKaydiEntity> GirisKayitlari => Set<GirisKaydiEntity>();
    public DbSet<OturumKaydiEntity> OturumKayitlari => Set<OturumKaydiEntity>();
    public DbSet<GuvenlikAyariEntity> GuvenlikAyarlari => Set<GuvenlikAyariEntity>();
    public DbSet<YedekDogrulamaEntity> YedekDogrulamalari => Set<YedekDogrulamaEntity>();
    public DbSet<SoruEntity> Sorular => Set<SoruEntity>();

    private (bool Atandi, string? Deger) _degistirenAd;
    private (bool Atandi, string? Deger) _degistirenCihaz;

    /// <summary>
    /// Geçmişe yazılacak kişi adı: HTTP isteğinde token'daki kullanıcının GÜNCEL adı (yeniden
    /// adlandırma eski token'larda da görünsün diye önbellekten). Ortak izleyici şifresiyle ve
    /// eski token'larda null. Girişte (henüz token yokken) açıkça atanır.
    /// </summary>
    public string? DegistirenAd
    {
        get => _degistirenAd.Atandi ? _degistirenAd.Deger : KimlikBilgisi.Ad(_http?.HttpContext);
        set => _degistirenAd = (true, value);
    }

    /// <summary>Geçmişe yazılacak cihaz adı (token'daki); girişte açıkça atanır.</summary>
    public string? DegistirenCihaz
    {
        get => _degistirenCihaz.Atandi ? _degistirenCihaz.Deger : KimlikBilgisi.Cihaz(_http?.HttpContext);
        set => _degistirenCihaz = (true, value);
    }

    private void KisiVeCihazYaz(List<DegisiklikEntity> satirlar)
    {
        if (satirlar.Count == 0) return;
        var ad = DegistirenAd;
        var cihaz = DegistirenCihaz;
        foreach (var s in satirlar)
        {
            s.Kullanici = ad;
            s.Cihaz = cihaz;
        }
    }
}
