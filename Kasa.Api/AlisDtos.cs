using Kasa.Core;

namespace Kasa.Api;

public record AlisKanalDto(int Id, string Ad, bool Aktif);
public record AliciDto(int Id, string Kullanici, string Ad, bool Aktif);
public record AliciYaz(string Kullanici, string Ad, string? Sifre, bool Aktif = true);
public record AlisDagilimYaz(int KanalId, decimal Tutar);
public record AlisKalemYaz(string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimYaz> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
/// <param name="IstekId">Yalnız oluşturmada (POST) tekrar anahtarı: aynı kimlik ve aynı içerik ilk alışı döndürür. Eski istemci
/// göndermez (null): her istek yeni taslaktır. Düzenlemede (PUT) yok sayılır.</param>
public record AlisYaz(int Surum, DateOnly Tarih, string Tedarikci, string? Not, IReadOnlyList<AlisKalemYaz> Kalemler, int? TedarikciId = null, DateOnly? Vade = null, Guid? IstekId = null);
public record AlisDurumYaz(int Surum, string? Not = null);
public record AlisDagilimDto(int KanalId, string Kanal, decimal Tutar);
public record AlisKalemDto(int Id, string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimDto> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
/// <param name="EskiKartHarcamasi">Bağlı gider kartsız eski kredi kartı harcaması (tip KrediKarti, kart yok): kart seçilmeden
/// yapılan düzeltmede bu tip korunur. Eski sunucu göndermez (false).</param>
public record AlisOdemeDto(int Id, int IslemId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, bool DagilimBekliyor, IReadOnlyList<AlisDagilimDto> Dagilimlar, int? HesapId = null, string? KrediKartiAdi = null, bool EskiKartHarcamasi = false);
public record AlisDto(int Id, int Surum, int? AliciId, string Alici, DateOnly Tarih, string Tedarikci, string? Not, string Durum, string? EditorNotu, decimal Toplam, decimal Odenen, decimal Kalan, IReadOnlyList<AlisKalemDto> Kalemler, IReadOnlyList<AlisOdemeDto> Odemeler, int? TedarikciId = null, DateOnly? Vade = null);
public record AlisOdemeYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, int? MevcutIslemId = null, string? Not = null, int? HesapId = null);
public record AlisOdemeDuzelt(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId = null, int? HesapId = null, int? HedefAlisId = null, int? HedefSurum = null);
public record AlisOdemeIptal(int Surum, Guid IstekId, string Aciklama);
/// <summary>GET /api/alis/baglanabilir-giderler sayfası: tarih ve kimlik azalan sırada; <see cref="SonrakiImlec"/> bir sonraki
/// sayfanın imlecidir (son sayfada null, <see cref="DevamVar"/> false).</summary>
public record BaglanabilirGiderSayfasi(IReadOnlyList<BaglanabilirGiderDto> Ogeler, string? SonrakiImlec, bool DevamVar);
/// <summary>Ödemeye bağlanabilir gider: ödeme formunun mevcut gideri seçip tarih, tutar ve kartı doldurması için gereken alanlar.</summary>
public record BaglanabilirGiderDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, int? KanalId, GiderTipi Tip, string? Not, int? KrediKartiId);
