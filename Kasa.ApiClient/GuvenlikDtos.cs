namespace Kasa.ApiClient;

// Kullanıcılar, oturumlar, iki adımlı giriş, sorular ve risk kartı (paket E).

/// <summary>
/// Giriş sonucu. <see cref="KodGerekli"/> true ise şifre doğru ama iki adımlı giriş kodu isteniyor
/// (token yok); <see cref="Mesaj"/> sunucunun açıklamasıdır (ör. "Kod hatalı").
/// </summary>
public record GirisSonucu(string? Rol, string? Ad, int? KullaniciId, bool KodGerekli = false, string? Mesaj = null)
{
    public static GirisSonucu KodIsteniyor(string? mesaj) => new(null, null, null, true, mesaj);
}

/// <summary>Oturumdaki kişi (/api/auth/me). Ortak izleyici şifresinde <see cref="Ad"/> ve <see cref="KullaniciId"/> boş.</summary>
public record BenDto(string? Rol, string? Ad, int? KullaniciId);

/// <summary>Kendi hesabım (editör). <see cref="EnvSifresi"/>: şifre hâlâ sunucu ayarındaki (.env).</summary>
public record HesapDto(int Id, string AdSoyad, string KullaniciAdi, string Rol, bool Yerlesik, bool EnvSifresi,
    bool IkiAdimAcik, int KurtarmaKoduKalan, int EditorOturumGun);

/// <summary>İki adımlı giriş kurulumu: uygulamaya elle yazılacak sır ve otpauth:// adresi.</summary>
public record IkiAdimKurulumDto(string Sir, string Adres);

/// <param name="Ben">Listeyi açan editörün kendi hesabı: kendi oturumlarını, rolünü ve aktifliğini listeden değiştiremez.</param>
public record KullaniciDto(int Id, string AdSoyad, string KullaniciAdi, string Rol, bool Aktif, bool Yerlesik,
    bool IkiAdimAcik, DateTime OlusturmaUtc, DateTime? SonGirisUtc, string? SonCihaz, int AcikOturum, bool Ben = false);

public record KullaniciEkle(string AdSoyad, string KullaniciAdi, string Rol, string Sifre);
public record KullaniciGuncelle(string AdSoyad, string Rol, bool Aktif);

/// <summary>
/// Açık oturum; <see cref="Id"/> oturum kimliğidir (token değil). <see cref="Eski"/>: güncellemeden önce
/// açılmış oturum (açılış anı token'dan tahmin edilir).
/// </summary>
public record OturumDto(string Id, int? KullaniciId, string? AdSoyad, string Rol, string? Cihaz, string? Ip,
    DateTime OlusturmaUtc, DateTime SonGorulmeUtc, DateTime BitisUtc, bool Eski, bool BuOturum);

/// <summary>
/// Giriş günlüğü satırı. Başarısız denemeler aynı IP, hesap ve nedenle saatlik tek satırda sayılır:
/// <see cref="Tekrar"/> deneme sayısı, <see cref="SonZamanUtc"/> son denemenin zamanı (tek denemede null).
/// </summary>
public record GirisKaydiDto(int Id, DateTime ZamanUtc, string KullaniciAdi, string? AdSoyad, string? Rol,
    bool Basarili, string? Neden, string? Ip, string? Cihaz, int Tekrar = 1, DateTime? SonZamanUtc = null);

/// <summary>Giriş günlüğünün bir sayfası (en yeni önce) + toplam satır.</summary>
public record GirisKaydiSayfasi(IReadOnlyList<GirisKaydiDto> Kayitlar, int Toplam);

public record GuvenlikAyariDto(int EditorOturumGun, int IzleyiciOturumGun, int GirisGunluguGun);

/// <summary>Sorunun neyle ilgili olduğu.</summary>
public enum SoruHedefTuru { Genel, Islem, Hafta, Cek }
public enum SoruDurumu { Acik, Kapali }

/// <summary>Kayda soru. <see cref="HedefOzet"/> soru anındaki kaydın kısa özeti.</summary>
public record SoruDto(int Id, SoruHedefTuru HedefTur, int? HedefId, DateOnly? Hafta, string? HedefOzet, string Metin,
    int? SoranId, string SoranAd, string SoranRol, DateTime SorulmaUtc, string? Cevap, string? CevaplayanAd,
    DateTime? CevaplanmaUtc, SoruDurumu Durum, DateTime? KapanmaUtc);

public record SoruYaz(SoruHedefTuru HedefTur, int? HedefId, DateOnly? Hafta, string Metin);

public record SoruOzetDto(int AcikSayisi, int CevapBekleyen, IReadOnlyList<SoruDto> SonAciklar);

public enum RiskSeviyesi { Sari, Kirmizi }
public record RiskMaddesiDto(RiskSeviyesi Seviye, string Konu, string Baslik, string Aciklama);
public record YedekDogrulamaDto(DateTime ZamanUtc, string Dosya, DateTime DosyaZamaniUtc, bool Basarili, string Mesaj);

/// <param name="Durum">"ok", "sari" ya da "kirmizi".</param>
public record SistemRiskDto(string Durum, IReadOnlyList<RiskMaddesiDto> Maddeler, DateTime HesaplanmaUtc,
    YedekDogrulamaDto? SonDogrulama, long? DiskBosMb, int DunBasarisizGiris);
