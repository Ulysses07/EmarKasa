using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>Rol adları (JWT'deki değerler).</summary>
public static class Roller
{
    public const string Editor = "editor";
    public const string Izleyici = "viewer";

    public static bool GecerliMi(string? rol) => rol is Editor or Izleyici;
}

/// <summary>
/// Kişisel kullanıcı hesabı (her ortağa ayrı giriş). <see cref="Yerlesik"/> satır, .env'deki editördür
/// (<c>Kasa:EditorKullanici</c>): açılışta kendiliğinden oluşturulur, kullanıcı adı .env'den gelir ve
/// <see cref="SifreHash"/> boş kaldıkça .env'deki şifre geçerlidir. Kullanıcı adı Türkçe büyük/küçük
/// harf duyarsız tekildir (kontrol uçta; index birebir eşleşmeyi DB seviyesinde de engeller).
/// </summary>
[Gecmis(GecmisTurleri.Kullanici, nameof(AdSoyad), nameof(KullaniciAdi), GizlideKimlikYaz = true)]
[Index(nameof(KullaniciAdi), IsUnique = true)]
public class KullaniciEntity
{
    public int Id { get; set; }
    public string AdSoyad { get; set; } = "";
    public string KullaniciAdi { get; set; } = "";
    /// <summary><see cref="Roller.Editor"/> ya da <see cref="Roller.Izleyici"/>.</summary>
    public string Rol { get; set; } = Roller.Izleyici;
    public bool Aktif { get; set; } = true;
    /// <summary>.env'deki editör hesabı (silinemez, rolü değişmez).</summary>
    public bool Yerlesik { get; set; }
    /// <summary>PBKDF2 hash (izleyici şifresiyle aynı biçim). Yerleşik editörde boşsa .env şifresi geçerlidir.</summary>
    [Gizli("Şifre değiştirildi")]
    public string? SifreHash { get; set; }
    /// <summary>Artınca bu kişinin tüm token'ları geçersiz olur (şifre değişimi, pasif yapma, oturumlarını kapatma).</summary>
    [Gizli("Oturumları kapatıldı")]
    public int OturumSurumu { get; set; }
    /// <summary>İki adımlı giriş sırrı (base32, RFC 6238). null = iki adımlı giriş kapalı.</summary>
    [Gizli("İki adımlı giriş ayarı değişti")]
    public string? TotpSir { get; set; }
    /// <summary>Kullanılmamış kurtarma kodlarının hash'leri (';' ile ayrılır).</summary>
    [Gizli("Kurtarma kodları değişti")]
    public string? KurtarmaKodlari { get; set; }
    public DateTime OlusturmaUtc { get; set; }
}

/// <summary>Giriş günlüğü nedenleri (sabit metinler; risk kartı ve kilit sayımı bunlara bakar).</summary>
public static class GirisNedenleri
{
    public const string HataliSifre = "Hatalı şifre";
    public const string HesapPasif = "Hesap pasif";
    /// <summary>Şifre doğru, iki adımlı kod henüz girilmedi (başarısız sayılmaz; ara adımdır).</summary>
    public const string KodBekleniyor = "Kod bekleniyor";
    public const string HataliKod = "Hatalı kod";
    public const string KodKilitli = "Çok fazla hatalı kod";
    public const string KurtarmaKodu = "Kurtarma kodu kullanıldı";
    public const string OrtakSifre = "Ortak izleyici şifresi";
}

/// <summary>
/// Giriş günlüğü: her giriş denemesi (başarılı ya da değil). Kullanıcı adı yazıldığı gibi (en fazla
/// <see cref="GuvenlikKurallari.GunlukAdEnCok"/> karakter), IP güvenilir proxy kuralıyla (X-Forwarded-For),
/// cihaz adı uygulamanın gönderdiği başlıktan gelir. Giriş yapmadan yazılabildiği için tablo sınırlıdır:
/// başarısız denemeler saatlik tek satırda sayılır (<see cref="Tekrar"/>), başarısızlar
/// <see cref="GuvenlikKurallari.BasarisizGirisGun"/> gün ve en çok
/// <see cref="GuvenlikKurallari.BasarisizGirisEnCokSatir"/> satır tutulur, başarılılar
/// <c>Kasa:GirisGunluguGun</c> (varsayılan 180) gün; saatlik bakım fazlasını siler.
/// </summary>
[GecmisDisi]
[Index(nameof(ZamanUtc))]
[Index(nameof(KullaniciId), nameof(ZamanUtc))]
public class GirisKaydiEntity
{
    public int Id { get; set; }
    public DateTime ZamanUtc { get; set; }
    public string KullaniciAdi { get; set; } = "";
    public int? KullaniciId { get; set; }
    public string? AdSoyad { get; set; }
    /// <summary>Hesabın rolü (başarılı girişte ve hesabı bilinen başarısız denemede).</summary>
    public string? Rol { get; set; }
    public bool Basarili { get; set; }
    public string? Neden { get; set; }
    public string? Ip { get; set; }
    public string? Cihaz { get; set; }
    /// <summary>İki adımlı girişte kullanılan zaman adımı (aynı kod ikinci kez kabul edilmesin).</summary>
    public long? TotpAdim { get; set; }
    /// <summary>
    /// Bu satırda sayılan deneme. Başarısız denemeler (hatalı kod hariç: onu kod kilidi sınırlar) aynı
    /// IP, aynı hesap (tanınmayan adlar tek hesap sayılır) ve aynı nedenle aynı saat içinde tek satırda
    /// toplanır; adlar farklıysa <see cref="KullaniciAdi"/> "<see cref="GuvenlikKurallari.CesitliAdlar"/>" olur.
    /// </summary>
    public int Tekrar { get; set; } = 1;
    /// <summary>Toplanan satırda son denemenin zamanı (tek denemede null).</summary>
    public DateTime? SonZamanUtc { get; set; }
}

/// <summary>
/// Açık oturum (verilen her token). Girişte yazılır; bu sürümden önce verilmiş token'lar ilk
/// görüldüklerinde eklenir (<see cref="Eski"/>). Son görülme en fazla birkaç dakikada bir güncellenir.
/// Oturum kapatma <see cref="IptalEdilenTokenEntity"/> ile yapılır; bu satır yalnız listeleme içindir.
/// </summary>
[GecmisDisi]
[Index(nameof(BitisUtc))]
public class OturumKaydiEntity
{
    [Key]
    public string Jti { get; set; } = "";
    public int? KullaniciId { get; set; }
    public string? AdSoyad { get; set; }
    public string Rol { get; set; } = "";
    public string? Cihaz { get; set; }
    public string? Ip { get; set; }
    public DateTime OlusturmaUtc { get; set; }
    public DateTime BitisUtc { get; set; }
    public DateTime SonGorulmeUtc { get; set; }
    /// <summary>Token'daki genel oturum sürümü (sv).</summary>
    public int Surum { get; set; }
    /// <summary>Token'daki kişi oturum sürümü (kişisel hesapta).</summary>
    public int? KullaniciSurumu { get; set; }
    public DateTime? KapatmaUtc { get; set; }
    /// <summary>
    /// Satır girişte değil, token ilk görüldüğünde eklendi (bu sürümden önce verilmiş token).
    /// <see cref="OlusturmaUtc"/> token'dan tahmin edilir: iat varsa o, yoksa bitiş − 30 gün (eski token'ların sabit ömrü).
    /// </summary>
    public bool Eski { get; set; }
}

/// <summary>Güvenlik ayarları (tek satır). Açılışta oluşturulur.</summary>
[Gecmis(GecmisTurleri.GuvenlikAyari)]
public class GuvenlikAyariEntity
{
    public int Id { get; set; }
    /// <summary>Editör oturumunun süresi (gün). Varsayılan 30 (eskisi gibi); 7 seçilebilir. İzleyicide hep 30.</summary>
    public int EditorOturumGun { get; set; } = GuvenlikKurallari.VarsayilanOturumGun;
}

/// <summary>Gece yedek doğrulamasının sonucu (en yeni günlük yedek açılıp kayıt sayıları karşılaştırılır).</summary>
[GecmisDisi]
public class YedekDogrulamaEntity
{
    public int Id { get; set; }
    public DateTime ZamanUtc { get; set; }
    public string Dosya { get; set; } = "";
    public DateTime DosyaZamaniUtc { get; set; }
    public bool Basarili { get; set; }
    public string Mesaj { get; set; } = "";
}

/// <summary>Güvenlik kuralları (sabitler).</summary>
public static class GuvenlikKurallari
{
    public const int VarsayilanOturumGun = 30;
    public const int IzleyiciOturumGun = 30;
    public const int EnKisaOturumGun = 1;
    public const int EnUzunOturumGun = 30;
    public const int SifreEnAz = 8;
    public const int SifreEnCok = 200;
    public const int VarsayilanGirisGunluguGun = 180;
    /// <summary>Bu kadar hatalı kod (son <see cref="KodKilitSuresi"/> içinde) kişinin kod girişini kilitler.</summary>
    public const int KodKilitDenemesi = 5;
    public static readonly TimeSpan KodKilitSuresi = TimeSpan.FromMinutes(15);
    /// <summary>Oturumun son görülme zamanı en fazla bu aralıkla yazılır.</summary>
    public static readonly TimeSpan SonGorulmeAraligi = TimeSpan.FromMinutes(5);
    /// <summary>Giriş günlüğüne yazılan kullanıcı adının en fazla uzunluğu (geçerli bir adın sınırı).</summary>
    public const int GunlukAdEnCok = 50;
    /// <summary>Başarısız denemeler bu kadar gün saklanır (giriş günlüğü süresi daha kısaysa o).</summary>
    public const int BasarisizGirisGun = 30;
    /// <summary>En çok bu kadar başarısız deneme satırı tutulur (en yeniler); saatlik bakım fazlasını siler.</summary>
    public const int BasarisizGirisEnCokSatir = 20_000;
    /// <summary>Toplanan satırda farklı adlar denendiyse kullanıcı adı yerine yazılır.</summary>
    public const string CesitliAdlar = "(çeşitli adlar)";
}
