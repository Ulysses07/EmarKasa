namespace Kasa.ApiClient;

public record AlisKanalDto(int Id, string Ad, bool Aktif);
public record AliciDto(int Id, string Kullanici, string Ad, bool Aktif);
public record AliciYaz(string Kullanici, string Ad, string? Sifre, bool Aktif = true);
public record AlisDagilimYaz(int KanalId, decimal Tutar);
public record AlisKalemYaz(string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimYaz> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
/// <param name="IstekId">Yalnız oluşturmada tekrar anahtarı: aynı kimlik ve gövde ilk taslağı döndürür. Düzenlemede gönderilmez.</param>
public record AlisYaz(int Surum, DateOnly Tarih, string Tedarikci, string? Not, IReadOnlyList<AlisKalemYaz> Kalemler, int? TedarikciId = null, DateOnly? Vade = null, Guid? IstekId = null);
public record AlisDurumYaz(int Surum, string? Not = null);
public record AlisDagilimDto(int KanalId, string Kanal, decimal Tutar);
public record AlisKalemDto(int Id, string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimDto> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
/// <param name="EskiKartHarcamasi">Bağlı gider kartsız eski kredi kartı harcaması; eski sunucu göndermez (false).</param>
public record AlisOdemeDto(int Id, int IslemId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, bool DagilimBekliyor, IReadOnlyList<AlisDagilimDto> Dagilimlar, int? HesapId = null, string? KrediKartiAdi = null, bool EskiKartHarcamasi = false);
public record AlisDto(int Id, int Surum, int? AliciId, string Alici, DateOnly Tarih, string Tedarikci, string? Not, string Durum, string? EditorNotu, decimal Toplam, decimal Odenen, decimal Kalan, IReadOnlyList<AlisKalemDto> Kalemler, IReadOnlyList<AlisOdemeDto> Odemeler, int? TedarikciId = null, DateOnly? Vade = null);
/// <param name="MevcutKartHarcamaId">Takipli kartla ödemede önceden (ekstreden ya da elle) gidersiz girilmiş kart harcaması: yeni
/// harcama üretilmez, ödeme ona bağlanır. Tarih, tutar ve kart harcamayla aynı gönderilir; MevcutIslemId ile birlikte gönderilmez.</param>
/// <param name="TaksitSayisi">Yeni takipteki kartla yeni ödemede kart harcamasının taksit sayısı (1–60); boşsa tek taksit. Mevcut gider ya
/// da kart harcaması bağlanırken gönderilmez. Eski sunucu alanı yok sayar (tek taksit).</param>
/// <param name="IlkKesimTarihi">İsteğe bağlı ilk taksidin ekstre kesimi (kartın kesim gününe en çok 7 gün uzak, ödemeden önce değil).</param>
public record AlisOdemeYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, int? MevcutIslemId = null, string? Not = null, int? HesapId = null, int? MevcutKartHarcamaId = null,
    int? TaksitSayisi = null, DateOnly? IlkKesimTarihi = null);
/// <summary>Alış ödemesine bağlanabilecek giderlerin bir sayfası (tarih ve kimlik azalan). <see cref="SonrakiImlec"/> sonraki
/// sayfanın imlecidir; son sayfada null.</summary>
public record BaglanabilirGiderSayfasi(IReadOnlyList<BaglanabilirGiderDto> Ogeler, string? SonrakiImlec, bool DevamVar);
/// <summary>Ödemeye bağlanabilir gider (yalnız seçim ve ödeme formunu doldurmak için gereken alanlar). <see cref="EkstreKayitId"/>:
/// banka ekstresinden gelen giderin kaynak satırı (bağlanınca satır eşleşmeye döner); eski sunucuda null.</summary>
public record BaglanabilirGiderDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, int? KanalId, GiderTipi Tip, string? Not, int? KrediKartiId, int? EkstreKayitId = null);
/// <summary>Takipli kartla ödemeye bağlanabilecek, gidere bağlı olmayan kart harcaması (ters sıra: ekstre önce işlenmiş).
/// <see cref="EkstreKayitId"/> harcamayı üreten ekstre satırı.</summary>
public record BaglanabilirKartHarcamasiDto(int Id, int KrediKartiId, DateOnly Tarih, string Aciklama, decimal Tutar, int? EkstreKayitId = null);
