using Kasa.Api.Data;

namespace Kasa.Api.Auth;

/// <summary>
/// Açılışta kişisel hesapların başlangıç durumunu hazırlar (HTTP dışında: geçmişe yazılmaz):
/// <list type="bullet">
/// <item>Güvenlik ayarı satırı yoksa varsayılanla (editör oturumu 30 gün) eklenir.</item>
/// <item>.env'deki editör (<c>Kasa:EditorKullanici</c> + <c>Kasa:EditorSifre</c>) için yerleşik hesap
///       yoksa oluşturulur (şifresi boş: .env şifresi geçerli). .env'deki kullanıcı adı değiştiyse
///       hesabın kullanıcı adı da güncellenir.</item>
/// <item><c>Kasa:EditorSifresiniSifirla=true</c> ise yerleşik editörün uygulamadan değiştirilmiş şifresi,
///       iki adımlı girişi ve kurtarma kodları silinir (.env şifresi yeniden geçerli olur) — şifre
///       unutulduğunda sunucudan kurtarma yolu. Ayar kapatılmazsa her açılışta yeniden sıfırlanır.</item>
/// </list>
/// </summary>
public static class KimlikTohumu
{
    public static void Hazirla(KasaDbContext db, IConfiguration cfg, ILogger log, TimeProvider saat)
    {
        if (!db.GuvenlikAyarlari.Any())
            db.GuvenlikAyarlari.Add(new GuvenlikAyariEntity());

        var envKullanici = cfg["Kasa:EditorKullanici"]?.Trim();
        if (!string.IsNullOrEmpty(envKullanici) && !string.IsNullOrEmpty(cfg["Kasa:EditorSifre"]))
        {
            var yerlesik = db.Kullanicilar.FirstOrDefault(k => k.Yerlesik);
            var digerleri = db.Kullanicilar.Where(k => !k.Yerlesik).Select(k => k.KullaniciAdi).ToList();
            var cakisiyor = digerleri.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, envKullanici));
            if (yerlesik is null)
            {
                db.Kullanicilar.Add(new KullaniciEntity
                {
                    AdSoyad = envKullanici.ToUpper(Metin.Tr),
                    KullaniciAdi = cakisiyor ? envKullanici + "-editor" : envKullanici,
                    Rol = Roller.Editor,
                    Aktif = true,
                    Yerlesik = true,
                    OlusturmaUtc = saat.GetUtcNow().UtcDateTime,
                });
            }
            else if (yerlesik.KullaniciAdi != envKullanici)
            {
                if (cakisiyor)
                    log.LogWarning("Kasa:EditorKullanici ({Ad}) başka bir kullanıcının adıyla çakışıyor; yerleşik hesabın adı değiştirilmedi.", envKullanici);
                else
                    yerlesik.KullaniciAdi = envKullanici;
            }

            if (yerlesik is not null && cfg.GetValue("Kasa:EditorSifresiniSifirla", false)
                && (yerlesik.SifreHash is not null || yerlesik.TotpSir is not null || yerlesik.KurtarmaKodlari is not null))
            {
                yerlesik.SifreHash = null;
                yerlesik.TotpSir = null;
                yerlesik.KurtarmaKodlari = null;
                yerlesik.OturumSurumu++;
                log.LogWarning("Kasa:EditorSifresiniSifirla açık: yerleşik editörün şifresi ve iki adımlı girişi sıfırlandı. Ayarı kapatın.");
            }
        }
        db.SaveChanges();
    }
}
