namespace Kasa.ApiClient;

public record KartMasrafYaz(Guid IstekId, int Surum, int EkstreId, DateOnly Tarih, decimal Tutar, string Aciklama, string? DagilimOzeti = null);
public record KartMasrafOnizlemeDto(int KartId, int EkstreId, DateOnly Tarih, decimal Tutar, decimal DevredenBorc, IReadOnlyList<TakipKanalPayi> Dagilimlar, string DagilimOzeti);
public record KasaEsikYaz(int Surum, decimal Tutar, bool Etkin);
public record KasaEsikDto(int KanalId, string Kanal, int Surum, decimal Tutar, bool Etkin, decimal Bakiye, bool EsikAltinda);
public record KasaKontrolOnizle(decimal GercekBakiye, string? Not = null);
/// <summary>Fark sıfırdan farklıysa <paramref name="Not"/> zorunludur (sunucu 400, alan 'not').</summary>
public record KasaKontrolYaz(Guid IstekId, decimal GercekBakiye, string KontrolOzeti, string? Not = null);
/// <summary>Kanal kasası: kontrol anındaki bakiye; listede <paramref name="GuncelBakiye"/> aynı gün için bugünkü veriyle hesaplanan
/// bakiye (kanal silinmişse null).</summary>
public record KasaKontrolKanalDto(int? KanalId, string Kanal, decimal Bakiye, decimal? GuncelBakiye = null);
/// <summary><paramref name="KanalBakiyeleri"/>/<paramref name="HesapTarihi"/> eski sunucuda boş.</summary>
public record KasaKontrolOnizlemeDto(decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string KontrolOzeti,
    IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, DateOnly? HesapTarihi = null);
/// <summary>Kasa kontrolü. <paramref name="HesapTarihi"/> ve <paramref name="KanalBakiyeleri"/> filigrandır (filigrandan önceki kayıtta
/// ve eski sunucuda boş). <paramref name="GuncelSistemBakiye"/>/<paramref name="GuncelFark"/>/<paramref name="SonradanDegisti"/>
/// yalnız listede dolar: kaydın günü için genel kasa bugünkü veriyle yeniden hesaplanır.</summary>
public record KasaKontrolDto(int Id, DateTimeOffset Kaydedildi, decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string? Not,
    int Surum = 1, DateOnly? HesapTarihi = null, IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, string? FarkAciklamasi = null,
    DateTimeOffset? FarkAciklamaZamani = null, decimal? GuncelSistemBakiye = null, decimal? GuncelFark = null, bool SonradanDegisti = false);
/// <summary>Farkın sonradan açıklanması; <paramref name="Surum"/> listedeki sürüm (uyuşmazsa 409).</summary>
public record KasaKontrolAciklamaYaz(Guid IstekId, int Surum, string Aciklama);
public record KasaKontrolIstekDto(Guid IstekId, string Tur, int SonucId);
/// <summary>"Bu kontrolden beri değişenler": geriye dönük değişim = <paramref name="GuncelSistemBakiye"/> − <paramref name="SistemBakiye"/>,
/// esas günden bugüne hareket = <paramref name="BugunkuSistemBakiye"/> − <paramref name="GuncelSistemBakiye"/>.</summary>
public record KasaKontrolSonrasiDto(int KontrolId, DateTimeOffset Kaydedildi, DateOnly EsasTarih, bool FiligranVar, decimal SistemBakiye,
    decimal GuncelSistemBakiye, decimal BugunkuSistemBakiye, IReadOnlyList<DenetimOlayDto> Degisiklikler, IReadOnlyList<KasaKontrolIstekDto> Istekler,
    IReadOnlyList<KasaHareketiDto> Hareketler, bool Kirpildi);
/// <summary>Kasa hareket dökümünün satırı. <paramref name="Tur"/>: Gelir, EkstreGeliri, EkGelir, KrediCekimi, Gider, SabitGider,
/// AylikGider, KartOdemesi, KartIadesi, KrediTaksidi, KartAySonu, Cek. <paramref name="KaynakAnahtari"/>: çek satırında hareketin
/// kimliği ("Cek:31", gider ayağı "Cek:31:gider") — anahtarın türü "Cek" olsa da kimlik çekin değil hareketin kimliğidir; denetim
/// izindeki varlık buna göre "CekHareket"tir, "Cek" değil. <paramref name="Otomatik"/>: tarihinde kendiliğinden işleyen etki.</summary>
public record KasaHareketiDto(DateOnly EtkiTarihi, DateOnly KayitTarihi, string Tur, string Aciklama, string Kanal, int? KanalId,
    decimal GenelKasaEtkisi, decimal KanalEtkisi, string? KaynakAnahtari, bool Otomatik);
public record KasaHareketleriDto(DateOnly Baslangic, DateOnly Bitis, int? KanalId, string? Kanal, decimal AcilisBakiyesi, decimal KapanisBakiyesi,
    IReadOnlyList<KasaHareketiDto> Hareketler);

public interface IKasaKontrolApi
{
    Task<KartMasrafOnizlemeDto> KartMasrafOnizleAsync(int kartId, KartMasrafYaz girdi);
    Task<KartTakipDto> KartMasrafKaydetAsync(int kartId, KartMasrafYaz girdi);
    Task<IReadOnlyList<KasaEsikDto>> KasaEsikleriAsync();
    Task<KasaEsikDto> KasaEsigiKaydetAsync(int kanalId, KasaEsikYaz girdi);
    Task<IReadOnlyList<KasaKontrolDto>> KasaKontrolleriAsync();
    Task<KasaKontrolOnizlemeDto> KasaKontrolOnizleAsync(KasaKontrolOnizle girdi);
    Task<KasaKontrolDto> KasaKontrolKaydetAsync(KasaKontrolYaz girdi);
    /// <summary>Farkı sonradan açıklar (PUT api/kasa-kontrol/{id}/aciklama); tutarlar değişmez.</summary>
    Task<KasaKontrolDto> KasaKontrolAciklaAsync(int id, KasaKontrolAciklamaYaz girdi);
    /// <summary>"Bu kontrolden beri değişenler" (GET api/kasa-kontrol/{id}/sonrasi; yalnız editör).</summary>
    Task<KasaKontrolSonrasiDto> KasaKontrolSonrasiAsync(int id);
    /// <summary>Kasa hareket dökümü (GET api/kasa-hareketleri); boş tarih sunucu varsayılanıdır (bitiş bugün, başlangıç ayın başı),
    /// <paramref name="kanalId"/> verilirse o kanalın kasası.</summary>
    Task<KasaHareketleriDto> KasaHareketleriAsync(DateOnly? baslangic = null, DateOnly? bitis = null, int? kanalId = null);
}
