using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

// ------------------------------------------------------------------ Çek / senet (özellik 30, 43)

/// <summary>
/// Tek dokunuşla durum değişikliği (yalnız portföydeki evrakta): alınanda TahsilEdildi / CiroEdildi /
/// Karsiliksiz, verilende Odendi. Tarih verilmezse bugün (Türkiye); karşılıksızda tarih tutulmaz.
/// </summary>
public record CekDurumYazDto(CekDurumu Durum, DateOnly? Tarih = null, string? CiroEdilenCari = null);

/// <summary>Risk dağılımında bir satır: keşideci ya da banka; oran toplam içindeki pay (0–1, 4 basamak).</summary>
public record CekRiskKalemi(string Ad, decimal Tutar, int Adet, decimal Oran);

/// <summary>Portföydeki alınan evrakın keşideciye ve bankaya göre dağılımı (tutara göre azalan).</summary>
public record CekRiskDto(decimal Toplam, int Adet, IReadOnlyList<CekRiskKalemi> Kesideciler, IReadOnlyList<CekRiskKalemi> Bankalar);

// ------------------------------------------------------------------ Kasa sayımı (özellik 35)

/// <summary>Sayım satırının türü.</summary>
public enum SayimSatirTuru { Nakit, Banka, Pos, Diger }

/// <summary>Küpür adedi: <see cref="Kurus"/> küpürün kuruş değeri (20000 = 200 TL, 5 = 5 kuruş).</summary>
public record KupurAdetDto(int Kurus, int Adet);

/// <summary>Sayım satırı; küpürler yalnız nakit satırında olabilir ve toplamı satır tutarına eşit olmalıdır.</summary>
public record SayimSatiriDto(SayimSatirTuru Tur, string Ad, decimal Tutar, IReadOnlyList<KupurAdetDto>? Kupurler = null);

/// <summary>Sayım farkının durumu ve açıklaması.</summary>
public record SayimFarkYazDto(SayimFarkDurumu Durum, string? Aciklama);

/// <summary>Sayımdan sonra yazılmış, sayım gününü (ya da öncesini) etkileyen geçmiş satırı.</summary>
public record SayimDegisikligiDto(int Id, DateTime ZamanUtc, string Rol, string Tur, string Eylem, string Ozet);

/// <summary>
/// "Neden değişti?": sayım kaydedildikten sonra, tarihi sayım gününe eşit ya da önce olan kayıtlara
/// yapılan değişiklikler. Degisim = GuncelHesaplanan − HesaplananTutar (takvim dışıysa null).
/// </summary>
public record NedenDegistiDto(int SayimId, DateOnly Tarih, decimal HesaplananTutar, decimal? GuncelHesaplanan,
    decimal? Degisim, IReadOnlyList<SayimDegisikligiDto> Degisiklikler);

/// <summary>Son kasa sayımı (tarihe göre); hiç sayım yoksa alanlar null. GecenGun = bugün − son sayım tarihi.</summary>
public record SonSayimDto(DateOnly? Tarih, int? GecenGun, int? SayimId);

// ------------------------------------------------------------------ Tekrarlayan gider (özellik 33)

/// <summary>Atlanan (geri açılabilir) bir tekrarlayan gider ayı.</summary>
public record TekrarlayanAtlananDto(int TekrarlayanGiderId, string Kalem, string Kanal, DateOnly Ay, DateOnly Vade);

/// <summary>Hazır vergi/prim şablonu: Eklendi, aynı kalemle bir tekrarlayan gider zaten varsa true.</summary>
public record TekrarlayanHazirDto(string Kod, string Ad, string Aciklama, bool Eklendi);
public record TekrarlayanHazirYazDto(string Kod);

// ------------------------------------------------------------------ Kart ekstresi mutabakatı (özellik 34)

/// <summary>Kapanmış bir ekstre dönemi ve (varsa) mutabakatının özeti. Fark = Ekstre − Hesaplanan.</summary>
public record KartDonemDto(DateOnly Baslangic, DateOnly Kesim, DateOnly SonOdeme, decimal HesaplananBorc,
    int? MutabakatId, decimal? EkstreTutari, decimal? Fark, KartMutabakatDurumu? Durum);

/// <summary>Dönemdeki karta bağlı harcama; Tikli, mutabakatta ekstrede görüldü diye işaretlendiyse.</summary>
public record KartMutabakatIslemDto(int Id, DateOnly Tarih, string Cari, decimal Tutar, string? Not, bool Tikli);

/// <summary>Dönemdeki kart ödemesi.</summary>
public record KartMutabakatOdemeDto(int Id, DateOnly Tarih, decimal Tutar, string? Not);

/// <summary>
/// Bir dönemin mutabakat ekranı. HesaplananBorc bugünkü kayıtlarla dönem sonu borcudur
/// (kart sayfasındaki borç hesabının aynısı); KayittakiHesaplanan, mutabakat kaydedildiği andaki değerdir.
/// </summary>
public record KartMutabakatDetayDto(
    int KrediKartiId, string KartAdi, DateOnly Baslangic, DateOnly Kesim, DateOnly SonOdeme,
    decimal DevredenBorc, decimal DonemHarcama, decimal DonemOdeme, decimal HesaplananBorc,
    IReadOnlyList<KartMutabakatIslemDto> Islemler, IReadOnlyList<KartMutabakatOdemeDto> Odemeler,
    int? MutabakatId, decimal? EkstreTutari, decimal? Fark, decimal? TiksizToplam, string? Not,
    KartMutabakatDurumu? Durum, decimal? KayittakiHesaplanan);

/// <summary>Mutabakat kaydı (kart + kesim başına tek; varsa güncellenir).</summary>
public record KartMutabakatYazDto(int KrediKartiId, DateOnly Kesim, decimal EkstreTutari,
    IReadOnlyList<int>? TikliIslemIdleri, string? Not, bool FarkKabul);
