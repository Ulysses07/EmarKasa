namespace Kasa.ApiClient;

public record AlisKanalDto(int Id, string Ad, bool Aktif);
public record AliciDto(int Id, string Kullanici, string Ad, bool Aktif);
public record AliciYaz(string Kullanici, string Ad, string? Sifre, bool Aktif = true);
public record AlisDagilimYaz(int KanalId, decimal Tutar);
public record AlisKalemYaz(string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimYaz> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
public record AlisYaz(int Surum, DateOnly Tarih, string Tedarikci, string? Not, IReadOnlyList<AlisKalemYaz> Kalemler, int? TedarikciId = null, DateOnly? Vade = null);
public record AlisDurumYaz(int Surum, string? Not = null);
public record AlisDagilimDto(int KanalId, string Kanal, decimal Tutar);
public record AlisKalemDto(int Id, string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimDto> Dagilimlar, decimal? Miktar = null, decimal? BirimFiyat = null);
public record AlisOdemeDto(int Id, int IslemId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, bool DagilimBekliyor, IReadOnlyList<AlisDagilimDto> Dagilimlar, int? HesapId = null, string? KrediKartiAdi = null);
public record AlisDto(int Id, int Surum, int? AliciId, string Alici, DateOnly Tarih, string Tedarikci, string? Not, string Durum, string? EditorNotu, decimal Toplam, decimal Odenen, decimal Kalan, IReadOnlyList<AlisKalemDto> Kalemler, IReadOnlyList<AlisOdemeDto> Odemeler, int? TedarikciId = null, DateOnly? Vade = null);
public record AlisOdemeYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, int? MevcutIslemId = null, string? Not = null, int? HesapId = null);
