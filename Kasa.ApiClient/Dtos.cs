namespace Kasa.ApiClient;

public enum GiderTipi { Cari, SabitGider, KrediKarti }

/// <summary>Cihaz: tanıdık cihaz belirteci (masaüstüne gövdede verilir; tarayıcıya HttpOnly çerezle gider, orada null).</summary>
public record LoginYanit(string Rol, string Token, string? Cihaz = null);

/// <summary>Surum (contract-6, KanalDto/IslemDto/GelenDto/AyarlarDto): kaydın iyimser eşzamanlılık sürümü; düzenlemede yazma gövdesiyle
/// geri gönderilir, kayıt arada değiştiyse sunucu 409 verir. Eski sunucu göndermez (0).</summary>
public record KanalDto(int Id, string Ad, bool Aktif, int Sira, decimal AcilisDevri, int Surum = 0);
public record IslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null, int? AlisId = null, bool DagilimBekliyor = false, int? AylikGiderOdemeId = null, int? EkstreKayitId = null,
    int Surum = 0);
public record GelenDto(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl, int? KanalId = null, bool EskiYinelenenGrup = false, int Surum = 0);
/// <summary>YeniTakip/Aktif: kart yeni takipte ve yeni kullanıma açık; yeni kredi kartı gideri yalnız böyle bir karta bağlanabilir
/// (K3). Eski sunucu göndermez: varsayılanlar eski kart gibidir (seçilemez).</summary>
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc, decimal GuncelBorc = 0m, decimal AcilisBorc = 0m, decimal HarcamaToplam = 0m, decimal OdemeToplam = 0m, decimal EkstreBorc = 0m,
    bool YeniTakip = false, bool Aktif = true);
/// <summary>Eski (takip öncesi) kredi listesinin (GET api/krediler) ve kart ödemesi listesinin (GET api/kartodemeler) biçimi.
/// Masaüstünde bu uçları çağıran metot yoktur; sunucu uçları sunmayı sürdürdüğü için sözleşme testleri (Kasa.Sozlesme.Tests:
/// SunucuKarsiliklari, KapsamTests, DtoEslesmeTests) yanıt türlerini bu kayıtlarla eşler ve alanlarını denetler.</summary>
public record KrediDto(int Id, string Ad, decimal CekilenTutar, DateOnly CekimTarihi, int TaksitSayisi, decimal AylikOdeme, int OdemeGunu, string Kanal, bool GerceklesmeTakibi = false);
public record KartOdemeDto(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
/// <summary>IzleyiciSifreKisa: kayıtlı izleyici şifresinin 12 karakter kuralına uymadığı bir girişte görüldü.
/// VekilUyarisi: sunucu güvenilmeyen kaynaktan vekil başlığı aldı (yanlış vekil ayarı). Eski sunucu ikisini de göndermez.</summary>
public record AyarlarDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, bool IzleyiciSifreVarMi,
    bool IzleyiciSifreKisa = false, string? VekilUyarisi = null, int Surum = 0);

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
/// görüntüsünden; her parça ayrı uçların yanıtıyla birebir aynıdır. Eski sunucuda uç yoksa (404) ya da uç sunucu hatası (5xx)
/// verirse istemci yalnız paneli (panel ucundan) doldurur; sunucu özeti ya da eşikleri hesaplayamayınca onları null da
/// gönderebilir. Her durumda null kalan <see cref="KasaEsikleri"/> ve <see cref="TakipOzeti"/>'ni çağıran eski uçlardan
/// ayrıca yükler ve hatasını ayrı gösterir.</summary>
/// <param name="TakipsizKayitlar">Takipte olmayan (geçişi yapılmamış) kartlar ve kalan taksidi olan eski krediler: ana sayfanın kalıcı
/// "hatırlatmalar sınırlı, geçiş yapın" uyarısı. Kayıt yoksa ya da eski sunucuda null.</param>
public record AnaSayfaDto(PanelDto Panel, IReadOnlyList<KasaEsikDto>? KasaEsikleri, TakipOzetDto? TakipOzeti, IReadOnlyList<TakipsizKayitDto>? TakipsizKayitlar = null);

// Mutasyon gövdeleri (Id sunucuda atanır; create'te gönderilmez)
// Surum (contract-6, KanalYaz/IslemYaz/GelenYaz/AyarYaz): düzenlemede okunan kaydın sürümü; her zaman gönderilir (oluşturmada sunucu
// yok sayar). Kayıt arada başka oturumda değiştiyse sunucu 409 verir ve kayıt değişmez. Gelirde dönem ve kanalın satırı yoksa 0.
public record KanalYaz(string Ad, bool Aktif, int Sira, decimal AcilisDevri, int Surum = 0);
/// <param name="IstekId">Yalnız oluşturmada tekrar anahtarı: zaman aşımından sonra aynı gövdeyle yeniden gönderilen gider
/// ikinci kez kaydedilmez (sunucu ilk kaydı döndürür). Düzenlemede gönderilmez.</param>
/// <param name="TaksitSayisi">Yalnız oluşturmada, yeni takipteki kartla girilen kart giderinin taksit sayısı (1–60); boşsa tek taksit.
/// Düzenlemede gönderilmez.</param>
/// <param name="IlkKesimTarihi">Yalnız oluşturmada, isteğe bağlı ilk taksidin ekstre kesimi.</param>
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null, Guid? IstekId = null,
    int? TaksitSayisi = null, DateOnly? IlkKesimTarihi = null, int Surum = 0);
public record GelenYaz(DateOnly DonemStart, string Kanal, decimal TutarTl, int Surum = 0);
public record AyarYaz(DateOnly TakipBaslangic, decimal KasaAcilisDevri, int Surum = 0);
