namespace Kasa.ApiClient;

// ------------------------------------------------------------------ Paket D: çek / senet

/// <summary>Kıymetli evrak türü. Senet çekle aynı vade ve kasa kurallarına tabidir.</summary>
public enum CekTuru { Cek, Senet }

/// <summary>Alınan evrakın konumu (kasaya etkisi yok). Verilen evrak her zaman Elde.</summary>
public enum CekKonumu { Elde, BankadaTahsilde, Teminatta, Icrada }

/// <summary>Tek dokunuşla durum: yalnız portföydeki evrakta; tarih null ise bugün (sunucu, Türkiye saati).</summary>
public record CekDurumYaz(CekDurumu Durum, DateOnly? Tarih = null, string? CiroEdilenCari = null);

/// <summary>Risk dağılımında bir keşideci ya da banka; Oran 0–1.</summary>
public record CekRiskKalemiDto(string Ad, decimal Tutar, int Adet, decimal Oran);

/// <summary>Portföydeki alınan evrakın keşideci ve bankaya göre dağılımı (tutara göre azalan).</summary>
public record CekRiskDto(decimal Toplam, int Adet, IReadOnlyList<CekRiskKalemiDto> Kesideciler, IReadOnlyList<CekRiskKalemiDto> Bankalar);

// ------------------------------------------------------------------ Paket D: kasa sayımı

public enum SayimSatirTuru { Nakit, Banka, Pos, Diger }

/// <summary>Sayım farkının durumu (fark sıfırsa anlamsız).</summary>
public enum SayimFarkDurumu { Acik, Aciklandi, KabulEdildi }

/// <summary>Küpür adedi; Kurus küpürün kuruş değeri (20000 = 200 TL).</summary>
public record KupurAdetDto(int Kurus, int Adet);

/// <summary>Sayım satırı; küpürler yalnız nakitte ve toplamı satır tutarına eşit.</summary>
public record SayimSatiriDto(SayimSatirTuru Tur, string Ad, decimal Tutar, IReadOnlyList<KupurAdetDto>? Kupurler = null);

public record SayimFarkYaz(SayimFarkDurumu Durum, string? Aciklama);

/// <summary>Sayımdan sonra yazılmış, sayım gününü etkileyen geçmiş satırı.</summary>
public record SayimDegisikligiDto(int Id, DateTime ZamanUtc, string Rol, string Tur, string Eylem, string Ozet);

/// <summary>"Neden değişti?": Degisim = GuncelHesaplanan − HesaplananTutar (takvim dışıysa null).</summary>
public record NedenDegistiDto(int SayimId, DateOnly Tarih, decimal HesaplananTutar, decimal? GuncelHesaplanan,
    decimal? Degisim, IReadOnlyList<SayimDegisikligiDto> Degisiklikler);

/// <summary>Son kasa sayımı (tarihe göre); hiç sayım yoksa alanlar null.</summary>
public record SonSayimDto(DateOnly? Tarih, int? GecenGun, int? SayimId);

// ------------------------------------------------------------------ Paket D: tekrarlayan gider

/// <summary>Tekrar sıklığı; ilk ay şablonun başlangıç ayıdır.</summary>
public enum TekrarSikligi { Aylik, UcAylik, AltiAylik, Yillik }

/// <summary>Atlanan (geri açılabilir) tekrarlayan gider ayı.</summary>
public record TekrarlayanAtlananDto(int TekrarlayanGiderId, string Kalem, string Kanal, DateOnly Ay, DateOnly Vade);

/// <summary>Hazır vergi/prim şablonu; Eklendi, aynı kalemle bir tekrarlayan gider zaten varsa true.</summary>
public record TekrarlayanHazirDto(string Kod, string Ad, string Aciklama, bool Eklendi);

// ------------------------------------------------------------------ Paket D: kart ekstresi mutabakatı

public enum KartMutabakatDurumu { Acik, Mutabik, FarkKabul }

/// <summary>Kapanmış ekstre dönemi ve varsa mutabakat özeti (Fark = Ekstre − Hesaplanan).</summary>
public record KartDonemDto(DateOnly Baslangic, DateOnly Kesim, DateOnly SonOdeme, decimal HesaplananBorc,
    int? MutabakatId, decimal? EkstreTutari, decimal? Fark, KartMutabakatDurumu? Durum);

public record KartMutabakatIslemDto(int Id, DateOnly Tarih, string Cari, decimal Tutar, string? Not, bool Tikli);
public record KartMutabakatOdemeDto(int Id, DateOnly Tarih, decimal Tutar, string? Not);

/// <summary>Bir dönemin mutabakat ekranı (HesaplananBorc bugünkü kayıtlarla dönem sonu borcu).</summary>
public record KartMutabakatDetayDto(
    int KrediKartiId, string KartAdi, DateOnly Baslangic, DateOnly Kesim, DateOnly SonOdeme,
    decimal DevredenBorc, decimal DonemHarcama, decimal DonemOdeme, decimal HesaplananBorc,
    IReadOnlyList<KartMutabakatIslemDto> Islemler, IReadOnlyList<KartMutabakatOdemeDto> Odemeler,
    int? MutabakatId, decimal? EkstreTutari, decimal? Fark, decimal? TiksizToplam, string? Not,
    KartMutabakatDurumu? Durum, decimal? KayittakiHesaplanan);

public record KartMutabakatYaz(int KrediKartiId, DateOnly Kesim, decimal EkstreTutari,
    IReadOnlyList<int>? TikliIslemIdleri, string? Not, bool FarkKabul);
