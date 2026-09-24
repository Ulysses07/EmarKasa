namespace Kasa.Api;

// Kullanıcılar, oturumlar, giriş günlüğü ve iki adımlı giriş (paket E).

/// <summary>Başarılı giriş yanıtı (çerezle birlikte gövdede token: Windows uygulaması Bearer kullanır).</summary>
public record GirisYanitiDto(string Rol, string Token, string? Ad, int? KullaniciId);

/// <summary>Oturumdaki kişi.</summary>
public record BenDto(string? Rol, string? Ad, int? KullaniciId);

/// <summary>Giriş yapmış editörün kendi hesabı.</summary>
/// <param name="EnvSifresi">Yerleşik editörde şifre hâlâ sunucu ayarındaki (.env) şifre.</param>
public record HesapDto(int Id, string AdSoyad, string KullaniciAdi, string Rol, bool Yerlesik, bool EnvSifresi,
    bool IkiAdimAcik, int KurtarmaKoduKalan, int EditorOturumGun);

public record SifreDegistirDto(string? MevcutSifre, string? YeniSifre);

/// <summary>Hesabın oturumları yenilendi: bu cihaz için yeni token (diğer cihazlar çıkar).</summary>
public record YeniTokenDto(string Token);

public record IkiAdimBaslatDto(string Sir, string Adres);

/// <param name="Sifre">Hesabın şifresi: iki adımı açmak ve kurtarma kodlarını yenilemek yalnız token'la yapılamaz.</param>
public record KodDto(string? Kod, string? Sifre = null);

public record IkiAdimKapatDto(string? Sifre, string? Kod);

/// <summary>Yeni kurtarma kodları (yalnız bu yanıtta görünür); <see cref="Token"/> oturum yenilendiyse dolu.</summary>
public record KurtarmaKodlariDto(IReadOnlyList<string> Kodlar, string? Token = null);

/// <param name="Ben">İsteği yapan editörün kendi hesabı (kendi oturumlarını, rolünü listeden değiştiremez).</param>
public record KullaniciDto(int Id, string AdSoyad, string KullaniciAdi, string Rol, bool Aktif, bool Yerlesik,
    bool IkiAdimAcik, DateTime OlusturmaUtc, DateTime? SonGirisUtc, string? SonCihaz, int AcikOturum, bool Ben);

public record KullaniciEkleDto(string? AdSoyad, string? KullaniciAdi, string? Rol, string? Sifre);

public record KullaniciGuncelleDto(string? AdSoyad, string? Rol, bool Aktif);

public record YeniSifreDto(string? YeniSifre);

/// <param name="Id">Oturum kimliği (token'ın jti'si; token'ın kendisi değil).</param>
/// <param name="Eski">Bu sürümden önce verilmiş token: satır ilk görüldüğünde eklendi, açılış anı token'dan tahmin.</param>
public record OturumDto(string Id, int? KullaniciId, string? AdSoyad, string Rol, string? Cihaz, string? Ip,
    DateTime OlusturmaUtc, DateTime SonGorulmeUtc, DateTime BitisUtc, bool Eski, bool BuOturum);

/// <param name="Tekrar">Satırda sayılan deneme (başarısızlar saatlik toplanır).</param>
/// <param name="SonZamanUtc">Toplanan satırda son denemenin zamanı.</param>
public record GirisKaydiDto(int Id, DateTime ZamanUtc, string KullaniciAdi, string? AdSoyad, string? Rol,
    bool Basarili, string? Neden, string? Ip, string? Cihaz, int Tekrar, DateTime? SonZamanUtc);

public record GuvenlikAyariDto(int EditorOturumGun, int IzleyiciOturumGun, int GirisGunluguGun);

public record GuvenlikAyariGuncelleDto(int EditorOturumGun);
