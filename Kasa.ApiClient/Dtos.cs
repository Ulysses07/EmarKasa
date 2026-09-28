namespace Kasa.ApiClient;

public enum GiderTipi { Cari, SabitGider, KrediKarti }

/// <summary>Cihaz: tanıdık cihaz belirteci (masaüstüne gövdede verilir; tarayıcıya HttpOnly çerezle gider, orada null).</summary>
public record LoginYanit(string Rol, string Token, string? Cihaz = null);

public record KanalDto(int Id, string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record IslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null, int? AlisId = null, bool DagilimBekliyor = false, int? AylikGiderOdemeId = null, int? EkstreKayitId = null);
public record GelenDto(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl, int? KanalId = null, bool EskiYinelenenGrup = false);
/// <summary>YeniTakip/Aktif: kart yeni takipte ve yeni kullanıma açık; yeni kredi kartı gideri yalnız böyle bir karta bağlanabilir
/// (K3). Eski sunucu göndermez: varsayılanlar eski kart gibidir (seçilemez).</summary>
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc, decimal GuncelBorc = 0m, decimal AcilisBorc = 0m, decimal HarcamaToplam = 0m, decimal OdemeToplam = 0m, decimal EkstreBorc = 0m,
    bool YeniTakip = false, bool Aktif = true);
public record KrediDto(int Id, string Ad, decimal CekilenTutar, DateOnly CekimTarihi, int TaksitSayisi, decimal AylikOdeme, int OdemeGunu, string Kanal, bool GerceklesmeTakibi = false);
public record KartOdemeDto(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
/// <summary>IzleyiciSifreKisa: kayıtlı izleyici şifresinin 12 karakter kuralına uymadığı bir girişte görüldü.
/// VekilUyarisi: sunucu güvenilmeyen kaynaktan vekil başlığı aldı (yanlış vekil ayarı). Eski sunucu ikisini de göndermez.</summary>
public record AyarlarDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, bool IzleyiciSifreVarMi,
    bool IzleyiciSifreKisa = false, string? VekilUyarisi = null);

public record DonemDto(DateOnly Start, DateOnly End, int Yil, int Ay);
public record KanalHaftalikDto(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
public record HaftalikOzetDto(
    DonemDto Donem,
    IReadOnlyList<KanalHaftalikDto> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir,
    decimal DagilimBekleyenTutar = 0m,
    // Sunucu yalnız son dönemde ve yalnız sorun varsa yazar (ör. rapor ufkunun ötesinde tarihli kayıt); eski sunucu göndermez.
    string? VeriSagligiUyarisi = null);
/// <summary>KrediGirisi: kanalın ayki kredi girişi (takipli kredi çekim payı). Kural 2'de Gelen ve AySonucu dışındadır; kural 1 ile
/// dondurulmuş ayda Gelen'in içindedir.</summary>
public record KanalAylikDto(string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal AySonucu, decimal KrediGirisi = 0m);
/// <summary>Aylık rapor. KrediGirisi: ayın bütün kredi girişi (takipli + eski kredi), yalnız kural 2'de dolu; ay sonucuna dahil değildir.
/// KuralSurumu: raporu üreten kural (eski sunucu ve kural 1'de null ya da 1). Dondurulmus: kilitli ayın kapatıldığı andaki raporu.
/// VeriSagligiUyarisi: tutarları değiştirmeyen uyarı (ör. takip başlangıcından önce tarihli giderler). Eski sunucu yeni alanları göndermez.</summary>
public record AylikRaporDto(int Yil, int Ay, IReadOnlyList<KanalAylikDto> Kanallar, decimal DagilimBekleyenTutar = 0m, decimal GenelGider = 0m, decimal GenelGelir = 0m,
    decimal? KrediGirisi = null, int? KuralSurumu = null, string? VeriSagligiUyarisi = null, bool Dondurulmus = false);
public record KanalBakiyeDto(string Kanal, decimal Bakiye, int? KanalId = null);
public record PanelDto(decimal GuncelKasa, IReadOnlyList<KanalBakiyeDto> Kanallar, decimal BuHaftaSonucu, decimal BuAySonucu, decimal DagilimBekleyenTutar = 0m);
/// <summary>Ana sayfa özeti (GET /api/rapor/ana-sayfa): panel, kanal eşikleri ve takip özeti sunucunun tek salt okunur anlık
/// görüntüsünden; her parça ayrı uçların yanıtıyla birebir aynıdır. Eski sunucuda uç yoksa istemci yalnız paneli doldurur:
/// <see cref="KasaEsikleri"/> ve <see cref="TakipOzeti"/> null kalır, çağıran onları eski uçlardan ayrıca yükler.</summary>
public record AnaSayfaDto(PanelDto Panel, IReadOnlyList<KasaEsikDto>? KasaEsikleri, TakipOzetDto? TakipOzeti);

// Mutasyon gövdeleri (Id sunucuda atanır; create'te gönderilmez)
public record KanalYaz(string Ad, bool Aktif, int Sira, decimal AcilisDevri);
/// <param name="IstekId">Yalnız oluşturmada tekrar anahtarı: zaman aşımından sonra aynı gövdeyle yeniden gönderilen gider
/// ikinci kez kaydedilmez (sunucu ilk kaydı döndürür). Düzenlemede gönderilmez.</param>
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null, Guid? IstekId = null);
public record GelenYaz(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiYaz(string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);
public record KartOdemeYaz(int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
public record AyarYaz(DateOnly TakipBaslangic, decimal KasaAcilisDevri);
