using Kasa.Api.Denetim;

namespace Kasa.Api;

public record KartMasrafYaz(Guid IstekId, int Surum, int EkstreId, DateOnly Tarih, decimal Tutar, string Aciklama, string? DagilimOzeti = null);
public record KartMasrafOnizlemeDto(int KartId, int EkstreId, DateOnly Tarih, decimal Tutar, decimal DevredenBorc, IReadOnlyList<TakipKanalPayi> Dagilimlar, string DagilimOzeti);
public record KasaEsikYaz(int Surum, decimal Tutar, bool Etkin);
public record KasaEsikDto(int KanalId, string Kanal, int Surum, decimal Tutar, bool Etkin, decimal Bakiye, bool EsikAltinda);
public record KasaKontrolOnizle(decimal GercekBakiye, string? Not = null);
/// <summary>Fark sıfırdan farklıysa <paramref name="Not"/> zorunludur (400, alan 'not').</summary>
public record KasaKontrolYaz(Guid IstekId, decimal GercekBakiye, string KontrolOzeti, string? Not = null);
/// <summary>Kanal kasası: kontrol anındaki bakiye; listede <paramref name="GuncelBakiye"/> aynı gün için bugünkü veriyle yeniden
/// hesaplanan bakiye (kanal silinmişse null).</summary>
public record KasaKontrolKanalDto(int? KanalId, string Kanal, decimal Bakiye, decimal? GuncelBakiye = null);
/// <summary><paramref name="KontrolOzeti"/> genel kasayı, gerçek bakiyeyi ve kanal bakiyelerini bağlar (açıklamayı değil):
/// kayıtta bunlardan biri değiştiyse 409. <paramref name="KanalBakiyeleri"/> ve <paramref name="HesapTarihi"/> eski sunucuda yok.</summary>
public record KasaKontrolOnizlemeDto(decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string KontrolOzeti,
    IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, DateOnly? HesapTarihi = null);
/// <summary>
/// Kasa kontrolü. Yeni alanlar (gap-denetim-izi-gozlemlenebilirlik-3, gap-coklu-giris-cift-sayim-mutabakat-17) sonda ve
/// varsayılanlı. <paramref name="HesapTarihi"/> ve <paramref name="KanalBakiyeleri"/> filigrandır: filigrandan önceki kayıtta null.
/// <paramref name="GuncelSistemBakiye"/>/<paramref name="GuncelFark"/>/<paramref name="SonradanDegisti"/> yalnız listede dolar:
/// kaydın günü (filigransız kayıtta kayıt anının İstanbul günü) için genel kasa bugünkü veriyle yeniden hesaplanır; kayıttaki
/// sistem bakiyesinden farklıysa o güne ya da öncesine düşen bir kayıt sonradan girildi, silindi ya da düzeltildi.
/// </summary>
public record KasaKontrolDto(int Id, DateTimeOffset Kaydedildi, decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string? Not,
    int Surum = 1, DateOnly? HesapTarihi = null, IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, string? FarkAciklamasi = null,
    DateTimeOffset? FarkAciklamaZamani = null, decimal? GuncelSistemBakiye = null, decimal? GuncelFark = null, bool SonradanDegisti = false);
/// <summary>Farkın sonradan açıklanması: tutarlar değişmez; <paramref name="Surum"/> listedeki sürüm (uyuşmazsa 409).</summary>
public record KasaKontrolAciklamaYaz(Guid IstekId, int Surum, string Aciklama);
/// <summary>Kontrolden sonra kaydedilen mali istek (istek türü ve sonucunun kimliği).</summary>
public record KasaKontrolIstekDto(Guid IstekId, string Tur, int SonucId);
/// <summary>
/// "Bu kontrolden beri değişenler". <paramref name="EsasTarih"/>: kontrolün hesap günü (filigransız kayıtta kayıt anının İstanbul
/// günü). <paramref name="GuncelSistemBakiye"/> − <paramref name="SistemBakiye"/>: esas güne kadarki geriye dönük değişim;
/// <paramref name="BugunkuSistemBakiye"/> − <paramref name="GuncelSistemBakiye"/>: esas günden bugüne kasaya işleyen hareketler.
/// <paramref name="Degisiklikler"/>: kontrolden sonraki denetim olayları (oturum, kasa kontrolü, eşik ve alıcı hesabı olayları hariç);
/// filigransız kayıtta kayıt anından sonrakiler. <paramref name="Istekler"/>: kontrolden sonraki mali istekler (filigransız kayıtta boş).
/// <paramref name="Hareketler"/>: etki tarihi esas günden sonra olan döküm satırları ve kontrolden sonra girilip esas güne ya da
/// öncesine düşen giderler. Her liste en çok 500 öğedir (en eskiler); aşılırsa <paramref name="Kirpildi"/>.
/// </summary>
public record KasaKontrolSonrasiDto(int KontrolId, DateTimeOffset Kaydedildi, DateOnly EsasTarih, bool FiligranVar, decimal SistemBakiye,
    decimal GuncelSistemBakiye, decimal BugunkuSistemBakiye, IReadOnlyList<DenetimOlayDto> Degisiklikler, IReadOnlyList<KasaKontrolIstekDto> Istekler,
    IReadOnlyList<KasaHareketiDto> Hareketler, bool Kirpildi);
/// <summary>
/// Kasa hareket dökümünün satırı (bkz. <see cref="Servisler.KasaDokumu"/>). <paramref name="Tur"/>: Gelir, EkstreGeliri, EkGelir,
/// KrediCekimi, Gider, SabitGider, AylikGider, KartOdemesi, KartIadesi, KrediTaksidi, KartAySonu. <paramref name="KaynakAnahtari"/>:
/// denetim izindeki varlık ve kimlik ("Islem:812", "TakipKartOdeme:44", "TakipKrediTaksit:9", "Kredi:5", "EkstreKayit:9",
/// "HesapHareket:3", "Gelen:17", "TakipHarcama:12"). <paramref name="Otomatik"/>: yazma olmadan tarihinde işleyen etki (kredi
/// taksidi, eski kartın ay sonu düşümü). <paramref name="KanalId"/> gerçek kanal olmayan etikette (Ortak, Dağılım bekliyor, Genel
/// kasa) null.
/// </summary>
public record KasaHareketiDto(DateOnly EtkiTarihi, DateOnly KayitTarihi, string Tur, string Aciklama, string Kanal, int? KanalId,
    decimal GenelKasaEtkisi, decimal KanalEtkisi, string? KaynakAnahtari, bool Otomatik);
/// <summary>GET /api/kasa-hareketleri yanıtı. <paramref name="KanalId"/> yoksa bakiyeler genel kasa, varsa o kanalın kasasıdır;
/// kapanış − açılış = satırların (kanalda kanal etkisinin) toplamı. <paramref name="Bitis"/> en geç bugündür.</summary>
public record KasaHareketleriDto(DateOnly Baslangic, DateOnly Bitis, int? KanalId, string? Kanal, decimal AcilisBakiyesi, decimal KapanisBakiyesi,
    IReadOnlyList<KasaHareketiDto> Hareketler);
