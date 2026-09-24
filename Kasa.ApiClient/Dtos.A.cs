namespace Kasa.ApiClient;

// Paket A — panel, nakit tahmini ve bildirim DTO'ları (sunucu: Kasa.Api/PanelDtos.cs, Kasa.Core/NakitTahmini.cs).

/// <summary>Nakit tahmini kaleminin kaynağı (sunucudaki TahminKalemTuru ile aynı adlar).</summary>
public enum TahminKalemTuru { AlinanCek, VerilenCek, KartOdemesi, TekrarlayanGider, IleriTarihliIslem, KartsizKrediKarti }

/// <summary>
/// Tahmindeki tek nakit hareketi. <see cref="Tutar"/> işaretlidir (+ giriş, − çıkış). <see cref="Tarih"/>
/// kalemin asıl günüdür; bugün ya da önceyse kalem hâlâ bekliyordur ve tahminde yarına yazılmıştır
/// (<see cref="Gecikmis"/>: asıl günü bugünden önce).
/// </summary>
public record TahminKalemiDto(DateOnly Tarih, TahminKalemTuru Tur, string Aciklama, decimal Tutar, int? CekId = null, bool Gecikmis = false);

/// <summary>Tahminin bir günü: girişler, çıkışlar ve gün sonundaki tahmini kasa.</summary>
public record TahminGunuDto(DateOnly Tarih, decimal Giris, decimal Cikis, decimal Kasa, IReadOnlyList<TahminKalemiDto> Kalemler);

/// <summary>
/// Gün gün nakit tahmini (GET /api/rapor/tahmin). <see cref="Gunler"/>[0] bugündür ve kasası panelin
/// güncel kasasıdır. <see cref="HaricKalemler"/>: hesaptan çıkarılan çeklerin ufuk içindeki kalemleri.
/// </summary>
public record NakitTahminDto(
    DateOnly Bugun, int Gun, decimal BaslangicKasa, IReadOnlyList<TahminGunuDto> Gunler,
    DateOnly EnDusukTarih, decimal EnDusukKasa, decimal SonKasa, decimal ToplamGiris, decimal ToplamCikis,
    IReadOnlyList<TahminKalemiDto> HaricKalemler);

/// <summary>Geleni girilmemiş, bitmiş bir dönem ve eksik kanalları (GET /api/gelenler/eksik).</summary>
public record EksikGelenDto(DateOnly DonemStart, DateOnly DonemEnd, IReadOnlyList<string> Kanallar);

/// <summary>Geçmişe dönük değişiklik satırının kısa hali.</summary>
public record GecmisOzetSatiriDto(int Id, DateTime ZamanUtc, string Tur, string Ozet);

/// <summary>
/// Değişiklik geçmişi özeti (GET /api/gecmis/ozet). <see cref="SonId"/>: en yeni satır (geçmiş boşsa 0);
/// <see cref="SonZamanUtc"/>: defterin son güncellenmesi. sonId verildiyse <see cref="Toplam"/> ondan
/// sonraki değişiklik sayısı, <see cref="GecmiseDonuk"/> bunlardan geçmiş ayları etkileyenler.
/// </summary>
public record GecmisOzetDto(int SonId, DateTime? SonZamanUtc, int Toplam, int GecmiseDonuk, IReadOnlyList<GecmisOzetSatiriDto> GecmiseDonukSatirlar);
